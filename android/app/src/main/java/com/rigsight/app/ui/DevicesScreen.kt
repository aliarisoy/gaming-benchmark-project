package com.rigsight.app.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Bluetooth
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.DeleteOutline
import androidx.compose.material.icons.rounded.Link
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.Wifi
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import com.rigsight.app.net.BleDeviceInfo
import com.rigsight.app.net.ConnectionManager
import com.rigsight.app.net.ConnectionUiState
import com.rigsight.app.net.LinkPhase
import com.rigsight.app.net.LinkTransport
import com.rigsight.app.protocol.DEFAULT_WS_PORT
import com.rigsight.app.service.ServiceActions
import com.rigsight.app.ui.theme.RigColors
import com.rigsight.app.util.hasBlePermissions

@Composable
fun DevicesScreen(
    manager: ConnectionManager,
    connection: ConnectionUiState,
    devices: List<BleDeviceInfo>,
    scanning: Boolean,
    pairingError: String?,
    onBack: () -> Unit,
    onRequestBlePermissions: () -> Unit,
) {
    val context = LocalContext.current
    var permissionsGranted by remember { mutableStateOf(hasBlePermissions(context)) }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) {
        permissionsGranted = hasBlePermissions(context)
    }

    var pairTarget by remember { mutableStateOf<BleDeviceInfo?>(null) }
    var confirmForget by remember { mutableStateOf(false) }

    LaunchedEffect(connection.phase) {
        if (connection.phase == LinkPhase.Connected) pairTarget = null
    }

    LazyColumn(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(start = 20.dp, end = 20.dp, top = 18.dp, bottom = 28.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        item {
            PageHeader(
                eyebrow = "Devices",
                title = "Connect your PC",
                subtitle = if (manager.settings.hasToken) {
                    connection.pcName?.let { "Paired with $it" } ?: "Paired"
                } else {
                    "Pair once. RigSight reconnects after that."
                },
                onBack = onBack,
            )
        }

        if (!pairingError.isNullOrBlank()) {
            item { NoticeBanner(pairingError, tone = BannerTone.Error) }
        } else if (connection.phase == LinkPhase.PairingRequired) {
            item {
                NoticeBanner(
                    text = connection.lastError ?: "Pair again with a fresh six-digit code.",
                    tone = BannerTone.Error,
                )
            }
        } else if (connection.phase == LinkPhase.Connecting) {
            item {
                NoticeBanner(
                    text = connection.lastError ?: "Connecting to your PC…",
                    tone = BannerTone.Info,
                )
            }
        } else if (connection.phase == LinkPhase.Connected) {
            item {
                NoticeBanner(
                    text = "Connected to ${connection.pcName ?: "your PC"} over " +
                        if (connection.transport == LinkTransport.Wifi) "Wi-Fi." else "Bluetooth.",
                    tone = BannerTone.Info,
                )
            }
        }

        if (manager.settings.hasToken || connection.pcName != null) {
            item {
                RigCard {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Surface(
                            color = RigColors.MintSoft,
                            shape = CircleShape,
                            modifier = Modifier.size(44.dp),
                        ) {
                            Icon(
                                Icons.Rounded.CheckCircle,
                                contentDescription = null,
                                tint = RigColors.Mint,
                                modifier = Modifier.padding(11.dp),
                            )
                        }
                        Spacer(Modifier.width(12.dp))
                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = connection.pcName ?: "Paired PC",
                                style = MaterialTheme.typography.titleLarge,
                                color = RigColors.Text,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                            Text(
                                text = linkSummary(connection),
                                style = MaterialTheme.typography.bodyMedium,
                                color = RigColors.TextDim,
                            )
                        }
                        Pill(
                            text = when {
                                connection.phase == LinkPhase.Connected && connection.transport == LinkTransport.Wifi -> "WI-FI"
                                connection.phase == LinkPhase.Connected -> "BLUETOOTH"
                                connection.phase == LinkPhase.Connecting -> "CONNECTING"
                                else -> "OFFLINE"
                            },
                            color = when {
                                connection.phase == LinkPhase.Connected && connection.transport == LinkTransport.Wifi -> RigColors.Mint
                                connection.phase == LinkPhase.Connected -> RigColors.Violet
                                else -> RigColors.TextDim
                            },
                            background = when {
                                connection.phase == LinkPhase.Connected && connection.transport == LinkTransport.Wifi -> RigColors.MintSoft
                                connection.phase == LinkPhase.Connected -> RigColors.VioletSoft
                                else -> RigColors.SurfaceHigher
                            },
                        )
                    }
                    Spacer(Modifier.height(10.dp))
                    connection.hosts.firstOrNull()?.let {
                        LabelValueRow("IP address", "$it:${connection.port}")
                    }
                    manager.settings.bleAddress?.let {
                        LabelValueRow("Bluetooth", it)
                    }
                    Spacer(Modifier.height(14.dp))
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        if (connection.phase == LinkPhase.Connected) {
                            SecondaryButton(
                                text = "Disconnect",
                                onClick = { manager.disconnect() },
                                modifier = Modifier.weight(1f),
                            )
                        } else {
                            PrimaryButton(
                                text = "Reconnect",
                                onClick = { manager.connect() },
                                icon = Icons.Rounded.Refresh,
                                modifier = Modifier.weight(1f),
                            )
                        }
                        SecondaryButton(
                            text = "Forget PC",
                            onClick = { confirmForget = true },
                            danger = true,
                            modifier = Modifier.weight(1f),
                        )
                    }
                }
            }
        }

        item {
            RigCard {
                SectionLabel("Bluetooth pairing", color = RigColors.Violet)
                Spacer(Modifier.height(8.dp))
                Text(
                    text = "Scan for a PC running RigSight, then enter the six-digit code shown on its screen. Bluetooth also carries game prompts and 1 Hz fallback metrics.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextDim,
                )
                Spacer(Modifier.height(14.dp))
                if (!permissionsGranted) {
                    NoticeBanner(
                        text = "Nearby devices permission is needed to scan over Bluetooth. You can still connect with the IP address below.",
                        tone = BannerTone.Warn,
                    )
                    Spacer(Modifier.height(12.dp))
                    PrimaryButton(
                        text = "Allow nearby devices",
                        onClick = onRequestBlePermissions,
                        icon = Icons.Rounded.Bluetooth,
                        modifier = Modifier.fillMaxWidth(),
                    )
                } else {
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        PrimaryButton(
                            text = if (scanning) "Scanning…" else "Scan for PCs",
                            onClick = {
                                if (scanning) {
                                    manager.stopBleScan()
                                } else {
                                    manager.startBleScan()
                                }
                            },
                            icon = Icons.Rounded.Search,
                            enabled = !scanning,
                            modifier = Modifier.weight(1f),
                        )
                        if (scanning) {
                            SecondaryButton(
                                text = "Stop",
                                onClick = { manager.stopBleScan() },
                                modifier = Modifier.weight(0.6f),
                            )
                        }
                    }
                }
            }
        }

        if (scanning) {
            item {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier.padding(horizontal = 4.dp),
                ) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(16.dp),
                        strokeWidth = 2.dp,
                        color = RigColors.Violet,
                    )
                    Spacer(Modifier.width(10.dp))
                    Text(
                        text = "Looking for RigSight PCs nearby…",
                        style = MaterialTheme.typography.bodyMedium,
                        color = RigColors.TextDim,
                    )
                }
            }
        }

        items(devices, key = { it.address }) { device ->
            if (permissionsGranted) {
                BleDeviceCard(device) {
                    manager.pairingError.value = null
                    pairTarget = device
                }
            }
        }

        if (permissionsGranted && devices.isEmpty() && !scanning) {
            item {
                Text(
                    text = "No PCs found yet. Keep RigSight open on the PC and make sure Bluetooth is on.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextFaint,
                    modifier = Modifier.padding(horizontal = 4.dp),
                )
            }
        }

        item {
            ManualIpCard(manager = manager, connection = connection)
        }

        item {
            RigCard {
                SectionLabel("Good to know")
                Spacer(Modifier.height(8.dp))
                Tip("The PC and phone should be on the same Wi-Fi network for live charts.")
                Tip("The six-digit code expires after 60 seconds. Generate a fresh one if it lapses.")
                Tip("If the PC is unreachable, allow RigSight through Windows Firewall and check that the agent is running.")
            }
        }
    }

    pairTarget?.let { target ->
        PairCodeDialog(
            device = target,
            error = pairingError,
            onDismiss = {
                pairTarget = null
                manager.pairingError.value = null
            },
            onPair = { code -> manager.pairWithBluetooth(target.address, code) },
        )
    }

    if (confirmForget) {
        AlertDialog(
            onDismissRequest = { confirmForget = false },
            containerColor = RigColors.SurfaceHigh,
            title = { Text("Forget this PC?", color = RigColors.Text) },
            text = {
                Text(
                    "The phone drops its token and stops connecting. Sessions already synced stay on the phone. Pairing again needs a fresh code.",
                    color = RigColors.TextDim,
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    confirmForget = false
                    manager.forgetPc()
                    ServiceActions.stop(context)
                }) {
                    Text("Forget PC", color = RigColors.Danger)
                }
            },
            dismissButton = {
                TextButton(onClick = { confirmForget = false }) {
                    Text("Cancel", color = RigColors.TextDim)
                }
            },
        )
    }
}

@Composable
private fun BleDeviceCard(device: BleDeviceInfo, onPair: () -> Unit) {
    RigCard(padding = PaddingValues(horizontal = 14.dp, vertical = 12.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Surface(
                color = RigColors.VioletSoft,
                shape = CircleShape,
                modifier = Modifier.size(38.dp),
            ) {
                Icon(
                    Icons.Rounded.Bluetooth,
                    contentDescription = null,
                    tint = RigColors.Violet,
                    modifier = Modifier.padding(9.dp),
                )
            }
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = device.name,
                    style = MaterialTheme.typography.titleMedium,
                    color = RigColors.Text,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = "${device.address} · ${rssiLabel(device.rssi)}",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextFaint,
                    maxLines = 1,
                )
            }
            SecondaryButton(
                text = "Pair",
                onClick = onPair,
            )
        }
    }
}

@Composable
private fun ManualIpCard(manager: ConnectionManager, connection: ConnectionUiState) {
    var host by rememberSaveable { mutableStateOf(manager.settings.host.orEmpty()) }
    var port by rememberSaveable {
        mutableStateOf(manager.settings.port.takeIf { it in 1..65535 }?.toString() ?: DEFAULT_WS_PORT.toString())
    }
    var code by rememberSaveable { mutableStateOf("") }

    RigCard {
        SectionLabel("Connect by IP", color = RigColors.Mint)
        Spacer(Modifier.height(8.dp))
        Text(
            text = "Works without Bluetooth. Enter the IP address shown in RigSight on the PC. A code is only needed the first time you pair.",
            style = MaterialTheme.typography.bodyMedium,
            color = RigColors.TextDim,
        )
        Spacer(Modifier.height(14.dp))

        OutlinedTextField(
            value = host,
            onValueChange = { host = it },
            label = { Text("PC address") },
            placeholder = { Text("192.168.1.50") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
        )
        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            OutlinedTextField(
                value = port,
                onValueChange = { port = it.filter(Char::isDigit).take(5) },
                label = { Text("Port") },
                singleLine = true,
                modifier = Modifier.weight(0.7f),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            )
            OutlinedTextField(
                value = code,
                onValueChange = { code = it.filter(Char::isDigit).take(6) },
                label = { Text("Code (first time)") },
                singleLine = true,
                modifier = Modifier.weight(1.3f),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                visualTransformation = PasswordVisualTransformation(),
            )
        }
        Spacer(Modifier.height(14.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            PrimaryButton(
                text = if (connection.phase == LinkPhase.Connecting) "Connecting…" else "Connect",
                onClick = { manager.connectManual(host, port, code) },
                icon = Icons.Rounded.Link,
                enabled = host.isNotBlank() && connection.phase != LinkPhase.Connecting,
                modifier = Modifier.weight(1f),
            )
            if (connection.phase == LinkPhase.Connected) {
                SecondaryButton(
                    text = "Disconnect",
                    onClick = { manager.disconnect() },
                    modifier = Modifier.weight(1f),
                )
            }
        }
        if (connection.phase == LinkPhase.Connected) {
            Spacer(Modifier.height(10.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    Icons.Rounded.Wifi,
                    contentDescription = null,
                    tint = RigColors.Mint,
                    modifier = Modifier.size(16.dp),
                )
                Spacer(Modifier.width(6.dp))
                Text(
                    text = "Connected · ${connection.hosts.firstOrNull() ?: "PC"}:${connection.port}",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.Mint,
                )
            }
        }
    }
}

@Composable
private fun PairCodeDialog(
    device: BleDeviceInfo,
    error: String?,
    onDismiss: () -> Unit,
    onPair: (String) -> Unit,
) {
    var code by rememberSaveable { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = RigColors.SurfaceHigh,
        title = { Text("Pair with ${device.name}", color = RigColors.Text) },
        text = {
            Column {
                Text(
                    text = "Enter the six-digit code shown in RigSight on the PC.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextDim,
                )
                Spacer(Modifier.height(14.dp))
                OutlinedTextField(
                    value = code,
                    onValueChange = { code = it.filter(Char::isDigit).take(6) },
                    label = { Text("Pairing code") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                    visualTransformation = PasswordVisualTransformation(),
                )
                if (!error.isNullOrBlank()) {
                    Spacer(Modifier.height(10.dp))
                    Text(
                        text = error,
                        style = MaterialTheme.typography.bodyMedium,
                        color = RigColors.Danger,
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = { onPair(code) },
                enabled = code.length == 6,
            ) {
                Text(
                    "Pair",
                    color = if (code.length == 6) RigColors.Mint else RigColors.TextFaint,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("Cancel", color = RigColors.TextDim)
            }
        },
    )
}

@Composable
private fun Tip(text: String) {
    Row(modifier = Modifier.padding(vertical = 4.dp)) {
        Text("•", color = RigColors.TextFaint, modifier = Modifier.padding(end = 8.dp))
        Text(
            text = text,
            style = MaterialTheme.typography.bodyMedium,
            color = RigColors.TextDim,
        )
    }
}

private fun rssiLabel(rssi: Int): String = when {
    rssi >= -60 -> "near"
    rssi >= -75 -> "good signal"
    else -> "weak signal"
}

private fun linkSummary(connection: ConnectionUiState): String = when {
    connection.phase == LinkPhase.Connected && connection.transport == LinkTransport.Wifi -> {
        val host = connection.hosts.firstOrNull()
        if (host != null) "Wi-Fi · $host:${connection.port}" else "Wi-Fi · full rate"
    }
    connection.phase == LinkPhase.Connected -> "Bluetooth fallback · 1 Hz"
    connection.phase == LinkPhase.Connecting -> "Connecting…"
    connection.phase == LinkPhase.PairingRequired -> "Pairing needed"
    else -> "Offline · tap reconnect"
}
