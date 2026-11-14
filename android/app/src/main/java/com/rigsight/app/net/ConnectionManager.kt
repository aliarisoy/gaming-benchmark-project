package com.rigsight.app.net

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothManager
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.ContextCompat
import com.rigsight.app.data.HistoryStore
import com.rigsight.app.data.SettingsStore
import com.rigsight.app.protocol.APP_VERSION
import com.rigsight.app.protocol.DEFAULT_WS_PORT
import com.rigsight.app.protocol.MessageTypes
import com.rigsight.app.protocol.PROTOCOL_VERSION
import com.rigsight.app.protocol.PromptInfo
import com.rigsight.app.protocol.RigJson
import com.rigsight.app.protocol.SessionInfo
import com.rigsight.app.protocol.SessionSummary
import com.rigsight.app.protocol.TelemetrySample
import com.rigsight.app.protocol.WireMessage
import com.rigsight.app.util.toEpochMillis
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import java.net.URI
import java.util.concurrent.TimeUnit

/**
 * Owns the phone side of the link. Wi-Fi is preferred; Bluetooth carries pairing,
 * prompts, control and a 1 Hz fallback. All state mutations happen on the main
 * dispatcher so Compose and the foreground service can collect safely.
 */
class ConnectionManager(
    context: Context,
    val settings: SettingsStore,
    val history: HistoryStore,
) {
    private val appContext = context.applicationContext
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val http = OkHttpClient.Builder()
        .connectTimeout(6, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .pingInterval(15, TimeUnit.SECONDS)
        .build()

    val connection = MutableStateFlow(ConnectionUiState())
    val activeSession = MutableStateFlow<ActiveSessionInfo?>(null)
    val latestSample = MutableStateFlow<TelemetrySample?>(null)
    val chartSamples = MutableStateFlow<List<TelemetrySample>>(emptyList())
    val prompt = MutableStateFlow<PromptInfo?>(null)
    val games = MutableStateFlow<List<com.rigsight.app.protocol.GameDto>>(emptyList())
    val sessions = MutableStateFlow(history.getSessions())
    val detail = MutableStateFlow<SessionDetail?>(null)
    val detailLoading = MutableStateFlow(false)
    val bleDevices = MutableStateFlow<List<BleDeviceInfo>>(emptyList())
    val bleScanning = MutableStateFlow(false)
    val pairingError = MutableStateFlow<String?>(null)

    val notices = MutableSharedFlow<String>(
        extraBufferCapacity = 8,
        onBufferOverflow = BufferOverflow.DROP_OLDEST,
    )
    val savedSessions = MutableSharedFlow<SessionSummary>(
        extraBufferCapacity = 2,
        onBufferOverflow = BufferOverflow.DROP_OLDEST,
    )

    private val dismissedPrompts = ArrayDeque<String>()
    @Volatile private var running = false
    @Volatile private var terminalError = false
    private var loopJob: Job? = null
    @Volatile private var currentWs: WebSocket? = null
    @Volatile private var pendingAck: CompletableDeferred<WsResult>? = null
    @Volatile private var wsConnected = false
    @Volatile private var pendingCode: String? = null
    private var sampleSessionId: String? = null
    private var lastEndedId: String? = null
    private var requestedDetailId: String? = null

    private var ble: BleClient? = null
    private var bleLinkUp = false
    private var bleAuthenticated = false
    private var bleConnecting = false
    private var pairingInProgress = false
    private var scanner: BleScanner? = null
    private var scanJob: Job? = null

    // ---------------------------------------------------------------- lifecycle

    fun startIfNeeded() {
        if (running) {
            refreshConnection()
            return
        }
        if (settings.autoConnect && (settings.hasToken || settings.host != null)) {
            connect()
        } else {
            refreshConnection()
        }
    }

    fun connect() {
        running = true
        terminalError = false
        pairingError.value = null
        refreshConnection(error = null)
        wakeLoop()
    }

    fun disconnect() {
        running = false
        loopJob?.cancel()
        loopJob = null
        closeWs()
        closeBle()
        clearActiveSession()
        prompt.value = null
        pairingInProgress = false
        refreshConnection(error = null)
    }

    fun forgetPc() {
        disconnect()
        settings.forgetPc()
        games.value = emptyList()
        sessions.value = history.getSessions()
        connection.value = ConnectionUiState()
        notices.tryEmit("Forgot this PC. Pair again to reconnect.")
    }

    /** Manual IP pairing: host, port and (for a first pairing) the six-digit code. */
    fun connectManual(hostInput: String, portInput: String, code: String) {
        val parsed = parseAddress(hostInput, portInput)
        if (parsed == null) {
            notices.tryEmit("Enter the PC's IP address, for example 192.168.1.50.")
            return
        }
        val (host, port) = parsed
        val trimmedCode = code.trim()
        if (!settings.hasToken && trimmedCode.isEmpty()) {
            pairingError.value = "Enter the six-digit code shown on the PC."
            connection.value = connection.value.copy(
                phase = LinkPhase.PairingRequired,
                lastError = pairingError.value,
            )
            return
        }
        if (trimmedCode.isNotEmpty() && !trimmedCode.matches(Regex("^[0-9]{6}$"))) {
            pairingError.value = "Pairing codes are six digits."
            return
        }

        settings.host = host
        settings.port = port
        pendingCode = trimmedCode.ifEmpty { null }
        pairingError.value = null
        pairingInProgress = trimmedCode.isNotEmpty()
        running = true
        terminalError = false
        closeWs()
        refreshConnection(error = null)
        wakeLoop()
    }

    // ---------------------------------------------------------------- commands

    fun startPrompt(promptId: String) {
        val current = prompt.value
        if (current?.id != promptId) {
            notices.tryEmit("That prompt has expired.")
            return
        }
        if (sendMessage(WireMessage(type = MessageTypes.START_SESSION, promptId = promptId))) {
            notices.tryEmit("Starting ${current.gameName} on your PC…")
        }
    }

    fun declinePrompt(promptId: String) {
        if (sendMessage(WireMessage(type = MessageTypes.DECLINE_PROMPT, promptId = promptId))) {
            dismissPromptLocally(promptId)
        }
    }

    fun clearPromptLocally(promptId: String) {
        dismissPromptLocally(promptId)
    }

    fun stopActiveSession() {
        val session = activeSession.value
        if (session == null) {
            notices.tryEmit("No session is active.")
            return
        }
        if (sendMessage(WireMessage(type = MessageTypes.STOP_SESSION, sessionId = session.id))) {
            notices.tryEmit("Stopping tracking on your PC…")
        }
    }

    fun refreshHistory() {
        sessions.value = history.getSessions()
        if (wsConnected) {
            sendMessage(WireMessage(type = MessageTypes.LIST_SESSIONS))
        } else {
            notices.tryEmit("History sync needs a Wi-Fi connection.")
        }
    }

    fun requestDetail(id: String) {
        requestedDetailId = id
        detailLoading.value = true
        if (detail.value?.summary?.id != id) detail.value = null
        val cachedSummary = history.getSession(id)
        val cachedSamples = history.getSamples(id)
        if (cachedSummary != null) detail.value = SessionDetail(cachedSummary, cachedSamples)
        if (wsConnected) {
            if (!sendMessage(WireMessage(type = MessageTypes.GET_SESSION, sessionId = id))) {
                detailLoading.value = false
            }
        } else {
            detailLoading.value = false
            if (cachedSummary == null) notices.tryEmit("Session details need a Wi-Fi connection.")
        }
    }

    fun clearDetail() {
        detail.value = null
        detailLoading.value = false
        requestedDetailId = null
    }

    fun deleteSession(id: String) {
        history.deleteSession(id)
        sessions.value = history.getSessions()
    }

    fun clearHistory() {
        history.clearAll()
        sessions.value = emptyList()
        notices.tryEmit("Local history cleared. The PC keeps its copy.")
    }

    // ------------------------------------------------------------------ scanning

    @SuppressLint("MissingPermission")
    fun startBleScan() {
        if (!hasBleScanPermission()) {
            notices.tryEmit("Allow Nearby devices permission to scan for your PC.")
            return
        }
        val adapter = appContext.getSystemService(BluetoothManager::class.java)?.adapter
        if (adapter == null) {
            notices.tryEmit("This phone has no Bluetooth adapter. Use the IP address instead.")
            return
        }
        if (!adapter.isEnabled) {
            notices.tryEmit("Turn on Bluetooth, then scan again.")
            return
        }

        stopBleScan()
        bleDevices.value = emptyList()
        pairingError.value = null
        val bleScanner = BleScanner(appContext, object : BleScanner.Listener {
            override fun onDevice(device: android.bluetooth.BluetoothDevice, name: String?, rssi: Int) {
                val label = name?.takeIf { it.isNotBlank() } ?: runCatching { device.name }.getOrNull()
                val display = label?.takeIf { it.isNotBlank() } ?: "Nearby RigSight PC"
                val current = bleDevices.value.toMutableList()
                val existing = current.indexOfFirst { it.address == device.address }
                val entry = BleDeviceInfo(display, device.address, rssi)
                if (existing >= 0) current[existing] = entry else current.add(entry)
                bleDevices.value = current.sortedByDescending { it.rssi }.take(24)
            }

            override fun onError(message: String) {
                bleScanning.value = false
                notices.tryEmit(message)
            }
        })
        scanner = bleScanner
        if (bleScanner.start()) {
            bleScanning.value = true
            scanJob = scope.launch {
                delay(12_000)
                stopBleScan()
            }
        }
    }

    fun stopBleScan() {
        scanJob?.cancel()
        scanJob = null
        scanner?.stop()
        scanner = null
        bleScanning.value = false
    }

    fun pairWithBluetooth(address: String, code: String) {
        val trimmed = code.trim()
        if (!trimmed.matches(Regex("^[0-9]{6}$"))) {
            pairingError.value = "Enter the six-digit code shown on the PC."
            return
        }
        pairingError.value = null
        running = true
        terminalError = false
        pairingInProgress = true
        stopBleScan()
        refreshConnection(error = null)
        connectBle(address, trimmed, userInitiated = true)
    }

    // ------------------------------------------------------------------ Wi-Fi

    private fun startLoop() {
        if (!running) return
        if (loopJob?.isActive == true) return
        loopJob = scope.launch {
            var attempt = 0
            while (isActive && running) {
                if (terminalError) {
                    refreshConnection()
                    break
                }
                if (wsConnected) {
                    delay(1_500)
                    continue
                }

                val bleFallback = bleAuthenticated
                if (!bleLinkUp && !bleConnecting && settings.hasToken && settings.bleAddress != null) {
                    connectBle(settings.bleAddress!!, code = null, userInitiated = false)
                    delay(2_500)
                    attempt = 0
                }

                if (!settings.hasToken && pendingCode == null) {
                    running = false
                    refreshConnection()
                    break
                }

                val hosts = candidateHosts()
                if (hosts.isEmpty()) {
                    if (!bleFallback) {
                        updateConnecting("Waiting for your PC. Open RigSight on the PC.")
                    }
                    delay(if (bleFallback) 15_000 else backoff(attempt++))
                    continue
                }

                if (!bleFallback) {
                    updateConnecting(
                        if (activeSession.value != null) {
                            "Reconnecting… your PC is still recording."
                        } else {
                            "Connecting to ${settings.pcName ?: "your PC"}…"
                        }
                    )
                }
                when (val result = attemptWifi(hosts)) {
                    is WsResult.Success -> {
                        attempt = 0
                        refreshConnection(error = null)
                    }
                    is WsResult.Terminal -> {
                        terminalError = true
                        pendingCode = null
                        if (result.clearToken) settings.token = ""
                        pairingInProgress = false
                        refreshConnection(error = result.message)
                        break
                    }
                    is WsResult.Retry -> {
                        if (!bleFallback) updateConnecting(result.message)
                        delay(if (bleFallback) 15_000 else backoff(attempt++))
                    }
                }
            }
        }
    }

    private fun wakeLoop() {
        if (!running) return
        loopJob?.cancel()
        loopJob = null
        startLoop()
    }

    private suspend fun attemptWifi(hosts: List<String>): WsResult {
        var lastMessage = "Could not reach your PC. Check Wi-Fi and that RigSight is running."
        for (rawHost in hosts) {
            val host = normalizeHost(rawHost)
            if (host.isBlank()) continue
            when (val result = attemptWifiHost(host, settings.port)) {
                is WsResult.Success -> {
                    settings.host = host
                    return result
                }
                is WsResult.Terminal -> return result
                is WsResult.Retry -> lastMessage = result.message
            }
        }
        return WsResult.Retry(lastMessage)
    }

    @SuppressLint("SetTextI18n")
    private suspend fun attemptWifiHost(host: String, port: Int): WsResult {
        val ack = CompletableDeferred<WsResult>()
        pendingAck = ack
        val request = try {
            Request.Builder().url("ws://$host:$port/v1/stream").build()
        } catch (_: Exception) {
            pendingAck = null
            return WsResult.Retry("That PC address is not valid.")
        }

        val socket = try {
            http.newWebSocket(request, createWsListener(host, port, ack))
        } catch (ex: Exception) {
            pendingAck = null
            return WsResult.Retry(ex.message ?: "Could not open a Wi-Fi connection.")
        }
        currentWs = socket

        var result: WsResult = WsResult.Retry("Connection closed before the PC answered.")
        try {
            result = withTimeoutOrNull(15_000) { ack.await() }
                ?: WsResult.Retry("The PC did not answer. Check the IP and Wi-Fi network.")
        } finally {
            if (pendingAck === ack) pendingAck = null
            if (result !is WsResult.Success) {
                if (currentWs === socket) currentWs = null
                wsConnected = false
                socket.cancel()
            }
        }
        return result
    }

    private fun createWsListener(host: String, port: Int, ack: CompletableDeferred<WsResult>) =
        object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                scope.launch {
                    if (currentWs !== webSocket) {
                        webSocket.cancel()
                        return@launch
                    }
                    val hello = WireMessage(
                        type = MessageTypes.HELLO,
                        protocolVersion = PROTOCOL_VERSION,
                        token = settings.token.takeIf { it.isNotBlank() },
                        code = pendingCode,
                        deviceName = settings.deviceName,
                        appVersion = APP_VERSION,
                    )
                    webSocket.send(RigJson.encodeToString(WireMessage.serializer(), hello))
                }
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                scope.launch { handleWsMessage(webSocket, text, host, ack) }
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                val reason = friendlyError(t)
                scope.launch { handleWsGone(webSocket, reason, ack) }
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                val message = when {
                    code == 4401 -> "Pairing is no longer valid. Pair again on the PC."
                    reason.isNotBlank() -> reason
                    else -> "The PC closed the connection."
                }
                scope.launch { handleWsGone(webSocket, message, ack) }
            }
        }

    private fun handleWsMessage(
        webSocket: WebSocket,
        text: String,
        host: String,
        ack: CompletableDeferred<WsResult>,
    ) {
        if (currentWs !== webSocket) return
        val message = runCatching {
            RigJson.decodeFromString(WireMessage.serializer(), text)
        }.getOrNull() ?: return

        if (message.type == MessageTypes.HELLO_ACK) {
            if (message.protocolVersion != null && message.protocolVersion != PROTOCOL_VERSION) {
                terminalError = true
                ack.complete(
                    WsResult.Terminal(
                        "RigSight protocol mismatch. Update the phone app and the PC agent.",
                        clearToken = false,
                    )
                )
                return
            }
            wsConnected = true
            currentWs = webSocket
            applyAck(message, host)
            if (ack.isActive) ack.complete(WsResult.Success)
            refreshConnection(error = null)
            return
        }
        handleCommon(message)
    }

    private fun handleWsGone(webSocket: WebSocket, message: String, ack: CompletableDeferred<WsResult>) {
        val wasCurrent = currentWs === webSocket
        if (wasCurrent) {
            currentWs = null
            wsConnected = false
        }
        if (ack.isActive) ack.complete(WsResult.Retry(message))
        if (wasCurrent && !terminalError && running) {
            refreshConnection(error = message)
        }
    }

    // ------------------------------------------------------------------ BLE

    @SuppressLint("MissingPermission")
    private fun connectBle(address: String, code: String?, userInitiated: Boolean) {
        if (!hasBleConnectPermission()) {
            notices.tryEmit("Allow Nearby devices permission to connect over Bluetooth.")
            if (userInitiated) pairingError.value = "Nearby devices permission is required."
            pairingInProgress = false
            return
        }
        val adapter = appContext.getSystemService(BluetoothManager::class.java)?.adapter
        if (adapter == null || !adapter.isEnabled) {
            if (userInitiated) {
                pairingError.value = "Turn on Bluetooth to pair over Bluetooth. You can also use the IP address."
            }
            return
        }
        val device = try {
            adapter.getRemoteDevice(address)
        } catch (ex: Exception) {
            if (userInitiated) pairingError.value = "That Bluetooth device is no longer available."
            return
        }

        closeBle()
        bleConnecting = true
        pairingInProgress = userInitiated
        var client: BleClient? = null

        val listener = object : BleClient.Listener {
            override fun onConnected(deviceName: String?, address: String) {
                if (ble !== client) return
                bleLinkUp = true
                if (address.isNotBlank()) settings.bleAddress = address
                refreshConnection()
            }

            override fun onDisconnected() {
                if (ble !== client) return
                val wasAuthenticated = bleAuthenticated
                ble = null
                bleLinkUp = false
                bleAuthenticated = false
                bleConnecting = false
                if (pairingInProgress) pairingInProgress = false
                refreshConnection(
                    error = if (wasAuthenticated) "Bluetooth disconnected. Reconnecting…" else connection.value.lastError
                )
                if (running && !wsConnected && settings.hasToken) {
                    scope.launch {
                        delay(1_500)
                        if (loopJob?.isActive != true) startLoop() else wakeLoop()
                    }
                }
            }

            override fun onMessage(json: String) {
                if (ble !== client) return
                val message = runCatching {
                    RigJson.decodeFromString(WireMessage.serializer(), json)
                }.getOrNull() ?: return
                handleBleMessage(message)
            }

            override fun onError(message: String) {
                if (ble !== client) return
                bleConnecting = false
                if (pairingInProgress) {
                    pairingInProgress = false
                    pairingError.value = message
                }
                refreshConnection(error = message)
            }
        }

        client = BleClient(appContext, scope, listener)
        ble = client
        client.connect(device, code, settings.token, settings.deviceName)
    }

    @SuppressLint("MissingPermission")
    private fun closeBle() {
        try { ble?.close() } catch (_: Exception) { }
        ble = null
        bleLinkUp = false
        bleAuthenticated = false
        bleConnecting = false
    }

    private fun handleBleMessage(message: WireMessage) {
        when (message.type) {
            MessageTypes.BLE_HELLO_ACK -> {
                bleConnecting = false
                if (message.ok == true) {
                    if (message.protocolVersion != null && message.protocolVersion != PROTOCOL_VERSION) {
                        pairingInProgress = false
                        pairingError.value = "RigSight protocol mismatch. Update the app and the PC agent."
                        refreshConnection(error = pairingError.value)
                        return
                    }
                    if (!message.token.isNullOrBlank()) settings.token = message.token!!
                    if (!settings.hasToken) {
                        pairingInProgress = false
                        pairingError.value = "The PC did not issue a device token."
                        refreshConnection(error = pairingError.value)
                        return
                    }
                    if (!message.pcName.isNullOrBlank()) settings.pcName = message.pcName
                    if (!message.pcId.isNullOrBlank()) settings.pcId = message.pcId
                    if (message.wsPort != null && message.wsPort in 1..65535) settings.port = message.wsPort!!
                    if (!message.wsHosts.isNullOrEmpty()) {
                        settings.host = message.wsHosts.first()
                        connection.value = connection.value.copy(
                            hosts = message.wsHosts.filter { it.isNotBlank() }
                        )
                    }

                    bleAuthenticated = true
                    bleLinkUp = true
                    running = true
                    pairingInProgress = false
                    pairingError.value = null
                    terminalError = false
                    refreshConnection(error = null)
                    notices.tryEmit("Paired with ${settings.pcName ?: "your PC"}.")
                    if (!wsConnected) wakeLoop()
                } else {
                    val reason = mapBleError(message.reason)
                    pairingInProgress = false
                    pairingError.value = reason
                    if (settings.hasToken && message.reason?.contains("token", ignoreCase = true) == true) {
                        settings.token = ""
                        terminalError = true
                        pairingError.value = "This PC revoked the pairing. Pair again with a fresh code."
                    }
                    refreshConnection(error = pairingError.value)
                    notices.tryEmit(pairingError.value ?: "Pairing failed.")
                    ble?.close()
                }
            }

            MessageTypes.DEVICE_INFO -> {
                if (!message.ok.orFalse()) refreshConnection()
            }

            else -> handleCommon(message)
        }
    }

    private fun mapBleError(reason: String?): String = when {
        reason.isNullOrBlank() -> "Pairing failed. Generate a new code on the PC and try again."
        reason.contains("Incorrect", ignoreCase = true) -> "Incorrect pairing code. Check the six digits on the PC."
        reason.contains("Too many", ignoreCase = true) -> "Too many attempts. Generate a new code on the PC."
        reason.contains("expired", ignoreCase = true) -> "The pairing code expired. Generate a new one on the PC."
        else -> reason
    }

    // -------------------------------------------------------------- messages

    private fun handleCommon(message: WireMessage) {
        when (message.type) {
            MessageTypes.PROMPT -> handlePrompt(message.prompt)
            MessageTypes.PROMPT_CLOSED -> {
                val id = message.promptId ?: return
                rememberDismissed(id)
                if (prompt.value?.id == id) prompt.value = null
            }
            MessageTypes.SESSION_STARTED -> message.session?.let { setActiveSession(it) }
            MessageTypes.SAMPLE -> message.sample?.let { handleSample(it) }
            MessageTypes.SESSION_ENDED -> message.summary?.let { handleSessionEnded(it) }
            MessageTypes.SESSIONS -> {
                val list = message.sessions ?: emptyList()
                history.upsertSummaries(list)
                sessions.value = history.getSessions()
            }
            MessageTypes.SESSION_DETAIL -> {
                val summary = message.summary ?: return
                if (requestedDetailId != null && requestedDetailId != summary.id) return
                val samples = message.samples ?: emptyList()
                history.saveDetail(summary, samples)
                detail.value = SessionDetail(summary, samples)
                detailLoading.value = false
            }
            MessageTypes.PONG -> Unit
            MessageTypes.ERROR -> handleWireError(message)
        }
    }

    private fun handlePrompt(info: PromptInfo?) {
        if (info == null || info.id.isBlank()) return
        if (dismissedPrompts.contains(info.id)) return
        if (info.expiresAt > 0 && info.expiresAt < System.currentTimeMillis()) return
        if (prompt.value?.id != info.id) prompt.value = info
    }

    private fun dismissPromptLocally(id: String) {
        rememberDismissed(id)
        if (prompt.value?.id == id) prompt.value = null
    }

    private fun rememberDismissed(id: String) {
        if (dismissedPrompts.contains(id)) return
        dismissedPrompts.addLast(id)
        while (dismissedPrompts.size > 16) dismissedPrompts.removeFirst()
    }

    private fun setActiveSession(info: SessionInfo) {
        val current = activeSession.value
        if (current?.id == info.id) return
        activeSession.value = ActiveSessionInfo.from(info)
        sampleSessionId = null
        latestSample.value = null
        chartSamples.value = emptyList()
        lastEndedId = null
        prompt.value = null
    }

    private fun handleSample(sample: TelemetrySample) {
        val session = activeSession.value ?: return
        if (sampleSessionId != session.id) {
            sampleSessionId = session.id
            latestSample.value = null
            chartSamples.value = emptyList()
        }
        val last = latestSample.value
        if (last != null && sample.t < last.t) return
        latestSample.value = sample

        val list = chartSamples.value.toMutableList()
        val tail = list.lastOrNull()
        if (tail != null && tail.t == sample.t) {
            list[list.lastIndex] = sample
        } else {
            list.add(sample)
            if (list.size > 600) list.subList(0, list.size - 600).clear()
        }
        chartSamples.value = list
    }

    private fun handleSessionEnded(summary: SessionSummary) {
        if (summary.id.isBlank() || lastEndedId == summary.id) return
        lastEndedId = summary.id
        if (activeSession.value?.id == summary.id) clearActiveSession()
        prompt.value = null
        history.upsertSummary(summary)
        sessions.value = history.getSessions()
        savedSessions.tryEmit(summary)
    }

    private fun handleWireError(message: WireMessage) {
        val code = message.code.orEmpty()
        val text = message.message?.takeIf { it.isNotBlank() } ?: "The PC reported an error."
        if (isAuthError(code) && !bleAuthenticated) {
            terminalError = true
            if (code != "protocol_mismatch") settings.token = ""
            val pending = pendingAck
            if (pending?.isActive == true) {
                pending.complete(WsResult.Terminal(text, clearToken = false))
            }
            wsConnected = false
            pairingInProgress = false
            refreshConnection(error = text)
        } else {
            val pending = pendingAck
            if (pending?.isActive == true) pending.complete(WsResult.Retry(text))
            detailLoading.value = false
            connection.value = connection.value.copy(lastError = text)
            notices.tryEmit(text)
        }
    }

    private fun isAuthError(code: String): Boolean = code in setOf(
        "unauthorized", "pair_failed", "protocol_mismatch", "bad_hello", "no_hello"
    )

    private fun applyAck(message: WireMessage, host: String?) {
        if (!message.token.isNullOrBlank()) settings.token = message.token!!
        if (!message.pcName.isNullOrBlank()) settings.pcName = message.pcName
        if (!message.pcId.isNullOrBlank()) settings.pcId = message.pcId
        if (message.games != null) games.value = message.games
        if (host != null) {
            settings.host = host
            connection.value = connection.value.copy(
                hosts = (connection.value.hosts + host).distinct()
            )
        }
        pendingCode = null
        terminalError = false
        pairingInProgress = false
        pairingError.value = null

        val session = message.session
        if (session != null) {
            setActiveSession(session)
        } else if (activeSession.value != null) {
            clearActiveSession()
        }
        refreshConnection(error = null)
        sendMessage(WireMessage(type = MessageTypes.LIST_SESSIONS))
    }

    // ------------------------------------------------------------------ state

    private fun updateConnecting(message: String) {
        connection.value = connection.value.copy(
            phase = LinkPhase.Connecting,
            transport = when {
                wsConnected -> LinkTransport.Wifi
                bleAuthenticated -> LinkTransport.Bluetooth
                else -> LinkTransport.None
            },
            wifiConnected = wsConnected,
            bluetoothConnected = bleAuthenticated,
            pcName = settings.pcName,
            pcId = settings.pcId,
            hosts = knownHosts(),
            port = settings.port,
            lastError = message,
            pairingInProgress = pairingInProgress,
        )
    }

    private fun refreshConnection(error: String? = connection.value.lastError) {
        val phase = when {
            wsConnected || bleAuthenticated -> LinkPhase.Connected
            terminalError -> LinkPhase.PairingRequired
            running -> LinkPhase.Connecting
            else -> LinkPhase.Disconnected
        }
        connection.value = ConnectionUiState(
            phase = phase,
            transport = when {
                wsConnected -> LinkTransport.Wifi
                bleAuthenticated -> LinkTransport.Bluetooth
                else -> LinkTransport.None
            },
            wifiConnected = wsConnected,
            bluetoothConnected = bleAuthenticated,
            pcName = settings.pcName,
            pcId = settings.pcId,
            hosts = knownHosts(),
            port = settings.port,
            lastError = if (wsConnected) null else error,
            pairingInProgress = pairingInProgress,
        )
    }

    private fun knownHosts(): List<String> {
        val fromBle = connection.value.hosts
        return (listOfNotNull(settings.host) + fromBle)
            .map { normalizeHost(it) }
            .filter { it.isNotBlank() }
            .distinct()
    }

    private fun clearActiveSession() {
        activeSession.value = null
        latestSample.value = null
        chartSamples.value = emptyList()
        sampleSessionId = null
    }

    private fun sendMessage(message: WireMessage): Boolean {
        val json = RigJson.encodeToString(WireMessage.serializer(), message)
        if (wsConnected) {
            if (currentWs?.send(json) == true) return true
        }
        if (bleAuthenticated) {
            ble?.send(json)
            return true
        }
        notices.tryEmit("Not connected to your PC. Reconnecting…")
        return false
    }

    private fun closeWs() {
        pendingAck?.cancel()
        pendingAck = null
        try { currentWs?.close(1000, "phone disconnected") } catch (_: Exception) { }
        try { currentWs?.cancel() } catch (_: Exception) { }
        currentWs = null
        wsConnected = false
    }

    // ----------------------------------------------------------------- helpers

    private fun candidateHosts(): List<String> =
        (listOfNotNull(settings.host) + connection.value.hosts)
            .map { normalizeHost(it) }
            .filter { it.isNotBlank() && it != "127.0.0.1" && !it.equals("localhost", true) }
            .distinct()
            .take(4)

    private fun normalizeHost(raw: String): String {
        var value = raw.trim()
        if (value.startsWith("ws://", true)) value = value.substring(5)
        if (value.startsWith("http://", true)) value = value.substring(7)
        if (value.startsWith("wss://", true)) value = value.substring(6)
        if (value.startsWith("https://", true)) value = value.substring(8)
        value = value.substringBefore('/')
        if (value.startsWith("[") && value.contains("]")) {
            value = value.substringAfter('[').substringBefore(']')
        } else if (value.count { it == ':' } == 1) {
            value = value.substringBefore(':')
        }
        return value.trim()
    }

    private fun parseAddress(hostInput: String, portInput: String): Pair<String, Int>? {
        val raw = hostInput.trim()
        if (raw.isEmpty()) return null
        if (raw.startsWith("ws://", true) || raw.startsWith("http://", true) ||
            raw.startsWith("wss://", true) || raw.startsWith("https://", true)
        ) {
            return try {
                val uri = URI(if (raw.startsWith("ws", true)) "http${raw.substring(2)}" else raw)
                val host = uri.host?.takeIf { it.isNotBlank() } ?: return null
                val port = if (uri.port in 1..65535) uri.port else portInput.toIntOrNull() ?: DEFAULT_WS_PORT
                host to port
            } catch (_: Exception) {
                null
            }
        }

        val normalized = normalizeHost(raw)
        if (normalized.isBlank()) return null
        val embeddedPort = raw.substringAfterLast(':', "").toIntOrNull()
        val port = if (raw.count { it == ':' } == 1) {
            embeddedPort?.takeIf { it in 1..65535 } ?: (portInput.toIntOrNull() ?: DEFAULT_WS_PORT)
        } else {
            portInput.toIntOrNull() ?: DEFAULT_WS_PORT
        }
        if (port !in 1..65535) return null
        return normalized to port
    }

    private fun backoff(attempt: Int): Long {
        val table = longArrayOf(2_000, 4_000, 8_000, 15_000, 30_000)
        return table[attempt.coerceIn(0, table.lastIndex)]
    }

    private fun friendlyError(t: Throwable): String = when (t) {
        is java.net.UnknownHostException -> "PC address not found. Check the IP."
        is java.net.ConnectException -> "The PC refused the connection. Is RigSight running?"
        is java.net.SocketTimeoutException -> "The PC did not answer. Check Wi-Fi."
        else -> t.message?.takeIf { it.isNotBlank() } ?: "Could not reach your PC."
    }

    private fun hasBleScanPermission(): Boolean {
        if (Build.VERSION.SDK_INT >= 31 &&
            ContextCompat.checkSelfPermission(appContext, Manifest.permission.BLUETOOTH_SCAN) !=
            PackageManager.PERMISSION_GRANTED
        ) return false
        if (Build.VERSION.SDK_INT <= 30 &&
            ContextCompat.checkSelfPermission(appContext, Manifest.permission.ACCESS_FINE_LOCATION) !=
            PackageManager.PERMISSION_GRANTED
        ) return false
        return true
    }

    private fun hasBleConnectPermission(): Boolean {
        if (Build.VERSION.SDK_INT >= 31 &&
            ContextCompat.checkSelfPermission(appContext, Manifest.permission.BLUETOOTH_CONNECT) !=
            PackageManager.PERMISSION_GRANTED
        ) return false
        return true
    }

    private fun Boolean?.orFalse(): Boolean = this == true

    private sealed interface WsResult {
        data object Success : WsResult
        data class Retry(val message: String) : WsResult
        data class Terminal(val message: String, val clearToken: Boolean) : WsResult
    }
}
