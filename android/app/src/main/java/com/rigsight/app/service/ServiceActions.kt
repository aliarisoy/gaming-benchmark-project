package com.rigsight.app.service

import android.content.Context
import android.content.Intent
import java.util.Locale

object ServiceActions {
    const val ACTION_START = "com.rigsight.app.action.START"
    const val ACTION_START_PROMPT = "com.rigsight.app.action.START_PROMPT"
    const val ACTION_DECLINE_PROMPT = "com.rigsight.app.action.DECLINE_PROMPT"
    const val ACTION_STOP_SESSION = "com.rigsight.app.action.STOP_SESSION"
    const val ACTION_RECONNECT = "com.rigsight.app.action.RECONNECT"
    const val ACTION_DISCONNECT = "com.rigsight.app.action.DISCONNECT"
    const val ACTION_STOP_SERVICE = "com.rigsight.app.action.STOP_SERVICE"

    const val EXTRA_PROMPT_ID = "prompt_id"
    const val EXTRA_OPEN_LIVE = "open_live"
    const val EXTRA_SESSION_ID = "session_id"

    fun intent(context: Context, action: String): Intent =
        Intent(context, RigSightService::class.java).setAction(action)

    fun start(context: Context) {
        androidx.core.content.ContextCompat.startForegroundService(
            context,
            intent(context, ACTION_START),
        )
    }

    fun stop(context: Context) {
        context.stopService(Intent(context, RigSightService::class.java))
    }

    fun promptIntent(context: Context, action: String, promptId: String): Intent =
        intent(context, action).putExtra(EXTRA_PROMPT_ID, promptId)

    fun formatPercent(value: Double?): String =
        value?.let { String.format(Locale.US, "%.0f%%", it) } ?: "—"
}
