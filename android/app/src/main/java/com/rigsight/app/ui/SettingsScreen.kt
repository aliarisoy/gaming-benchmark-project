package com.rigsight.app.ui

import android.content.Intent
import android.net.Uri
import android.os.PowerManager
import android.provider.Settings
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
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
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.BatterySaver
import androidx.compose.material.icons.rounded.ChevronRight
import androidx.compose.material.icons.rounded.DeleteOutline
import androidx.compose.material.icons.rounded.Edit
import androidx.compose.material.icons.rounded.LinkOff
import androidx.compose.material.icons.rounded.Notifications
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import com.rigsight.app.BuildConfig
import com.rigsight.app.net.ConnectionManager
import com.rigsight.app.net.ConnectionUiState
import com.rigsight.app.net.LinkPhase
import com.rigsight.app.protocol.PROTOCOL_VERSION
import com.rigsight.app.service.ServiceActions
import com.rigsight.app.ui.theme.RigColors
import com.rigsight.app.util.hasNotificationPermission

@Composable
fun SettingsScreen(
    manager: ConnectionManager,
    connection: ConnectionUiState,
    sessionCount: Int,
    onBack: () -> Unit,
    onOpenDevices: () -> Unit,
    onRequestNotificationPermission: () -> Unit,
) {
    val context = LocalContext.current
    val settings = manager.settings
    var autoConnect by remember { mutableStateOf(settings.autoConnect) }
    var keepScreenOn by remember { mutableStateOf(settings.keepScreenOn) }
    var notifyPrompt by remember { mutableStateOf(settings.notificationPrompt) }
    var notifySaved by remember { mutableStateOf(settings.notificationSaved) }
    var notificationsGranted by remember { mutableStateOf(hasNotificationPermission(context)) }
    var renameOpen by remember { mutableStateOf(false) }
    var clearHistoryOpen by remember { mutableStateOf(false) }
    var forgetOpen by remember { mutableStateOf(false) }

    val powerManager = context.getSystemService(PowerManager::class.java)
    var ignoringBattery by remember {
        mutableStateOf(powerManager?.isIgnoringBatteryOptimizations(context.packageName) == true)
    }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) {
        notificationsGranted = hasNotificationPermission(context)
        ignoringBattery = powerManager?.isIgnoringBatteryOptimizations(context.packageName) == true
    }

    LazyColumn(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(start = 20.dp, end = 20.dp, top = 18.dp, bottom = 28.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        item {
            PageHeader(
                eyebrow = "Settings",
                title = "Preferences",
                subtitle = connection.pcName ?: "This phone",
                onBack = onBack,
            )
        }

        item {
            RigCard {
                SectionLabel("Connection")
                Spacer(Modifier.height(6.dp))
                SettingSwitch(
                    title = "Auto-connect",
                    subtitle = "Reconnect to the last PC when the app opens.",
                    checked = autoConnect,
                    onCheckedChange = {
                        autoConnect = it
                        settings.autoConnect = it
                    },
                )
                SettingDivider()
                SettingAction(
                    title = "This phone's name",
                    value = settings.deviceName,
                    icon = Icons.Rounded.Edit,
                    onClick = { renameOpen = true },
                )
                SettingDivider()
                SettingAction(
                    title = "Paired PC",
                    value = if (settings.hasToken) {
                        settings.pcName ?: "Paired"
                    } else {
                        "Not paired"
                    },
                    icon = Icons.Rounded.ChevronRight,
                    onClick = onOpenDevices,
                    trailing = when {
                        connection.phase == LinkPhase.Connected -> "Connected"
                        connection.phase == LinkPhase.Connecting -> "Connecting"
                        else -> "Offline"
                    },
                )
            }
        }

        item {
            RigCard {
                SectionLabel("Notifications", color = RigColors.Violet)
                Spacer(Modifier.height(6.dp))
                if (!notificationsGranted) {
                    NoticeBanner(
                        text = "Android is blocking notifications. Game prompts and saved-session summaries cannot appear while the app is in the background.",
                        tone = BannerTone.Warn,
                    )
                    Spacer(Modifier.height(10.dp))
                    PrimaryButton(
                        text = "Allow notifications",
                        onClick = onRequestNotificationPermission,
                        icon = Icons.Rounded.Notifications,
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Spacer(Modifier.height(6.dp))
                }
                SettingSwitch(
                    title = "Game detected prompts",
                    subtitle = "Notify when a tracked game launches.",
                    checked = notifyPrompt,
                    onCheckedChange = {
                        notifyPrompt = it
                        settings.notificationPrompt = it
                    },
                )
                SettingDivider()
                SettingSwitch(
                    title = "Saved session summaries",
                    subtitle = "Notify when a recording finishes.",
                    checked = notifySaved,
                    onCheckedChange = {
                        notifySaved = it
                        settings.notificationSaved = it
                    },
                )
            }
        }

        item {
            RigCard {
                SectionLabel("Display")
                Spacer(Modifier.height(6.dp))
                SettingSwitch(
                    title = "Keep screen on while live",
                    subtitle = "Stops the live screen from sleeping during a session.",
                    checked = keepScreenOn,
                    onCheckedChange = {
                        keepScreenOn = it
                        settings.keepScreenOn = it
                    },
                )
            }
        }

        item {
            RigCard {
                SectionLabel("Background")
                Spacer(Modifier.height(6.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = "Battery optimization",
                            style = MaterialTheme.typography.titleMedium,
                            color = RigColors.Text,
                        )
                        Text(
                            text = if (ignoringBattery) {
                                "RigSight is exempt. Background prompts can arrive."
                            } else {
                                "Android may sleep the app and delay prompts. Exempt RigSight for reliable prompts."
                            },
                            style = MaterialTheme.typography.bodyMedium,
                            color = RigColors.TextDim,
                        )
                    }
                    Spacer(Modifier.width(10.dp))
                    SecondaryButton(
                        text = if (ignoringBattery) "Exempt" else "Fix",
                        onClick = {
                            if (!ignoringBattery) {
                                val request = Intent(
                                    Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS,
                                    Uri.parse("package:${context.packageName}"),
                                )
                                val started = runCatching { context.startActivity(request) }.isSuccess
                                if (!started) {
                                    runCatching {
                                        context.startActivity(
                                            Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
                                        )
                                    }
                                }
                            }
                        },
                        enabled = !ignoringBattery,
                        icon = Icons.Rounded.BatterySaver,
                    )
                }
                Spacer(Modifier.height(10.dp))
                Text(
                    text = "Recording happens on the PC. Even if this phone sleeps or the app is closed, the session keeps recording. The foreground service keeps prompts and history sync alive.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextFaint,
                )
            }
        }

        item {
            RigCard {
                SectionLabel("History")
                Spacer(Modifier.height(6.dp))
                SettingAction(
                    title = "Clear local history",
                    value = "$sessionCount saved on this phone",
                    icon = Icons.Rounded.DeleteOutline,
                    onClick = { clearHistoryOpen = true },
                    danger = true,
                )
                Spacer(Modifier.height(4.dp))
                Text(
                    text = "Clearing local history removes cached summaries and charts from this phone. Recordings on the PC stay untouched.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextFaint,
                )
            }
        }

        item {
            RigCard {
                SectionLabel("Pairing")
                Spacer(Modifier.height(6.dp))
                SettingAction(
                    title = "Forget this PC",
                    value = "Drops the paired token on this phone",
                    icon = Icons.Rounded.LinkOff,
                    onClick = { forgetOpen = true },
                    danger = true,
                )
                if (!settings.hasToken) {
                    Spacer(Modifier.height(6.dp))
                    Text(
                        text = "No PC is paired. Open Devices to pair over Bluetooth or by IP.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = RigColors.TextFaint,
                    )
                }
            }
        }

        item {
            RigCard {
                SectionLabel("About")
                Spacer(Modifier.height(8.dp))
                LabelValueRow("App version", BuildConfig.VERSION_NAME)
                LabelValueRow("Protocol", "v$PROTOCOL_VERSION")
                LabelValueRow("PC", settings.pcName ?: "Not paired")
                Spacer(Modifier.height(8.dp))
                Text(
                    text = "RigSight records on your PC and streams the numbers to this phone. No overlay touches the game.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextFaint,
                )
            }
        }
    }

    if (renameOpen) {
        RenameDialog(
            current = settings.deviceName,
            onDismiss = { renameOpen = false },
            onSave = {
                settings.deviceName = it
                renameOpen = false
            },
        )
    }

    if (clearHistoryOpen) {
        AlertDialog(
            onDismissRequest = { clearHistoryOpen = false },
            containerColor = RigColors.SurfaceHigh,
            title = { Text("Clear local history?", color = RigColors.Text) },
            text = {
                Text(
                    "Removes $sessionCount cached sessions from this phone. The PC keeps every recording and will re-sync summaries next time you connect.",
                    color = RigColors.TextDim,
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    clearHistoryOpen = false
                    manager.clearHistory()
                }) {
                    Text("Clear phone history", color = RigColors.Danger)
                }
            },
            dismissButton = {
                TextButton(onClick = { clearHistoryOpen = false }) {
                    Text("Cancel", color = RigColors.TextDim)
                }
            },
        )
    }

    if (forgetOpen) {
        AlertDialog(
            onDismissRequest = { forgetOpen = false },
            containerColor = RigColors.SurfaceHigh,
            title = { Text("Forget this PC?", color = RigColors.Text) },
            text = {
                Text(
                    "The phone drops its token and stops connecting. Pairing again needs a fresh six-digit code from the PC.",
                    color = RigColors.TextDim,
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    forgetOpen = false
                    manager.forgetPc()
                    ServiceActions.stop(context)
                }) {
                    Text("Forget PC", color = RigColors.Danger)
                }
            },
            dismissButton = {
                TextButton(onClick = { forgetOpen = false }) {
                    Text("Cancel", color = RigColors.TextDim)
                }
            },
        )
    }
}

@Composable
private fun SettingSwitch(
    title: String,
    subtitle: String,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable { onCheckedChange(!checked) }
            .padding(vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.titleMedium, color = RigColors.Text)
            Text(subtitle, style = MaterialTheme.typography.bodyMedium, color = RigColors.TextDim)
        }
        Spacer(Modifier.width(12.dp))
        Switch(
            checked = checked,
            onCheckedChange = onCheckedChange,
            colors = SwitchDefaults.colors(
                checkedThumbColor = RigColors.Ink,
                checkedTrackColor = RigColors.Mint,
                checkedBorderColor = RigColors.Mint,
                uncheckedThumbColor = RigColors.TextDim,
                uncheckedTrackColor = RigColors.SurfaceHigher,
                uncheckedBorderColor = RigColors.Outline,
            ),
        )
    }
}

@Composable
private fun SettingAction(
    title: String,
    value: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    onClick: () -> Unit,
    danger: Boolean = false,
    trailing: String? = null,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = title,
                style = MaterialTheme.typography.titleMedium,
                color = if (danger) RigColors.Danger else RigColors.Text,
            )
            Text(
                text = value,
                style = MaterialTheme.typography.bodyMedium,
                color = RigColors.TextDim,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        if (trailing != null) {
            Text(
                text = trailing,
                style = MaterialTheme.typography.labelMedium,
                color = RigColors.TextFaint,
                modifier = Modifier.padding(end = 6.dp),
            )
        }
        Icon(
            imageVector = icon,
            contentDescription = null,
            tint = if (danger) RigColors.Danger else RigColors.TextFaint,
            modifier = Modifier.size(20.dp),
        )
    }
}

@Composable
private fun SettingDivider() {
    Box(
        Modifier
            .fillMaxWidth()
            .height(1.dp)
            .background(RigColors.Outline.copy(alpha = 0.6f))
    )
}

@Composable
private fun RenameDialog(current: String, onDismiss: () -> Unit, onSave: (String) -> Unit) {
    var name by rememberSaveable { mutableStateOf(current) }
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = RigColors.SurfaceHigh,
        title = { Text("Phone name", color = RigColors.Text) },
        text = {
            Column {
                Text(
                    text = "This name appears in the PC's paired devices list.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextDim,
                )
                Spacer(Modifier.height(12.dp))
                OutlinedTextField(
                    value = name,
                    onValueChange = { name = it.take(40) },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Text),
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = { onSave(name.trim().ifBlank { current }) },
                enabled = name.isNotBlank(),
            ) {
                Text("Save", color = RigColors.Mint)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("Cancel", color = RigColors.TextDim)
            }
        },
    )
}
