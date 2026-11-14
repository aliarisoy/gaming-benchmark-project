package com.rigsight.app.data

import android.content.Context
import android.os.Build
import com.rigsight.app.protocol.DEFAULT_WS_PORT

/** Small, synchronous preference store shared by the UI and the foreground service. */
class SettingsStore(context: Context) {
    private val prefs = context.getSharedPreferences("rigsight", Context.MODE_PRIVATE)

    var token: String
        get() = prefs.getString(KEY_TOKEN, "").orEmpty()
        set(value) = prefs.edit().putString(KEY_TOKEN, value).apply()

    var pcName: String?
        get() = prefs.getString(KEY_PC_NAME, null)
        set(value) = prefs.edit().putString(KEY_PC_NAME, value).apply()

    var pcId: String?
        get() = prefs.getString(KEY_PC_ID, null)
        set(value) = prefs.edit().putString(KEY_PC_ID, value).apply()

    var host: String?
        get() = prefs.getString(KEY_HOST, null)
        set(value) = prefs.edit().putString(KEY_HOST, value).apply()

    var port: Int
        get() = prefs.getInt(KEY_PORT, DEFAULT_WS_PORT)
        set(value) = prefs.edit().putInt(KEY_PORT, value).apply()

    var bleAddress: String?
        get() = prefs.getString(KEY_BLE_ADDRESS, null)
        set(value) = prefs.edit().putString(KEY_BLE_ADDRESS, value).apply()

    var deviceName: String
        get() = prefs.getString(KEY_DEVICE_NAME, null) ?: defaultDeviceName()
        set(value) = prefs.edit().putString(KEY_DEVICE_NAME, value).apply()

    var autoConnect: Boolean
        get() = prefs.getBoolean(KEY_AUTO_CONNECT, true)
        set(value) = prefs.edit().putBoolean(KEY_AUTO_CONNECT, value).apply()

    var keepScreenOn: Boolean
        get() = prefs.getBoolean(KEY_KEEP_SCREEN_ON, true)
        set(value) = prefs.edit().putBoolean(KEY_KEEP_SCREEN_ON, value).apply()

    var notificationPrompt: Boolean
        get() = prefs.getBoolean(KEY_NOTIFY_PROMPT, true)
        set(value) = prefs.edit().putBoolean(KEY_NOTIFY_PROMPT, value).apply()

    var notificationSaved: Boolean
        get() = prefs.getBoolean(KEY_NOTIFY_SAVED, true)
        set(value) = prefs.edit().putBoolean(KEY_NOTIFY_SAVED, value).apply()

    val hasToken: Boolean get() = token.isNotBlank()

    fun forgetPc() {
        prefs.edit()
            .remove(KEY_TOKEN)
            .remove(KEY_PC_NAME)
            .remove(KEY_PC_ID)
            .remove(KEY_HOST)
            .remove(KEY_PORT)
            .remove(KEY_BLE_ADDRESS)
            .apply()
    }

    private fun defaultDeviceName(): String {
        val manufacturer = Build.MANUFACTURER.orEmpty().trim()
        val model = Build.MODEL.orEmpty().trim()
        val name = when {
            model.isBlank() -> "Android phone"
            manufacturer.isBlank() || model.startsWith(manufacturer, ignoreCase = true) -> model
            else -> "$manufacturer $model"
        }
        return name.replaceFirstChar { it.uppercase() }
    }

    private companion object {
        const val KEY_TOKEN = "token"
        const val KEY_PC_NAME = "pcName"
        const val KEY_PC_ID = "pcId"
        const val KEY_HOST = "host"
        const val KEY_PORT = "port"
        const val KEY_BLE_ADDRESS = "bleAddress"
        const val KEY_DEVICE_NAME = "deviceName"
        const val KEY_AUTO_CONNECT = "autoConnect"
        const val KEY_KEEP_SCREEN_ON = "keepScreenOn"
        const val KEY_NOTIFY_PROMPT = "notifyPrompt"
        const val KEY_NOTIFY_SAVED = "notifySaved"
    }
}
