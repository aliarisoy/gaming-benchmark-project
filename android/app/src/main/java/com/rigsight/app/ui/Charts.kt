package com.rigsight.app.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.rigsight.app.protocol.TelemetrySample
import com.rigsight.app.ui.theme.RigColors
import com.rigsight.app.util.metric
import kotlin.math.max

@Composable
fun FpsChart(
    samples: List<TelemetrySample>,
    modifier: Modifier = Modifier,
    height: Dp = 170.dp,
    color: Color = RigColors.Mint,
    showLabels: Boolean = true,
    fixedMax: Double? = null,
) {
    Box(modifier = modifier.fillMaxWidth().height(height), contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxWidth().height(height)) {
            val left = if (showLabels) 38.dp.toPx() else 4.dp.toPx()
            val right = 6.dp.toPx()
            val top = 10.dp.toPx()
            val bottom = if (showLabels) 18.dp.toPx() else 4.dp.toPx()
            val width = size.width - left - right
            val chartHeight = size.height - top - bottom
            if (width <= 0 || chartHeight <= 0) return@Canvas

            val values = samples.mapNotNull { it.fps }
            val maxValue = fixedMax
                ?: max(120.0, (values.maxOrNull() ?: 0.0) * 1.12)
            val minValue = 0.0

            val paint = android.graphics.Paint().apply {
                isAntiAlias = true
                this.color = RigColors.TextFaint.toArgb()
                textSize = 10.sp.toPx()
            }

            // Horizontal grid + labels.
            for (i in 0..3) {
                val fraction = i / 3f
                val y = top + chartHeight * fraction
                drawLine(
                    color = RigColors.Outline.copy(alpha = 0.6f),
                    start = Offset(left, y),
                    end = Offset(left + width, y),
                    strokeWidth = 1.dp.toPx(),
                )
                if (showLabels) {
                    val value = maxValue * (1 - fraction)
                    drawContext.canvas.nativeCanvas.drawText(
                        value.metric(0),
                        left - 7.dp.toPx(),
                        y + 3.dp.toPx(),
                        paint.apply { textAlign = android.graphics.Paint.Align.RIGHT },
                    )
                }
            }

            if (samples.size < 2) return@Canvas

            val stepX = width / (samples.size - 1).coerceAtLeast(1)
            val points = samples.mapIndexedNotNull { index, sample ->
                val value = sample.fps ?: return@mapIndexedNotNull null
                val normalized = ((value - minValue) / (maxValue - minValue)).coerceIn(0.0, 1.0)
                Offset(left + stepX * index, top + chartHeight * (1 - normalized).toFloat())
            }
            if (points.size < 2) return@Canvas

            val path = smoothPath(points)
            val fill = Path().apply {
                addPath(path)
                lineTo(points.last().x, top + chartHeight)
                lineTo(points.first().x, top + chartHeight)
                close()
            }
            drawPath(
                path = fill,
                brush = Brush.verticalGradient(
                    listOf(color.copy(alpha = 0.26f), Color.Transparent),
                    startY = top,
                    endY = top + chartHeight,
                ),
            )
            drawPath(
                path = path,
                color = color,
                style = Stroke(width = 2.2.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round),
            )
            drawCircle(color, radius = 3.2.dp.toPx(), center = points.last())
            drawCircle(Color.White.copy(alpha = 0.85f), radius = 1.4.dp.toPx(), center = points.last())

            if (showLabels) {
                paint.textAlign = android.graphics.Paint.Align.LEFT
                drawContext.canvas.nativeCanvas.drawText(
                    com.rigsight.app.util.formatDuration(samples.first().t),
                    left,
                    size.height - 3.dp.toPx(),
                    paint,
                )
                paint.textAlign = android.graphics.Paint.Align.RIGHT
                drawContext.canvas.nativeCanvas.drawText(
                    com.rigsight.app.util.formatDuration(samples.last().t),
                    left + width,
                    size.height - 3.dp.toPx(),
                    paint,
                )
            }
        }
        if (samples.size < 2) {
            Text(
                text = "Waiting for telemetry",
                style = MaterialTheme.typography.bodyMedium,
                color = RigColors.TextFaint,
                modifier = Modifier.align(Alignment.Center).padding(top = 16.dp),
            )
        }
    }
}

@Composable
fun LoadChart(
    samples: List<TelemetrySample>,
    modifier: Modifier = Modifier,
    height: Dp = 170.dp,
) {
    Box(modifier = modifier.fillMaxWidth().height(height)) {
        Canvas(Modifier.fillMaxWidth().height(height)) {
            val left = 38.dp.toPx()
            val right = 6.dp.toPx()
            val top = 10.dp.toPx()
            val bottom = 18.dp.toPx()
            val width = size.width - left - right
            val chartHeight = size.height - top - bottom
            if (width <= 0 || chartHeight <= 0) return@Canvas

            val paint = android.graphics.Paint().apply {
                isAntiAlias = true
                color = RigColors.TextFaint.toArgb()
                textSize = 10.sp.toPx()
                textAlign = android.graphics.Paint.Align.RIGHT
            }
            for (i in 0..4) {
                val fraction = i / 4f
                val y = top + chartHeight * fraction
                drawLine(
                    color = RigColors.Outline.copy(alpha = 0.55f),
                    start = Offset(left, y),
                    end = Offset(left + width, y),
                    strokeWidth = 1.dp.toPx(),
                )
                drawContext.canvas.nativeCanvas.drawText(
                    "${((1 - fraction) * 100).toInt()}%",
                    left - 7.dp.toPx(),
                    y + 3.dp.toPx(),
                    paint,
                )
            }

            if (samples.size >= 2) {
                val stepX = width / (samples.size - 1).coerceAtLeast(1)
                fun lineFor(selector: (TelemetrySample) -> Double?, color: Color) {
                    val points = samples.mapIndexedNotNull { index, sample ->
                        val value = selector(sample) ?: return@mapIndexedNotNull null
                        Offset(
                            left + stepX * index,
                            top + chartHeight * (1 - (value / 100.0).coerceIn(0.0, 1.0)).toFloat(),
                        )
                    }
                    if (points.size >= 2) {
                        drawPath(
                            path = smoothPath(points),
                            color = color,
                            style = Stroke(width = 2.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round),
                        )
                    }
                }
                lineFor({ it.gpuLoadPct }, RigColors.Violet)
                lineFor({ it.cpuLoadPct }, RigColors.Mint)
            } else {
                drawContext.canvas.nativeCanvas.drawText(
                    "waiting for load",
                    left + width / 2,
                    top + chartHeight / 2,
                    paint.apply { textAlign = android.graphics.Paint.Align.CENTER },
                )
            }
        }
    }
}

@Composable
fun HistoryChart(
    samples: List<TelemetrySample>,
    selector: (TelemetrySample) -> Double?,
    color: Color,
    modifier: Modifier = Modifier,
    height: Dp = 160.dp,
    unit: String = "",
    fixedMax: Double? = null,
) {
    val points = samples.mapNotNull { sample -> selector(sample)?.let { it } }
    val maxValue = fixedMax ?: max(1.0, (points.maxOrNull() ?: 1.0) * 1.12)

    Box(modifier = modifier.fillMaxWidth().height(height), contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxWidth().height(height)) {
            val left = 38.dp.toPx()
            val right = 6.dp.toPx()
            val top = 10.dp.toPx()
            val bottom = 18.dp.toPx()
            val width = size.width - left - right
            val chartHeight = size.height - top - bottom
            if (width <= 0 || chartHeight <= 0) return@Canvas

            val paint = android.graphics.Paint().apply {
                isAntiAlias = true
                this.color = RigColors.TextFaint.toArgb()
                textSize = 10.sp.toPx()
                textAlign = android.graphics.Paint.Align.RIGHT
            }
            for (i in 0..3) {
                val fraction = i / 3f
                val y = top + chartHeight * fraction
                drawLine(
                    color = RigColors.Outline.copy(alpha = 0.55f),
                    start = Offset(left, y),
                    end = Offset(left + width, y),
                    strokeWidth = 1.dp.toPx(),
                )
                drawContext.canvas.nativeCanvas.drawText(
                    (maxValue * (1 - fraction)).metric(0),
                    left - 7.dp.toPx(),
                    y + 3.dp.toPx(),
                    paint,
                )
            }
            if (unit.isNotBlank()) {
                paint.textAlign = android.graphics.Paint.Align.LEFT
                drawContext.canvas.nativeCanvas.drawText(unit, left, size.height - 2.dp.toPx(), paint)
            }

            val stepX = width / (samples.size - 1).coerceAtLeast(1)
            val offsets = samples.mapIndexedNotNull { index, sample ->
                val value = selector(sample) ?: return@mapIndexedNotNull null
                val normalized = (value / maxValue).coerceIn(0.0, 1.0)
                Offset(left + stepX * index, top + chartHeight * (1 - normalized).toFloat())
            }
            if (offsets.size >= 2) {
                val path = smoothPath(offsets)
                val fill = Path().apply {
                    addPath(path)
                    lineTo(offsets.last().x, top + chartHeight)
                    lineTo(offsets.first().x, top + chartHeight)
                    close()
                }
                drawPath(
                    path = fill,
                    brush = Brush.verticalGradient(
                        listOf(color.copy(alpha = 0.2f), Color.Transparent),
                        startY = top,
                        endY = top + chartHeight,
                    ),
                )
                drawPath(
                    path = path,
                    color = color,
                    style = Stroke(width = 2.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round),
                )
            } else if (samples.isEmpty()) {
                drawContext.canvas.nativeCanvas.drawText(
                    "No samples synced",
                    left + width / 2,
                    top + chartHeight / 2,
                    paint.apply { textAlign = android.graphics.Paint.Align.CENTER },
                )
            }
        }
    }
}

/** One line in a [MultiHistoryChart]. */
data class ChartSeries(
    val label: String,
    val color: Color,
    val selector: (TelemetrySample) -> Double?,
)

@Composable
fun MultiHistoryChart(
    samples: List<TelemetrySample>,
    series: List<ChartSeries>,
    modifier: Modifier = Modifier,
    height: Dp = 170.dp,
    unit: String = "",
    fixedMax: Double? = null,
) {
    val allValues = series.flatMap { entry -> samples.mapNotNull { entry.selector(it) } }
    val maxValue = fixedMax ?: max(1.0, (allValues.maxOrNull() ?: 1.0) * 1.12)

    Box(modifier = modifier.fillMaxWidth().height(height), contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxWidth().height(height)) {
            val left = 38.dp.toPx()
            val right = 6.dp.toPx()
            val top = 10.dp.toPx()
            val bottom = 18.dp.toPx()
            val width = size.width - left - right
            val chartHeight = size.height - top - bottom
            if (width <= 0 || chartHeight <= 0) return@Canvas

            val paint = android.graphics.Paint().apply {
                isAntiAlias = true
                color = RigColors.TextFaint.toArgb()
                textSize = 10.sp.toPx()
                textAlign = android.graphics.Paint.Align.RIGHT
            }
            for (i in 0..3) {
                val fraction = i / 3f
                val y = top + chartHeight * fraction
                drawLine(
                    color = RigColors.Outline.copy(alpha = 0.55f),
                    start = Offset(left, y),
                    end = Offset(left + width, y),
                    strokeWidth = 1.dp.toPx(),
                )
                drawContext.canvas.nativeCanvas.drawText(
                    (maxValue * (1 - fraction)).metric(0),
                    left - 7.dp.toPx(),
                    y + 3.dp.toPx(),
                    paint,
                )
            }
            if (unit.isNotBlank()) {
                paint.textAlign = android.graphics.Paint.Align.LEFT
                drawContext.canvas.nativeCanvas.drawText(unit, left, size.height - 2.dp.toPx(), paint)
            }

            if (samples.size >= 2) {
                val stepX = width / (samples.size - 1).coerceAtLeast(1)
                for (entry in series) {
                    val offsets = samples.mapIndexedNotNull { index, sample ->
                        val value = entry.selector(sample) ?: return@mapIndexedNotNull null
                        val normalized = (value / maxValue).coerceIn(0.0, 1.0)
                        Offset(left + stepX * index, top + chartHeight * (1 - normalized).toFloat())
                    }
                    if (offsets.size >= 2) {
                        drawPath(
                            path = smoothPath(offsets),
                            color = entry.color,
                            style = Stroke(width = 2.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round),
                        )
                    }
                }
            } else if (samples.isEmpty()) {
                drawContext.canvas.nativeCanvas.drawText(
                    "No samples synced",
                    left + width / 2,
                    top + chartHeight / 2,
                    paint.apply { textAlign = android.graphics.Paint.Align.CENTER },
                )
            }
        }
    }
}


@Composable
fun ChartLegend(items: List<Pair<String, Color>>, modifier: Modifier = Modifier) {
    Row(modifier = modifier, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
        for ((label, color) in items) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Surface(color = color, shape = CircleShape, modifier = Modifier.size(7.dp)) {}
                Spacer(Modifier.width(6.dp))
                Text(label, style = MaterialTheme.typography.labelMedium, color = RigColors.TextDim)
            }
        }
    }
}

private fun smoothPath(points: List<Offset>): Path {
    val path = Path()
    if (points.isEmpty()) return path
    path.moveTo(points.first().x, points.first().y)
    for (i in 0 until points.lastIndex) {
        val current = points[i]
        val next = points[i + 1]
        val midX = (current.x + next.x) / 2f
        path.cubicTo(midX, current.y, midX, next.y, next.x, next.y)
    }
    return path
}
