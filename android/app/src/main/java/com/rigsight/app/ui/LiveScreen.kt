package com.rigsight.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.History
import androidx.compose.material.icons.rounded.Stop
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.rigsight.app.net.ActiveSessionInfo
import com.rigsight.app.net.ConnectionUiState
import com.rigsight.app.net.LinkPhase
import com.rigsight.app.net.LinkTransport
import com.rigsight.app.protocol.TelemetrySample
import com.rigsight.app.ui.theme.RigColors
import com.rigsight.app.util.formatDuration
import com.rigsight.app.util.fpsText
import com.rigsight.app.util.mbToGbText
import com.rigsight.app.util.metric
import com.rigsight.app.util.percentText
import com.rigsight.app.util.tempText
import com.rigsight.app.util.wattsText

@Composable
fun LiveScreen(
    session: ActiveSessionInfo?,
    connection: ConnectionUiState,
    latest: TelemetrySample?,
    samples: List<TelemetrySample>,
    onBack: () -> Unit,
    onOpenHistory: () -> Unit,
    onStop: () -> Unit,
) {
    var confirmStop by remember { mutableStateOf(false) }
    val elapsed = rememberElapsedMs(session?.startedAt.orEmpty())

    LazyColumn(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(start = 20.dp, end = 20.dp, top = 18.dp, bottom = 28.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        item {
            PageHeader(
                eyebrow = "Live telemetry",
                title = session?.gameName ?: "Session",
                subtitle = session?.let {
                    "${formatDuration(elapsed)} · ${transportLabel(connection)}"
                },
                onBack = onBack,
                actions = {
                    if (session != null) {
                        Pill(
                            text = if (connection.transport == LinkTransport.Wifi) "LIVE" else "LIVE · 1 HZ",
                            color = if (connection.transport == LinkTransport.Wifi) RigColors.Mint else RigColors.Violet,
                            background = if (connection.transport == LinkTransport.Wifi) RigColors.MintSoft else RigColors.VioletSoft,
                            dot = true,
                        )
                    }
                },
            )
        }

        if (session == null) {
            item {
                RigCard {
                    Text(
                        text = "This session has ended.",
                        style = MaterialTheme.typography.titleLarge,
                        color = RigColors.Text,
                    )
                    Spacer(Modifier.height(6.dp))
                    Text(
                        text = "The PC finished recording and saved the summary. You can review the charts in History.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = RigColors.TextDim,
                    )
                    Spacer(Modifier.height(16.dp))
                    PrimaryButton(
                        text = "View history",
                        onClick = onOpenHistory,
                        icon = Icons.Rounded.History,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            }
            return@LazyColumn
        }

        if (connection.phase != LinkPhase.Connected) {
            item {
                NoticeBanner(
                    text = connection.lastError
                        ?: "Reconnecting… your PC keeps recording while the phone catches up.",
                    tone = BannerTone.Warn,
                )
            }
        }

        item {
            val window = samples.takeLast(240)
            val windowAvg = window.mapNotNull { it.fps }.average().takeIf { !it.isNaN() }
            RigCard(accentBorder = RigColors.Mint.copy(alpha = 0.4f)) {
                Row(verticalAlignment = Alignment.Bottom) {
                    Text(
                        text = latest?.fps.fpsText() ?: "—",
                        style = MaterialTheme.typography.displayLarge,
                        color = RigColors.Text,
                        maxLines = 1,
                    )
                    Spacer(Modifier.width(8.dp))
                    Text(
                        text = "FPS",
                        style = MaterialTheme.typography.labelMedium,
                        color = RigColors.TextDim,
                        modifier = Modifier.padding(bottom = 10.dp),
                    )
                    Spacer(Modifier.weight(1f))
                    Column(horizontalAlignment = Alignment.End) {
                        Text(
                            text = latest?.frameTimeMs?.let { "${it.metric(1)} ms" } ?: "—",
                            style = MaterialTheme.typography.titleMedium,
                            color = RigColors.Text,
                        )
                        Text(
                            text = "frame time",
                            style = MaterialTheme.typography.labelSmall,
                            color = RigColors.TextFaint,
                        )
                    }
                }

                Row(
                    modifier = Modifier.fillMaxWidth().padding(top = 12.dp),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    LowStat("1% low", latest?.low1Fps, Modifier.weight(1f))
                    LowStat("0.1% low", latest?.low01Fps, Modifier.weight(1f))
                    LowStat("window avg", windowAvg, Modifier.weight(1f))
                }

                FpsChart(
                    samples = window,
                    height = 150.dp,
                    modifier = Modifier.padding(top = 14.dp),
                )
            }
        }

        item {
            ThermalCard(latest)
        }

        item {
            MemoryCard(latest)
        }

        item {
            RigCard {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Surface(
                        color = if (connection.transport == LinkTransport.Wifi) RigColors.MintSoft else RigColors.VioletSoft,
                        shape = CircleShape,
                        modifier = Modifier.size(10.dp),
                    ) {}
                    Spacer(Modifier.width(8.dp))
                    Text(
                        text = linkDetail(connection),
                        style = MaterialTheme.typography.bodyMedium,
                        color = RigColors.TextDim,
                        modifier = Modifier.weight(1f),
                    )
                }
                Spacer(Modifier.height(14.dp))
                SecondaryButton(
                    text = "Stop session",
                    onClick = { confirmStop = true },
                    icon = Icons.Rounded.Stop,
                    danger = true,
                    modifier = Modifier.fillMaxWidth(),
                )
                Spacer(Modifier.height(8.dp))
                Text(
                    text = "Stopping ends recording on the PC too. Leaving this screen or locking the phone does not stop it.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextFaint,
                )
            }
        }
    }

    if (confirmStop) {
        AlertDialog(
            onDismissRequest = { confirmStop = false },
            containerColor = RigColors.SurfaceHigh,
            title = { Text("Stop tracking?", color = RigColors.Text) },
            text = {
                Text(
                    "The PC will finish this session and save the summary. The game keeps running.",
                    color = RigColors.TextDim,
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    confirmStop = false
                    onStop()
                }) {
                    Text("Stop", color = RigColors.Danger)
                }
            },
            dismissButton = {
                TextButton(onClick = { confirmStop = false }) {
                    Text("Keep recording", color = RigColors.Mint)
                }
            },
        )
    }
}

@Composable
private fun LowStat(label: String, value: Double?, modifier: Modifier = Modifier) {
    Column(modifier = modifier) {
        Text(
            text = value?.metric(0) ?: "—",
            style = MaterialTheme.typography.titleLarge,
            color = RigColors.Text,
            maxLines = 1,
        )
        Text(label, style = MaterialTheme.typography.labelSmall, color = RigColors.TextFaint)
    }
}

@Composable
private fun ThermalCard(sample: TelemetrySample?) {
    RigCard {
        SectionLabel("Thermals & power")
        Spacer(Modifier.height(14.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(18.dp)) {
            ThermalColumn(
                label = "CPU",
                accent = RigColors.Mint,
                temp = sample?.cpuTempC,
                power = sample?.cpuPowerW,
                load = sample?.cpuLoadPct,
                clock = sample?.cpuClockMhz,
                modifier = Modifier.weight(1f),
            )
            Box(
                Modifier
                    .width(1.dp)
                    .height(132.dp)
                    .background(RigColors.Outline.copy(alpha = 0.7f))
            )
            ThermalColumn(
                label = "GPU",
                accent = RigColors.Violet,
                temp = sample?.gpuTempC,
                power = sample?.gpuPowerW,
                load = sample?.gpuLoadPct,
                clock = sample?.gpuClockMhz,
                hotspot = sample?.gpuHotspotC,
                modifier = Modifier.weight(1f),
            )
        }
    }
}

@Composable
private fun ThermalColumn(
    label: String,
    accent: Color,
    temp: Double?,
    power: Double?,
    load: Double?,
    clock: Double?,
    modifier: Modifier = Modifier,
    hotspot: Double? = null,
) {
    Column(modifier = modifier) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Surface(color = accent, shape = CircleShape, modifier = Modifier.size(7.dp)) {}
            Spacer(Modifier.width(6.dp))
            Text(label, style = MaterialTheme.typography.labelMedium, color = accent)
        }
        Spacer(Modifier.height(8.dp))
        Text(
            text = temp.tempText(),
            style = MaterialTheme.typography.headlineMedium,
            color = tempColor(temp),
            maxLines = 1,
        )
        Spacer(Modifier.height(2.dp))
        Text(
            text = listOfNotNull(
                power.wattsText(),
                load.percentText(),
            ).joinToString(" · "),
            style = MaterialTheme.typography.bodyMedium,
            color = RigColors.TextDim,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        if (load != null) {
            Spacer(Modifier.height(8.dp))
            UsageBar(fraction = (load / 100.0).coerceIn(0.0, 1.0).toFloat(), color = accent)
        }
        Spacer(Modifier.height(8.dp))
        Text(
            text = clock?.let { "${it.metric(0)} MHz" } ?: "—",
            style = MaterialTheme.typography.labelMedium,
            color = RigColors.TextFaint,
        )
        if (hotspot != null) {
            Text(
                text = "hotspot ${hotspot.metric(0)} °C",
                style = MaterialTheme.typography.labelMedium,
                color = RigColors.TextFaint,
            )
        }
    }
}

@Composable
private fun MemoryCard(sample: TelemetrySample?) {
    RigCard {
        SectionLabel("Memory")
        Spacer(Modifier.height(12.dp))
        MemoryRow(
            label = "VRAM",
            used = sample?.vramUsedMb,
            total = sample?.vramTotalMb,
            color = RigColors.Violet,
        )
        Spacer(Modifier.height(12.dp))
        MemoryRow(
            label = "System RAM",
            used = sample?.ramUsedMb,
            total = sample?.ramTotalMb,
            color = RigColors.Mint,
        )
    }
}

@Composable
private fun MemoryRow(label: String, used: Double?, total: Double?, color: Color) {
    val fraction = if (used != null && total != null && total > 0) {
        (used / total).coerceIn(0.0, 1.0).toFloat()
    } else {
        0f
    }
    Column {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = label,
                style = MaterialTheme.typography.labelMedium,
                color = RigColors.TextDim,
                modifier = Modifier.weight(1f),
            )
            Text(
                text = if (used != null) {
                    "${used.mbToGbText()} / ${total.mbToGbText()}"
                } else {
                    "—"
                },
                style = MaterialTheme.typography.titleMedium,
                color = RigColors.Text,
            )
        }
        Spacer(Modifier.height(7.dp))
        UsageBar(fraction = fraction, color = color)
    }
}

@Composable
private fun UsageBar(fraction: Float, color: Color, height: Int = 6) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .height(height.dp)
            .background(RigColors.SurfaceHigher, RoundedCornerShape(50)),
    ) {
        if (fraction > 0f) {
            Box(
                modifier = Modifier
                    .fillMaxHeight()
                    .fillMaxWidth(fraction.coerceIn(0.02f, 1f))
                    .background(color.copy(alpha = 0.85f), RoundedCornerShape(50)),
            )
        }
    }
}

private fun tempColor(temp: Double?): Color = when {
    temp == null -> RigColors.Text
    temp >= 90 -> RigColors.Danger
    temp >= 80 -> RigColors.Amber
    else -> RigColors.Text
}

private fun transportLabel(connection: ConnectionUiState): String = when {
    connection.transport == LinkTransport.Wifi -> "Wi-Fi"
    connection.transport == LinkTransport.Bluetooth -> "Bluetooth · 1 Hz"
    else -> "reconnecting"
}

private fun linkDetail(connection: ConnectionUiState): String = when {
    connection.transport == LinkTransport.Wifi -> {
        val host = connection.hosts.firstOrNull()
        if (host != null) "Full-rate stream over Wi-Fi · $host:${connection.port}" else "Full-rate stream over Wi-Fi"
    }
    connection.transport == LinkTransport.Bluetooth ->
        "Bluetooth fallback · 1 Hz updates. Wi-Fi gives you the full-rate chart."
    else -> connection.lastError ?: "Not connected to your PC. Reconnecting…"
}
