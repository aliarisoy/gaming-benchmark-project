package com.rigsight.app.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.annotation.SuppressLint
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.rigsight.app.MainActivity
import com.rigsight.app.R
import com.rigsight.app.net.ActiveSessionInfo
import com.rigsight.app.net.ConnectionUiState
import com.rigsight.app.net.LinkPhase
import com.rigsight.app.net.LinkTransport
import com.rigsight.app.protocol.PromptInfo
import com.rigsight.app.protocol.SessionSummary
import com.rigsight.app.protocol.TelemetrySample
import com.rigsight.app.util.durationText
import com.rigsight.app.util.formatDuration
import com.rigsight.app.util.tempText
import com.rigsight.app.util.toEpochMillis
import com.rigsight.app.util.wattsText

object Notifications {
    const val ID_CONNECTION = 10
    const val ID_PROMPT = 20
    const val ID_SESSION = 30
    const val ID_SAVED = 40

    private const val CHANNEL_PROMPT = "game_detected"
    private const val CHANNEL_SESSION = "session_active"
    private const val CHANNEL_CONNECTION = "connection"
    private const val CHANNEL_SAVED = "session_saved"

    fun ensureChannels(context: Context) {
        val manager = context.getSystemService(NotificationManager::class.java) ?: return

        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_PROMPT,
                context.getString(R.string.game_detected_channel),
                NotificationManager.IMPORTANCE_HIGH,
            ).apply {
                description = "A game launched and is ready to track."
                enableVibration(true)
                lockscreenVisibility = Notification.VISIBILITY_PUBLIC
                setShowBadge(true)
            }
        )

        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_SESSION,
                context.getString(R.string.session_active_channel),
                NotificationManager.IMPORTANCE_LOW,
            ).apply {
                description = "Ongoing session status with current FPS."
                setSound(null, null)
                setShowBadge(false)
            }
        )

        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_CONNECTION,
                context.getString(R.string.connection_channel),
                NotificationManager.IMPORTANCE_LOW,
            ).apply {
                description = "Keeps RigSight connected to your PC in the background."
                setSound(null, null)
                setShowBadge(false)
            }
        )

        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_SAVED,
                context.getString(R.string.session_saved_channel),
                NotificationManager.IMPORTANCE_DEFAULT,
            ).apply {
                description = "A completed session summary."
                setShowBadge(true)
            }
        )
    }

    fun connection(context: Context, text: String, state: ConnectionUiState?): Notification {
        val title = when {
            state?.phase == LinkPhase.Connected && state.transport == LinkTransport.Wifi -> "RigSight · Wi-Fi"
            state?.phase == LinkPhase.Connected -> "RigSight · Bluetooth"
            else -> "RigSight"
        }
        return NotificationCompat.Builder(context, CHANNEL_CONNECTION)
            .setSmallIcon(R.drawable.ic_stat_rigsight)
            .setContentTitle(title)
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setContentIntent(openApp(context))
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setShowWhen(false)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .build()
    }

    fun prompt(context: Context, prompt: PromptInfo): Notification {
        val start = PendingIntent.getForegroundService(
            context,
            ID_PROMPT + 1,
            ServiceActions.promptIntent(context, ServiceActions.ACTION_START_PROMPT, prompt.id),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val decline = PendingIntent.getForegroundService(
            context,
            ID_PROMPT + 2,
            ServiceActions.promptIntent(context, ServiceActions.ACTION_DECLINE_PROMPT, prompt.id),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        return NotificationCompat.Builder(context, CHANNEL_PROMPT)
            .setSmallIcon(R.drawable.ic_stat_rigsight)
            .setContentTitle("Game detected: ${prompt.gameName}")
            .setContentText("Tap Start to record on your PC. No overlay.")
            .setStyle(NotificationCompat.BigTextStyle().bigText("Tap Start to record on your PC. No overlay."))
            .setContentIntent(openApp(context))
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setCategory(NotificationCompat.CATEGORY_EVENT)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setAutoCancel(true)
            .setTimeoutAfter(120_000)
            .addAction(0, "Start", start)
            .addAction(0, "Not now", decline)
            .build()
    }

    fun session(
        context: Context,
        session: ActiveSessionInfo,
        sample: TelemetrySample?,
        connection: ConnectionUiState,
    ): Notification {
        val fps = sample?.fps?.let { String.format(java.util.Locale.US, "%.0f FPS", it) }
        val elapsed = formatElapsed(session)
        val link = when {
            connection.transport == LinkTransport.Wifi -> "Wi-Fi"
            connection.transport == LinkTransport.Bluetooth -> "Bluetooth fallback"
            else -> "Reconnecting"
        }
        val title = listOfNotNull(session.gameName, fps).joinToString(" · ")
        val bits = mutableListOf(elapsed, link)
        sample?.gpuTempC?.tempText()?.let { bits.add("GPU $it") }
        sample?.cpuTempC?.tempText()?.let { bits.add("CPU $it") }
        sample?.gpuPowerW?.wattsText()?.let { bits.add("GPU $it") }

        val stop = PendingIntent.getForegroundService(
            context,
            ID_SESSION + 1,
            ServiceActions.intent(context, ServiceActions.ACTION_STOP_SESSION),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        return NotificationCompat.Builder(context, CHANNEL_SESSION)
            .setSmallIcon(R.drawable.ic_stat_rigsight)
            .setContentTitle(title)
            .setContentText(bits.joinToString(" · "))
            .setStyle(NotificationCompat.BigTextStyle().bigText(bits.joinToString(" · ")))
            .setContentIntent(openLive(context))
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setShowWhen(false)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .addAction(0, "Stop", stop)
            .build()
    }

    fun saved(context: Context, summary: SessionSummary): Notification {
        val bits = mutableListOf(summary.durationText)
        summary.avgFps?.let { bits.add(String.format(java.util.Locale.US, "%.0f avg FPS", it)) }
        summary.low1Fps?.let { bits.add(String.format(java.util.Locale.US, "1%% low %.0f", it)) }
        val intent = Intent(context, MainActivity::class.java)
            .putExtra(ServiceActions.EXTRA_SESSION_ID, summary.id)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        val content = PendingIntent.getActivity(
            context,
            ID_SAVED + summary.id.hashCode(),
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        return NotificationCompat.Builder(context, CHANNEL_SAVED)
            .setSmallIcon(R.drawable.ic_stat_rigsight)
            .setContentTitle("Session saved · ${summary.gameName}")
            .setContentText(bits.joinToString(" · "))
            .setStyle(NotificationCompat.BigTextStyle().bigText(bits.joinToString(" · ")))
            .setContentIntent(content)
            .setAutoCancel(true)
            .setCategory(NotificationCompat.CATEGORY_STATUS)
            .build()
    }

    fun cancelPrompt(context: Context) {
        context.getSystemService(NotificationManager::class.java)?.cancel(ID_PROMPT)
    }

    fun cancelSession(context: Context) {
        context.getSystemService(NotificationManager::class.java)?.cancel(ID_SESSION)
    }

    private fun openApp(context: Context): PendingIntent {
        val intent = Intent(context, MainActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        return PendingIntent.getActivity(
            context,
            ID_CONNECTION,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    private fun openLive(context: Context): PendingIntent {
        val intent = Intent(context, MainActivity::class.java)
            .putExtra(ServiceActions.EXTRA_OPEN_LIVE, true)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        return PendingIntent.getActivity(
            context,
            ID_SESSION + 3,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    private fun formatElapsed(session: ActiveSessionInfo): String {
        val started = session.startedAt.toEpochMillis()
        val elapsed = if (started > 0) System.currentTimeMillis() - started else 0
        return formatDuration(elapsed)
    }

    @SuppressLint("MissingPermission")
    fun notifyConnection(context: Context, text: String, state: ConnectionUiState?) {
        safeNotify(context, ID_CONNECTION, connection(context, text, state))
    }

    @SuppressLint("MissingPermission")
    fun notifyPrompt(context: Context, prompt: PromptInfo) {
        safeNotify(context, ID_PROMPT, prompt(context, prompt))
    }

    @SuppressLint("MissingPermission")
    fun notifySession(
        context: Context,
        session: ActiveSessionInfo,
        sample: TelemetrySample?,
        connection: ConnectionUiState,
    ) {
        safeNotify(context, ID_SESSION, session(context, session, sample, connection))
    }

    @SuppressLint("MissingPermission")
    fun notifySaved(context: Context, summary: SessionSummary) {
        safeNotify(context, ID_SAVED, saved(context, summary))
    }

    private fun safeNotify(context: Context, id: Int, notification: Notification) {
        try {
            NotificationManagerCompat.from(context).notify(id, notification)
        } catch (_: SecurityException) {
            // Notification permission was revoked; the foreground service keeps running.
        }
    }
}
