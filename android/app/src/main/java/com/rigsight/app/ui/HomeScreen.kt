package com.rigsight.app.ui

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
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Bluetooth
import androidx.compose.material.icons.rounded.History
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Settings
import androidx.compose.material.icons.rounded.SportsEsports
import androidx.compose.material.icons.rounded.Wifi
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.rigsight.app.net.ActiveSessionInfo
import com.rigsight.app.net.ConnectionManager
import com.rigsight.app.net.ConnectionUiState
import com.rigsight.app.net.LinkPhase
import com.rigsight.app.net.LinkTransport
import com.rigsight.app.protocol.GameDto
import com.rigsight.app.protocol.PromptInfo
import com.rigsight.app.protocol.SessionSummary
import com.rigsight.app.protocol.TelemetrySample
import com.rigsight.app.ui.theme.RigColors
import com.rigsight.app.util.formatDuration
import com.rigsight.app.util.fpsText
import com.rigsight.app.util.metric
import com.rigsight.app.util.toEpochMillis
import kotlinx.coroutines.delay

@Composable
fun HomeScreen(
    manager: ConnectionManager,
    connection: ConnectionUiState,
    active: ActiveSessionInfo?,
    latest: TelemetrySample?,
    chartSamples: List<TelemetrySample>,
    prompt: PromptInfo?,
    games: List<GameDto>,
    sessions: List<SessionSummary>,
    onOpenLive: () -> Unit,
    onOpenHistory: () -> Unit,
    onOpenDevices: () -> Unit,
    onOpenSession: (String) -> Unit,
    onOpenSettings: () -> Unit,
) {
    val hasToken = manager.settings.hasToken
    val paired = hasToken || connection.phase == LinkPhase.Connected

    LazyColumn(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(start = 20.dp, end = 20.dp, top = 18.dp, bottom = 28.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        item {
            PageHeader(
                eyebrow = "RigSight",
                title = if (paired) "Dashboard" else "Your rig, in reach.",
                subtitle = connectionSubtitle(connection, hasToken),
                actions = {
                    IconButton(onClick = onOpenSettings) {
                        Icon(Icons.Rounded.Settings, contentDescription = "Settings", tint = RigColors.TextDim)
                    }
                },
            )
        }

        if (!paired) {
            item { WelcomeCard(onOpenDevices) }
        }

        if (connection.phase == LinkPhase.PairingRequired) {
            item {
                NoticeBanner(
                    text = connection.lastError ?: "Pair again with the six-digit code on your PC.",
                    tone = BannerTone.Error,
                )
            }
        } else if (connection.phase == LinkPhase.Connecting && !connection.lastError.isNullOrBlank()) {
            item {
                NoticeBanner(
                    text = connection.lastError ?: "Connecting…",
                    tone = BannerTone.Warn,
                )
            }
        }

        if (connection.phase == LinkPhase.Connected) {
            item { PcStatusCard(connection, onOpenDevices) }
        }

        if (connection.phase == LinkPhase.Connected && connection.transport == LinkTransport.Bluetooth) {
            item {
                NoticeBanner(
                    text = "Bluetooth fallback · 1 Hz updates. Connect to the same Wi-Fi for the full-rate feed.",
                    tone = BannerTone.Warn,
                )
            }
        }

        if (connection.phase == LinkPhase.Disconnected && hasToken) {
            item {
                ReconnectCard(
                    pcName = connection.pcName,
                    error = connection.lastError,
                    onReconnect = { manager.connect() },
                    onDevices = onOpenDevices,
                )
            }
        }

        prompt?.let { pending ->
            item {
                PromptCard(
                    prompt = pending,
                    onStart = { manager.startPrompt(pending.id) },
                    onDecline = { manager.declinePrompt(pending.id) },
                )
            }
        }

        active?.let { session ->
            item {
                ActiveSessionCard(
                    session = session,
                    latest = latest,
                    samples = chartSamples,
                    onOpenLive = onOpenLive,
                    onOpenHistory = onOpenHistory,
                )
            }
        }

        if (sessions.isNotEmpty()) {
            item {
                SectionHeader(
                    title = "Recent sessions",
                    action = "View all",
                    onAction = onOpenHistory,
                )
            }
            items(sessions.take(3), key = { it.id }) { summary ->
                SessionRow(summary) { onOpenSession(summary.id) }
            }
        }

        if (games.isNotEmpty()) {
            item { SectionLabel("Game library", modifier = Modifier.padding(top = 6.dp)) }
            items(games, key = { it.id }) { game ->
                GameRow(game)
            }
        }

        if (paired && sessions.isEmpty() && active == null) {
            item {
                EmptyState(
                    icon = Icons.Rounded.History,
                    title = "No sessions yet",
                    body = "Launch a game on your PC and accept the prompt. RigSight never records without you.",
                )
            }
        }

        if (connection.phase == LinkPhase.Connected || hasToken) {
            item {
                Text(
                    text = "Recording lives on your PC. Closing this app or locking the phone does not stop a session.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextFaint,
                    modifier = Modifier.padding(top = 4.dp),
                )
            }
        }
    }
}

@Composable
private fun WelcomeCard(onConnect: () -> Unit) {
    RigCard(accentBorder = RigColors.Mint.copy(alpha = 0.35f)) {
        SectionLabel("No overlay · no injection", color = RigColors.Mint)
        Spacer(Modifier.height(10.dp))
        Text(
            text = "See what your PC is doing while the game owns the screen.",
            style = MaterialTheme.typography.headlineSmall,
            color = RigColors.Text,
        )
        Spacer(Modifier.height(8.dp))
        Text(
            text = "Live FPS, CPU and GPU temperatures, watts and memory — streamed to this phone over your own network.",
            style = MaterialTheme.typography.bodyMedium,
            color = RigColors.TextDim,
        )
        Spacer(Modifier.height(16.dp))
        PrimaryButton(
            text = "Connect your PC",
            onClick = onConnect,
            icon = Icons.Rounded.Bluetooth,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(8.dp))
        Text(
            text = "Pair with the six-digit code shown in RigSight on the PC. Wi-Fi is preferred; Bluetooth works as a fallback.",
            style = MaterialTheme.typography.bodyMedium,
            color = RigColors.TextFaint,
        )
    }
}

@Composable
private fun PcStatusCard(connection: ConnectionUiState, onOpenDevices: () -> Unit) {
    val wifi = connection.transport == LinkTransport.Wifi
    RigCard(onClick = onOpenDevices) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Surface(
                color = if (wifi) RigColors.MintSoft else RigColors.VioletSoft,
                shape = CircleShape,
                modifier = Modifier.size(46.dp),
            ) {
                Icon(
                    imageVector = if (wifi) Icons.Rounded.Wifi else Icons.Rounded.Bluetooth,
                    contentDescription = null,
                    tint = if (wifi) RigColors.Mint else RigColors.Violet,
                    modifier = Modifier.padding(12.dp),
                )
            }
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                SectionLabel("Connected PC")
                Spacer(Modifier.height(3.dp))
                Text(
                    text = connection.pcName ?: "Your PC",
                    style = MaterialTheme.typography.titleLarge,
                    color = RigColors.Text,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = buildString {
                        append(if (wifi) "Wi-Fi" else "Bluetooth")
                        val host = connection.hosts.firstOrNull()
                        if (host != null) append(" · $host:${connection.port}")
                    },
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextDim,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Pill(if (wifi) "FULL RATE" else "1 HZ", color = if (wifi) RigColors.Mint else RigColors.Violet)
        }
    }
}

@Composable
private fun ReconnectCard(
    pcName: String?,
    error: String?,
    onReconnect: () -> Unit,
    onDevices: () -> Unit,
) {
    RigCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Surface(color = RigColors.AmberSoft, shape = CircleShape, modifier = Modifier.size(44.dp)) {
                Icon(
                    Icons.Rounded.Refresh,
                    contentDescription = null,
                    tint = RigColors.Amber,
                    modifier = Modifier.padding(11.dp),
                )
            }
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = pcName ?: "Your PC",
                    style = MaterialTheme.typography.titleLarge,
                    color = RigColors.Text,
                )
                Text(
                    text = error ?: "Not connected. The PC may be asleep or off the network.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextDim,
                )
            }
        }
        Spacer(Modifier.height(14.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            PrimaryButton("Reconnect", onReconnect, modifier = Modifier.weight(1f), icon = Icons.Rounded.Refresh)
            SecondaryButton("Devices", onDevices, modifier = Modifier.weight(1f))
        }
    }
}

@Composable
private fun PromptCard(prompt: PromptInfo, onStart: () -> Unit, onDecline: () -> Unit) {
    RigCard(accentBorder = RigColors.Mint.copy(alpha = 0.55f)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Surface(color = RigColors.MintSoft, shape = CircleShape, modifier = Modifier.size(42.dp)) {
                Icon(
                    Icons.Rounded.SportsEsports,
                    contentDescription = null,
                    tint = RigColors.Mint,
                    modifier = Modifier.padding(10.dp),
                )
            }
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                SectionLabel("Game detected", color = RigColors.Mint)
                Spacer(Modifier.height(3.dp))
                Text(
                    text = prompt.gameName,
                    style = MaterialTheme.typography.titleLarge,
                    color = RigColors.Text,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Pill("WAITING", color = RigColors.Mint, dot = true)
        }
        Spacer(Modifier.height(12.dp))
        Text(
            text = "Start a telemetry session? Your PC records; this phone only watches.",
            style = MaterialTheme.typography.bodyMedium,
            color = RigColors.TextDim,
        )
        Spacer(Modifier.height(14.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            PrimaryButton("Start tracking", onStart, modifier = Modifier.weight(1.3f), icon = Icons.Rounded.PlayArrow)
            SecondaryButton("Not now", onDecline, modifier = Modifier.weight(1f))
        }
    }
}

@Composable
private fun ActiveSessionCard(
    session: ActiveSessionInfo,
    latest: TelemetrySample?,
    samples: List<TelemetrySample>,
    onOpenLive: () -> Unit,
    onOpenHistory: () -> Unit,
) {
    val elapsed = rememberElapsedMs(session.startedAt)
    RigCard(onClick = onOpenLive, accentBorder = RigColors.Mint.copy(alpha = 0.5f)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Pill("LIVE", color = RigColors.Mint, dot = true)
            Spacer(Modifier.width(8.dp))
            Text(
                text = formatDuration(elapsed),
                style = MaterialTheme.typography.labelLarge,
                color = RigColors.TextDim,
                modifier = Modifier.weight(1f),
            )
            Text(
                text = session.gameName,
                style = MaterialTheme.typography.titleMedium,
                color = RigColors.Text,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Spacer(Modifier.height(10.dp))
        Row(verticalAlignment = Alignment.Bottom) {
            Text(
                text = latest?.fps.fpsText() ?: "—",
                style = MaterialTheme.typography.displayMedium,
                color = RigColors.Text,
            )
            Spacer(Modifier.width(7.dp))
            Text(
                text = "FPS",
                style = MaterialTheme.typography.labelMedium,
                color = RigColors.TextDim,
                modifier = Modifier.padding(bottom = 8.dp),
            )
            Spacer(Modifier.weight(1f))
            Column(horizontalAlignment = Alignment.End) {
                Text(
                    text = latest?.low1Fps?.metric(0) ?: "—",
                    style = MaterialTheme.typography.titleLarge,
                    color = RigColors.Mint,
                )
                Text("1% low", style = MaterialTheme.typography.labelSmall, color = RigColors.TextFaint)
            }
        }
        FpsChart(
            samples = samples.takeLast(180),
            height = 74.dp,
            showLabels = false,
            modifier = Modifier.padding(top = 6.dp),
        )
        Spacer(Modifier.height(12.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            SecondaryButton("History", onOpenHistory, modifier = Modifier.weight(1f), icon = Icons.Rounded.History)
            PrimaryButton("Open live", onOpenLive, modifier = Modifier.weight(1.2f))
        }
    }
}

@Composable
private fun GameRow(game: GameDto) {
    RigCard(padding = PaddingValues(horizontal = 14.dp, vertical = 12.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Surface(color = RigColors.SurfaceHigh, shape = RoundedCornerShape(12.dp), modifier = Modifier.size(38.dp)) {
                Box(contentAlignment = Alignment.Center) {
                    Text(
                        text = game.name.take(1).uppercase().ifBlank { "?" },
                        style = MaterialTheme.typography.titleMedium,
                        color = RigColors.Mint,
                    )
                }
            }
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = game.name,
                    style = MaterialTheme.typography.titleMedium,
                    color = RigColors.Text,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = game.exeName,
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextFaint,
                    maxLines = 1,
                )
            }
            Text(
                text = "PROMPT",
                style = MaterialTheme.typography.labelSmall,
                color = RigColors.TextFaint,
            )
        }
    }
}

@Composable
private fun SectionHeader(title: String, action: String? = null, onAction: (() -> Unit)? = null) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 4.dp)) {
        Text(
            text = title,
            style = MaterialTheme.typography.titleLarge,
            color = RigColors.Text,
            modifier = Modifier.weight(1f),
        )
        if (action != null && onAction != null) {
            TextButton(onClick = onAction) {
                Text(action, style = MaterialTheme.typography.labelLarge, color = RigColors.Mint)
            }
        }
    }
}

private fun connectionSubtitle(state: ConnectionUiState, hasToken: Boolean): String = when {
    state.phase == LinkPhase.Connected && state.transport == LinkTransport.Wifi ->
        "Connected over Wi-Fi · ${state.pcName ?: "your PC"}"
    state.phase == LinkPhase.Connected -> "Bluetooth fallback · 1 Hz updates"
    state.phase == LinkPhase.PairingRequired -> "Pairing needed"
    state.phase == LinkPhase.Connecting -> "Reconnecting…"
    hasToken -> "PC offline"
    else -> "Not connected"
}

@Composable
fun rememberElapsedMs(startedAt: String): Long {
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(startedAt) {
        while (true) {
            now = System.currentTimeMillis()
            delay(1_000)
        }
    }
    val started = remember(startedAt) { startedAt.toEpochMillis() }
    return if (started > 0) (now - started).coerceAtLeast(0) else 0
}
