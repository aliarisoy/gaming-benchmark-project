package com.rigsight.app.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.History
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.rigsight.app.protocol.SessionSummary
import com.rigsight.app.ui.theme.RigColors
import com.rigsight.app.util.formatCompactDuration
import com.rigsight.app.util.fpsText

@Composable
fun HistoryScreen(
    sessions: List<SessionSummary>,
    connected: Boolean,
    onBack: () -> Unit,
    onRefresh: () -> Unit,
    onOpenSession: (String) -> Unit,
) {
    var gameFilter by rememberSaveable { mutableStateOf<String?>(null) }
    val gameNames = sessions.map { it.gameName }.filter { it.isNotBlank() }.distinct().sorted()
    val activeFilter = gameFilter?.takeIf { it in gameNames }
    val filtered = if (activeFilter == null) sessions else sessions.filter { it.gameName == activeFilter }

    LazyColumn(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(start = 20.dp, end = 20.dp, top = 18.dp, bottom = 28.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        item {
            PageHeader(
                eyebrow = "History",
                title = "Every session",
                subtitle = if (connected) {
                    "Synced from your PC · ${sessions.size} saved"
                } else {
                    "Showing the local copy · reconnect to sync"
                },
                onBack = onBack,
                actions = { RefreshAction(onRefresh) },
            )
        }

        if (sessions.isNotEmpty()) {
            item {
                HistoryStatsCard(filtered)
            }
        }

        if (gameNames.size > 1) {
            item {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    FilterPill(
                        text = "All games",
                        selected = activeFilter == null,
                        onClick = { gameFilter = null },
                    )
                    for (name in gameNames) {
                        FilterPill(
                            text = name,
                            selected = activeFilter == name,
                            onClick = { gameFilter = if (activeFilter == name) null else name },
                        )
                    }
                }
            }
        }

        if (filtered.isEmpty()) {
            item {
                EmptyState(
                    icon = Icons.Rounded.History,
                    title = if (sessions.isEmpty()) "No sessions yet" else "No sessions for this game",
                    body = if (sessions.isEmpty()) {
                        "Launch a game on your PC and start the prompt. Sessions appear here when they finish."
                    } else {
                        "Pick another game or clear the filter."
                    },
                    actionLabel = "Refresh",
                    onAction = onRefresh,
                )
            }
        } else {
            items(filtered, key = { it.id }) { summary ->
                SessionRow(summary) { onOpenSession(summary.id) }
            }
        }

        item {
            Text(
                text = "The PC keeps every raw sample. This phone caches summaries and downsampled charts for the sessions you open.",
                style = MaterialTheme.typography.bodyMedium,
                color = RigColors.TextFaint,
                modifier = Modifier.padding(top = 6.dp),
            )
        }
    }
}

@Composable
private fun HistoryStatsCard(sessions: List<SessionSummary>) {
    val totalMs = sessions.sumOf { it.durationMs }
    val bestFps = sessions.mapNotNull { it.avgFps }.maxOrNull()
    RigCard(padding = PaddingValues(vertical = 16.dp, horizontal = 12.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            HistoryStat(
                label = "Sessions",
                value = sessions.size.toString(),
                modifier = Modifier.weight(1f),
            )
            StatDivider()
            HistoryStat(
                label = "Tracked",
                value = formatCompactDuration(totalMs),
                modifier = Modifier.weight(1f),
            )
            StatDivider()
            HistoryStat(
                label = "Best avg",
                value = bestFps.fpsText(),
                modifier = Modifier.weight(1f),
            )
        }
    }
}

@Composable
private fun HistoryStat(label: String, value: String, modifier: Modifier = Modifier) {
    Column(modifier = modifier, horizontalAlignment = Alignment.CenterHorizontally) {
        Text(
            text = value,
            style = MaterialTheme.typography.headlineSmall,
            color = RigColors.Text,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Spacer(Modifier.height(2.dp))
        Text(label, style = MaterialTheme.typography.labelSmall, color = RigColors.TextFaint)
    }
}

@Composable
private fun StatDivider() {
    Surface(
        color = RigColors.Outline,
        modifier = Modifier.width(1.dp).height(30.dp),
    ) {}
}

@Composable
private fun FilterPill(text: String, selected: Boolean, onClick: () -> Unit) {
    Surface(
        color = if (selected) RigColors.MintSoft else RigColors.Surface,
        shape = RoundedCornerShape(50),
        border = BorderStroke(
            1.dp,
            if (selected) RigColors.Mint.copy(alpha = 0.5f) else RigColors.Outline,
        ),
        modifier = Modifier.clickable(onClick = onClick),
    ) {
        Text(
            text = text,
            style = MaterialTheme.typography.labelLarge,
            color = if (selected) RigColors.Mint else RigColors.TextDim,
            maxLines = 1,
            modifier = Modifier.padding(horizontal = 14.dp, vertical = 8.dp),
        )
    }
}
