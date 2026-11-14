package com.rigsight.app.service

import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.os.SystemClock
import android.widget.Toast
import com.rigsight.app.RigSightApp
import com.rigsight.app.data.SettingsStore
import com.rigsight.app.net.ConnectionManager
import com.rigsight.app.net.ConnectionUiState
import com.rigsight.app.net.LinkPhase
import com.rigsight.app.net.LinkTransport
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Keeps the transport alive independently of the UI. The PC records even if the
 * phone disappears; this service makes sure the phone prompt and control stay up
 * while the app is backgrounded or the screen is locked.
 */
class RigSightService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private lateinit var manager: ConnectionManager
    private lateinit var settings: SettingsStore
    private var lastConnectionText: String? = null

    private var lastSessionNotificationAt = 0L
    private var lastSessionNotificationId: String? = null
    private var promptExpiryJob: Job? = null

    override fun onCreate() {
        super.onCreate()
        val app = application as RigSightApp
        manager = app.container.manager
        settings = app.container.settings
        Notifications.ensureChannels(this)
        val notification = Notifications.connection(this, "Starting RigSight…", null)
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(
                Notifications.ID_CONNECTION,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE,
            )
        } else {
            startForeground(Notifications.ID_CONNECTION, notification)
        }
        observe()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ServiceActions.ACTION_START_PROMPT -> {
                val promptId = intent.getStringExtra(ServiceActions.EXTRA_PROMPT_ID).orEmpty()
                if (promptId.isNotBlank()) manager.startPrompt(promptId)
            }
            ServiceActions.ACTION_DECLINE_PROMPT -> {
                val promptId = intent.getStringExtra(ServiceActions.EXTRA_PROMPT_ID).orEmpty()
                if (promptId.isNotBlank()) manager.declinePrompt(promptId)
            }
            ServiceActions.ACTION_STOP_SESSION -> manager.stopActiveSession()
            ServiceActions.ACTION_RECONNECT -> manager.connect()
            ServiceActions.ACTION_DISCONNECT -> manager.disconnect()
            ServiceActions.ACTION_STOP_SERVICE -> {
                manager.disconnect()
                stopForeground(STOP_FOREGROUND_REMOVE)
                stopSelf()
                return START_NOT_STICKY
            }
            else -> manager.startIfNeeded()
        }

        // A system restart passes a null intent. Reconnect if the user had paired.
        if (intent == null) manager.startIfNeeded()
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onDestroy() {
        promptExpiryJob?.cancel()
        scope.cancel()
        manager.disconnect()
        super.onDestroy()
    }

    private fun observe() {
        scope.launch {
            manager.connection.collect { state ->
                val text = connectionText(state)
                if (text != lastConnectionText) {
                    lastConnectionText = text
                    Notifications.notifyConnection(this@RigSightService, text, state)
                }
                val session = manager.activeSession.value
                if (session != null && !state.wifiConnected && state.bluetoothConnected) {
                    Notifications.notifySession(
                        this@RigSightService,
                        session,
                        manager.latestSample.value,
                        state,
                    )
                }
            }
        }

        scope.launch {
            manager.prompt.collect { prompt ->
                promptExpiryJob?.cancel()
                promptExpiryJob = null
                if (prompt == null) {
                    Notifications.cancelPrompt(this@RigSightService)
                    return@collect
                }
                if (settings.notificationPrompt) {
                    Notifications.notifyPrompt(this@RigSightService, prompt)
                }
                val remaining = if (prompt.expiresAt > 0) {
                    prompt.expiresAt - System.currentTimeMillis()
                } else {
                    60_000
                }
                val wait = (remaining + 750).coerceIn(1_000, 180_000)
                promptExpiryJob = scope.launch {
                    delay(wait)
                    if (manager.prompt.value?.id == prompt.id) {
                        manager.clearPromptLocally(prompt.id)
                    }
                }
            }
        }

        scope.launch {
            manager.activeSession.collect { session ->
                if (session == null) {
                    Notifications.cancelSession(this@RigSightService)
                    lastSessionNotificationId = null
                    lastSessionNotificationAt = 0
                } else if (session.id != lastSessionNotificationId) {
                    lastSessionNotificationId = session.id
                    lastSessionNotificationAt = SystemClock.elapsedRealtime()
                    Notifications.notifySession(
                        this@RigSightService,
                        session,
                        manager.latestSample.value,
                        manager.connection.value,
                    )
                }
            }
        }

        scope.launch {
            manager.latestSample.collect { sample ->
                val session = manager.activeSession.value ?: return@collect
                val now = SystemClock.elapsedRealtime()
                if (now - lastSessionNotificationAt >= 5_000 || session.id != lastSessionNotificationId) {
                    lastSessionNotificationAt = now
                    lastSessionNotificationId = session.id
                    Notifications.notifySession(
                        this@RigSightService,
                        session,
                        sample,
                        manager.connection.value,
                    )
                }
            }
        }

        scope.launch {
            val app = application as RigSightApp
            manager.savedSessions.collect { summary ->
                if (settings.notificationSaved && !app.container.presence.liveVisible) {
                    Notifications.notifySaved(this@RigSightService, summary)
                }
            }
        }

        scope.launch {
            val presence = (application as RigSightApp).container.presence
            manager.notices.collect { message ->
                if (!presence.appForeground) {
                    Toast.makeText(this@RigSightService, message, Toast.LENGTH_SHORT).show()
                }
            }
        }
    }

    private fun connectionText(state: ConnectionUiState): String = when {
        state.phase == LinkPhase.Connected && state.transport == LinkTransport.Wifi ->
            "Connected to ${state.pcName ?: "your PC"} over Wi-Fi"
        state.phase == LinkPhase.Connected ->
            "Bluetooth fallback · ${state.pcName ?: "your PC"} (1 Hz updates)"
        state.phase == LinkPhase.PairingRequired ->
            state.lastError ?: "Pairing needed · open RigSight"
        state.phase == LinkPhase.Connecting ->
            state.lastError ?: "Connecting to your PC…"
        settings.hasToken ->
            "Disconnected · open RigSight to reconnect"
        else ->
            "Open RigSight to pair your PC"
    }
}
