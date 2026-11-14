package com.rigsight.app.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.DeleteOutline
import androidx.compose.material.icons.rounded.History
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
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
import androidx.compose.ui.unit.dp
import com.rigsight.app.net.SessionDetail
import com.rigsight.app.protocol.SessionSummary
import com.rigsight.app.protocol.TelemetrySample
import com.rigsight.app.ui.theme.RigColors
import com.rigsight.app.util.durationText
import com.rigsight.app.util.formatDuration
import com.rigsight.app.util.formatFullDate
import com.rigsight.app.util.fpsText
import com.rigsight.app.util.mbToGbText
import com.rigsight.app.util.metric
import com.rigsight.app.util.percentText
import com.rigsight.app.util.stopText
import com.rigsight.app.util.tempText
import com.rigsight.app.util.wattsText

@Composable
fun DetailScreen(
    summary: SessionSummary?,
    detail: SessionDetail?,
    loading: Boolean,
    connected: Boolean,
    onBack: () -> Unit,
    onSync: () -> Unit,
    onDelete: () -> Unit,
) {
    var confirmDelete by remember { mutableStateOf(false) }
    val shown = detail?.summary ?: summary
    val samples = if (detail != null && detail.summary.id == shown?.id) detail.samples else emptyList()

    LazyColumn(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(start = 20.dp, end = 20.dp, top = 18.dp, bottom = 28.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        item {
            PageHeader(
                eyebrow = "Session",
                title = shown?.gameName ?: "Session detail",
                subtitle = shown?.let {
                    "${formatFullDate(it.startedAt)} · ${it.durationText}"
                },
                onBack = onBack,
                actions = {
                    if (shown != null) {
                        IconButton(onClick = { confirmDelete = true }) {
                            Icon(
                                Icons.Rounded.DeleteOutline,
                                contentDescription = "Delete local copy",
                                tint = RigColors.TextDim,
                            )
                        }
                    }
                },
            )
        }

        if (shown == null) {
            item {
                if (loading) {
                    LoadingRow("Loading session from your PC…")
                } else {
                    EmptyState(
                        icon = Icons.Rounded.History,
                        title = "Session not available",
                        body = "This session is not cached on the phone yet. Sync it from the PC over Wi-Fi.",
                        actionLabel = if (connected) "Sync from PC" else null,
                        onAction = if (connected) onSync else null,
                    )
                }
            }
            return@LazyColumn
        }

        item {
            RigCard(accentBorder = RigColors.Mint.copy(alpha = 0.35f)) {
                Row(verticalAlignment = Alignment.Bottom) {
                    Text(
                        text = shown.avgFps.fpsText(),
                        style = MaterialTheme.typography.displayLarge,
                        color = RigColors.Text,
                        maxLines = 1,
                    )
                    Spacer(Modifier.width(8.dp))
                    Text(
                        text = "avg FPS",
                        style = MaterialTheme.typography.labelMedium,
                        color = RigColors.TextDim,
                        modifier = Modifier.padding(bottom = 10.dp),
                    )
                    Spacer(Modifier.weight(1f))
                    Pill(
                        text = shown.stopText.uppercase(),
                        color = RigColors.TextDim,
                        background = RigColors.SurfaceHigher,
                    )
                }
                Spacer(Modifier.height(16.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    DetailStat("Median", shown.medianFps.fpsText(), Modifier.weight(1f))
                    DetailStat("1% low", shown.low1Fps.fpsText(), Modifier.weight(1f))
                    DetailStat("0.1% low", shown.low01Fps.fpsText(), Modifier.weight(1f))
                    DetailStat(
                        "Frame",
                        shown.avgFrameTimeMs?.let { "${it.metric(1)} ms" } ?: "—",
                        Modifier.weight(1f),
                    )
                }
            }
        }

        item {
            RigCard {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(modifier = Modifier.weight(1f)) {
                        SectionLabel("Frame rate")
                        Spacer(Modifier.height(4.dp))
                        Text(
                            text = "FPS over the session",
                            style = MaterialTheme.typography.titleMedium,
                            color = RigColors.Text,
                        )
                    }
                    Pill("${shown.sampleCount} samples", color = RigColors.Mint)
                }
                FpsChart(
                    samples = samples,
                    height = 180.dp,
                    modifier = Modifier.padding(top = 10.dp),
                )
                if (samples.isEmpty()) {
                    Spacer(Modifier.height(8.dp))
                    SyncHint(loading = loading, connected = connected, onSync = onSync)
                }
            }
        }

        item {
            ChartCard(
                title = "Thermals",
                subtitle = "CPU and GPU package temperature",
                samples = samples,
                series = listOf(
                    ChartSeries("CPU", RigColors.Mint) { it.cpuTempC },
                    ChartSeries("GPU", RigColors.Violet) { it.gpuTempC },
                ),
                unit = "°C",
            )
        }

        item {
            ChartCard(
                title = "Power draw",
                subtitle = "Package power reported by the sensors",
                samples = samples,
                series = listOf(
                    ChartSeries("CPU", RigColors.Mint) { it.cpuPowerW },
                    ChartSeries("GPU", RigColors.Violet) { it.gpuPowerW },
                ),
                unit = "W",
            )
        }

        item {
            ChartCard(
                title = "Load",
                subtitle = "Average utilisation per poll",
                samples = samples,
                series = listOf(
                    ChartSeries("CPU", RigColors.Mint) { it.cpuLoadPct },
                    ChartSeries("GPU", RigColors.Violet) { it.gpuLoadPct },
                ),
                unit = "%",
                fixedMax = 100.0,
            )
        }

        item {
            RigCard {
                SectionLabel("Session facts")
                Spacer(Modifier.height(8.dp))
                LabelValueRow("Started", formatFullDate(shown.startedAt))
                LabelValueRow("Duration", shown.durationText)
                LabelValueRow("Samples recorded", shown.sampleCount.toString())
                LabelValueRow("Stop reason", shown.stopText)
                LabelValueRow("Peak CPU temp", shown.maxCpuTempC.tempText())
                LabelValueRow("Peak GPU temp", shown.maxGpuTempC.tempText())
                LabelValueRow("Peak CPU power", shown.maxCpuPowerW.wattsText())
                LabelValueRow("Peak GPU power", shown.maxGpuPowerW.wattsText())
                LabelValueRow("Avg CPU load", shown.avgCpuLoadPct.percentText())
                LabelValueRow("Avg GPU load", shown.avgGpuLoadPct.percentText())
                LabelValueRow("Peak VRAM", shown.maxVramUsedMb.mbToGbText())
            }
        }

        item {
            Text(
                text = "Charts are a downsampled view. The PC keeps the raw JSONL recording and is the source of truth.",
                style = MaterialTheme.typography.bodyMedium,
                color = RigColors.TextFaint,
                modifier = Modifier.padding(top = 4.dp),
            )
        }
    }

    if (confirmDelete && shown != null) {
        AlertDialog(
            onDismissRequest = { confirmDelete = false },
            containerColor = RigColors.SurfaceHigh,
            title = { Text("Delete local copy?", color = RigColors.Text) },
            text = {
                Text(
                    "Removes this session's cached summary and charts from the phone. The recording on your PC is not touched.",
                    color = RigColors.TextDim,
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    confirmDelete = false
                    onDelete()
                }) {
                    Text("Delete from phone", color = RigColors.Danger)
                }
            },
            dismissButton = {
                TextButton(onClick = { confirmDelete = false }) {
                    Text("Cancel", color = RigColors.TextDim)
                }
            },
        )
    }
}

@Composable
private fun DetailStat(label: String, value: String, modifier: Modifier = Modifier) {
    Column(modifier = modifier) {
        Text(
            text = value,
            style = MaterialTheme.typography.titleLarge,
            color = RigColors.Text,
            maxLines = 1,
        )
        Text(label, style = MaterialTheme.typography.labelSmall, color = RigColors.TextFaint)
    }
}

@Composable
private fun ChartCard(
    title: String,
    subtitle: String,
    samples: List<TelemetrySample>,
    series: List<ChartSeries>,
    unit: String,
    fixedMax: Double? = null,
) {
    RigCard {
        SectionLabel(title)
        Spacer(Modifier.height(4.dp))
        Text(subtitle, style = MaterialTheme.typography.bodyMedium, color = RigColors.TextDim)
        ChartLegend(
            items = series.map { it.label to it.color },
            modifier = Modifier.padding(top = 12.dp),
        )
        MultiHistoryChart(
            samples = samples,
            series = series,
            unit = unit,
            fixedMax = fixedMax,
            height = 170.dp,
            modifier = Modifier.padding(top = 6.dp),
        )
        if (samples.isEmpty()) {
            Spacer(Modifier.height(4.dp))
            Text(
                text = "No chart samples cached on this phone yet.",
                style = MaterialTheme.typography.bodyMedium,
                color = RigColors.TextFaint,
            )
        }
    }
}

@Composable
private fun SyncHint(loading: Boolean, connected: Boolean, onSync: () -> Unit) {
    if (loading) {
        LoadingRow("Syncing charts from your PC…")
        return
    }
    Surface(color = RigColors.SurfaceHigh, shape = RoundedCornerShape(14.dp)) {
        Column(modifier = Modifier.padding(14.dp)) {
            Text(
                text = if (connected) {
                    "Charts are not cached on this phone yet."
                } else {
                    "Charts need a Wi-Fi connection to the PC."
                },
                style = MaterialTheme.typography.bodyMedium,
                color = RigColors.TextDim,
            )
            if (connected) {
                Spacer(Modifier.height(10.dp))
                SecondaryButton(
                    text = "Sync charts",
                    onClick = onSync,
                    icon = Icons.Rounded.Refresh,
                )
            }
        }
    }
}
