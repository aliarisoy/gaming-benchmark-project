package com.rigsight.app

import android.app.Application
import android.content.Context
import com.rigsight.app.data.HistoryStore
import com.rigsight.app.data.SettingsStore
import com.rigsight.app.net.ConnectionManager
import kotlinx.coroutines.flow.MutableStateFlow

class RigSightApp : Application() {
    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()
        container = AppContainer(this)
    }
}

class AppContainer(context: Context) {
    val settings = SettingsStore(context)
    val history = HistoryStore(context)
    val manager = ConnectionManager(context, settings, history)
    val presence = UiPresence()
    val navigationRequest = MutableStateFlow<NavigationTarget?>(null)
}

class UiPresence {
    @Volatile var appForeground: Boolean = false
    @Volatile var liveVisible: Boolean = false
}

data class NavigationTarget(
    val route: String,
    val sessionId: String? = null,
)
