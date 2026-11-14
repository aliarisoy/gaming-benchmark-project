package com.rigsight.app.util

import com.rigsight.app.protocol.SessionSummary
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale
import kotlin.math.roundToLong

private const val DASH = "—"

fun String.toEpochMillis(): Long {
    if (isBlank()) return 0
    return runCatching { OffsetDateTime.parse(this).toInstant().toEpochMilli() }
        .recoverCatching { Instant.parse(this).toEpochMilli() }
        .getOrDefault(0)
}

fun String.toLocalDateTime(): java.time.LocalDateTime? = runCatching {
    OffsetDateTime.parse(this).atZoneSameInstant(ZoneId.systemDefault()).toLocalDateTime()
}.recoverCatching {
    Instant.parse(this).atZone(ZoneId.systemDefault()).toLocalDateTime()
}.getOrNull()

fun formatDate(iso: String): String {
    val local = iso.toLocalDateTime() ?: return DASH
    return local.format(DateTimeFormatter.ofPattern("MMM d · HH:mm", Locale.getDefault()))
}

fun formatFullDate(iso: String): String {
    val local = iso.toLocalDateTime() ?: return DASH
    return local.format(DateTimeFormatter.ofPattern("EEEE, MMM d · HH:mm", Locale.getDefault()))
}

fun Double?.metric(decimals: Int = 0): String {
    val value = this ?: return DASH
    if (value.isNaN() || value.isInfinite()) return DASH
    return if (decimals <= 0) value.roundToLong().toString()
    else String.format(Locale.US, "%.${decimals}f", value)
}

fun Double?.fpsText(): String = metric(0)

fun Double?.tempText(): String = this?.let { "${it.metric(0)} °C" } ?: DASH

fun Double?.wattsText(): String = this?.let { "${it.metric(0)} W" } ?: DASH

fun Double?.percentText(): String = this?.let { "${it.metric(0)}%" } ?: DASH

fun Double?.mbToGbText(): String =
    this?.let { String.format(Locale.US, "%.1f GB", it / 1024.0) } ?: DASH

fun formatDuration(ms: Long): String {
    val totalSeconds = (ms.coerceAtLeast(0) / 1000).toInt()
    val hours = totalSeconds / 3600
    val minutes = (totalSeconds % 3600) / 60
    val seconds = totalSeconds % 60
    return if (hours > 0) {
        String.format(Locale.US, "%d:%02d:%02d", hours, minutes, seconds)
    } else {
        String.format(Locale.US, "%02d:%02d", minutes, seconds)
    }
}

fun formatCompactDuration(ms: Long): String {
    val totalSeconds = (ms.coerceAtLeast(0) / 1000).toInt()
    val hours = totalSeconds / 3600
    val minutes = (totalSeconds % 3600) / 60
    val seconds = totalSeconds % 60
    return when {
        hours > 0 -> "${hours}h ${minutes.toString().padStart(2, '0')}m"
        minutes > 0 -> "${minutes}m ${seconds.toString().padStart(2, '0')}s"
        else -> "${seconds}s"
    }
}

fun stopReasonLabel(reason: String): String = when (reason) {
    "game_exit" -> "Game exit"
    "phone" -> "Stopped from phone"
    "user" -> "Stopped on PC"
    "agent_shutdown" -> "PC agent shut down"
    "interrupted" -> "Interrupted"
    "demo" -> "Demo"
    else -> reason.replaceFirstChar { it.uppercase() }
}

val SessionSummary.durationText: String get() = formatCompactDuration(durationMs)
val SessionSummary.dateText: String get() = formatDate(startedAt)
val SessionSummary.fullDateText: String get() = formatFullDate(startedAt)
val SessionSummary.stopText: String get() = stopReasonLabel(stopReason)
