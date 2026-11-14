package com.rigsight.app.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.rounded.ChevronRight
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.rigsight.app.protocol.SessionSummary
import com.rigsight.app.ui.theme.RigColors
import com.rigsight.app.util.dateText
import com.rigsight.app.util.durationText
import com.rigsight.app.util.fpsText

@Composable
fun RigCard(
    modifier: Modifier = Modifier,
    padding: PaddingValues = PaddingValues(18.dp),
    accentBorder: Color? = null,
    onClick: (() -> Unit)? = null,
    content: @Composable ColumnScope.() -> Unit,
) {
    Surface(
        modifier = modifier
            .fillMaxWidth()
            .then(if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier),
        color = RigColors.Surface,
        shape = RoundedCornerShape(22.dp),
        border = BorderStroke(1.dp, accentBorder ?: RigColors.Outline.copy(alpha = 0.75f)),
    ) {
        Column(modifier = Modifier.padding(padding), content = content)
    }
}

@Composable
fun SectionLabel(text: String, modifier: Modifier = Modifier, color: Color = RigColors.TextFaint) {
    Text(
        text = text.uppercase(),
        style = MaterialTheme.typography.labelSmall,
        color = color,
        modifier = modifier,
    )
}

@Composable
fun Pill(
    text: String,
    color: Color = RigColors.Mint,
    background: Color = RigColors.MintSoft,
    modifier: Modifier = Modifier,
    dot: Boolean = false,
) {
    Surface(color = background, shape = RoundedCornerShape(50), modifier = modifier) {
        Row(
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 5.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (dot) {
                Surface(color = color, shape = CircleShape, modifier = Modifier.size(6.dp)) {}
                Spacer(Modifier.width(6.dp))
            }
            Text(
                text = text,
                style = MaterialTheme.typography.labelMedium,
                color = color,
                maxLines = 1,
            )
        }
    }
}

@Composable
fun MetricCard(
    label: String,
    value: String,
    accent: Color = RigColors.Mint,
    details: List<String> = emptyList(),
    modifier: Modifier = Modifier,
    icon: ImageVector? = null,
) {
    RigCard(modifier = modifier, padding = PaddingValues(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            if (icon != null) {
                Icon(
                    imageVector = icon,
                    contentDescription = null,
                    tint = accent,
                    modifier = Modifier.size(16.dp),
                )
                Spacer(Modifier.width(7.dp))
            }
            SectionLabel(label)
        }
        Spacer(Modifier.height(8.dp))
        Text(
            text = value,
            style = MaterialTheme.typography.headlineSmall,
            color = RigColors.Text,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        if (details.isNotEmpty()) {
            Spacer(Modifier.height(3.dp))
            Text(
                text = details.joinToString(" · "),
                style = MaterialTheme.typography.bodyMedium,
                color = RigColors.TextDim,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
fun PrimaryButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    icon: ImageVector? = null,
) {
    Button(
        onClick = onClick,
        modifier = modifier.height(50.dp),
        enabled = enabled,
        shape = RoundedCornerShape(16.dp),
        colors = ButtonDefaults.buttonColors(
            containerColor = RigColors.Mint,
            contentColor = RigColors.Ink,
            disabledContainerColor = RigColors.Mint.copy(alpha = 0.25f),
            disabledContentColor = RigColors.Ink.copy(alpha = 0.5f),
        ),
        contentPadding = PaddingValues(horizontal = 18.dp),
    ) {
        if (icon != null) {
            Icon(icon, contentDescription = null, modifier = Modifier.size(18.dp))
            Spacer(Modifier.width(8.dp))
        }
        Text(text, style = MaterialTheme.typography.labelLarge)
    }
}

@Composable
fun SecondaryButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    icon: ImageVector? = null,
    danger: Boolean = false,
) {
    val tint = if (danger) RigColors.Danger else RigColors.Text
    OutlinedButton(
        onClick = onClick,
        modifier = modifier.height(50.dp),
        enabled = enabled,
        shape = RoundedCornerShape(16.dp),
        border = BorderStroke(1.dp, if (danger) RigColors.Danger.copy(alpha = 0.55f) else RigColors.Outline),
        colors = ButtonDefaults.outlinedButtonColors(contentColor = tint),
        contentPadding = PaddingValues(horizontal = 18.dp),
    ) {
        if (icon != null) {
            Icon(icon, contentDescription = null, modifier = Modifier.size(18.dp))
            Spacer(Modifier.width(8.dp))
        }
        Text(text, style = MaterialTheme.typography.labelLarge)
    }
}

@Composable
fun PageHeader(
    eyebrow: String,
    title: String,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    onBack: (() -> Unit)? = null,
    actions: @Composable RowScope.() -> Unit = {},
) {
    Column(modifier = modifier.fillMaxWidth()) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            if (onBack != null) {
                IconButton(onClick = onBack, modifier = Modifier.size(36.dp)) {
                    Icon(
                        Icons.AutoMirrored.Rounded.ArrowBack,
                        contentDescription = "Back",
                        tint = RigColors.Text,
                    )
                }
                Spacer(Modifier.width(4.dp))
            }
            Column(modifier = Modifier.weight(1f)) {
                SectionLabel(eyebrow)
                Spacer(Modifier.height(4.dp))
                Text(title, style = MaterialTheme.typography.headlineMedium, color = RigColors.Text)
                if (subtitle != null) {
                    Spacer(Modifier.height(3.dp))
                    Text(subtitle, style = MaterialTheme.typography.bodyMedium, color = RigColors.TextDim)
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp), content = actions)
        }
    }
}

@Composable
fun EmptyState(
    icon: ImageVector,
    title: String,
    body: String,
    modifier: Modifier = Modifier,
    actionLabel: String? = null,
    onAction: (() -> Unit)? = null,
) {
    Column(
        modifier = modifier.fillMaxWidth().padding(vertical = 36.dp, horizontal = 16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Surface(
            color = RigColors.SurfaceHigh,
            shape = CircleShape,
            modifier = Modifier.size(62.dp),
        ) {
            Icon(
                icon,
                contentDescription = null,
                tint = RigColors.Mint,
                modifier = Modifier.padding(17.dp),
            )
        }
        Spacer(Modifier.height(16.dp))
        Text(title, style = MaterialTheme.typography.titleLarge, color = RigColors.Text, textAlign = TextAlign.Center)
        Spacer(Modifier.height(6.dp))
        Text(
            body,
            style = MaterialTheme.typography.bodyMedium,
            color = RigColors.TextDim,
            textAlign = TextAlign.Center,
        )
        if (actionLabel != null && onAction != null) {
            Spacer(Modifier.height(18.dp))
            PrimaryButton(actionLabel, onAction)
        }
    }
}

@Composable
fun LoadingRow(text: String) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(vertical = 12.dp)) {
        CircularProgressIndicator(
            modifier = Modifier.size(18.dp),
            strokeWidth = 2.dp,
            color = RigColors.Mint,
        )
        Spacer(Modifier.width(10.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = RigColors.TextDim)
    }
}

enum class BannerTone { Info, Warn, Error }

@Composable
fun NoticeBanner(
    text: String,
    modifier: Modifier = Modifier,
    tone: BannerTone = BannerTone.Info,
) {
    val (fg, bg) = when (tone) {
        BannerTone.Info -> RigColors.Mint to RigColors.MintSoft
        BannerTone.Warn -> RigColors.Amber to RigColors.AmberSoft
        BannerTone.Error -> RigColors.Danger to RigColors.DangerSoft
    }
    Surface(
        modifier = modifier.fillMaxWidth(),
        color = bg,
        shape = RoundedCornerShape(14.dp),
    ) {
        Text(
            text = text,
            style = MaterialTheme.typography.bodyMedium,
            color = fg,
            modifier = Modifier.padding(horizontal = 14.dp, vertical = 11.dp),
        )
    }
}

@Composable
fun RefreshAction(onClick: () -> Unit) {
    IconButton(onClick = onClick) {
        Icon(Icons.Rounded.Refresh, contentDescription = "Refresh", tint = RigColors.TextDim)
    }
}

@Composable
fun Chevron() {
    Icon(Icons.Rounded.ChevronRight, contentDescription = null, tint = RigColors.TextFaint)
}

@Composable
fun LabelValueRow(label: String, value: String, valueColor: Color = RigColors.Text) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 5.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, style = MaterialTheme.typography.bodyMedium, color = RigColors.TextDim, modifier = Modifier.weight(1f))
        Text(
            value,
            style = MaterialTheme.typography.titleMedium,
            color = valueColor,
            fontWeight = FontWeight.Medium,
        )
    }
}

@Composable
fun SessionRow(summary: SessionSummary, onClick: () -> Unit) {
    RigCard(padding = PaddingValues(14.dp), onClick = onClick) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Surface(
                color = RigColors.MintSoft,
                shape = RoundedCornerShape(14.dp),
                modifier = Modifier.size(42.dp),
            ) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(
                        text = summary.gameName.take(2).uppercase().ifBlank { "??" },
                        style = MaterialTheme.typography.labelLarge,
                        color = RigColors.Mint,
                    )
                }
            }
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = summary.gameName,
                    style = MaterialTheme.typography.titleMedium,
                    color = RigColors.Text,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = "${summary.dateText} · ${summary.durationText}",
                    style = MaterialTheme.typography.bodyMedium,
                    color = RigColors.TextDim,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Column(horizontalAlignment = Alignment.End) {
                Text(
                    text = summary.avgFps.fpsText(),
                    style = MaterialTheme.typography.titleLarge,
                    color = RigColors.Text,
                )
                Text(
                    text = "avg FPS",
                    style = MaterialTheme.typography.labelSmall,
                    color = RigColors.TextFaint,
                )
            }
            Chevron()
        }
    }
}
