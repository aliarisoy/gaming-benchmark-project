package com.rigsight.app

import android.Manifest
import android.content.Intent
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import com.rigsight.app.service.ServiceActions
import com.rigsight.app.ui.RigSightRoot
import com.rigsight.app.ui.theme.RigSightTheme
import com.rigsight.app.util.blePermissionRequest
import com.rigsight.app.util.hasNotificationPermission

class MainActivity : ComponentActivity() {
    private val notificationPermission =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { }

    private val blePermissions =
        registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        val container = (application as RigSightApp).container
        handleIntent(intent, container)

        setContent {
            RigSightTheme {
                RigSightRoot(
                    container = container,
                    onRequestBlePermissions = { blePermissions.launch(blePermissionRequest()) },
                    onRequestNotificationPermission = {
                        if (Build.VERSION.SDK_INT >= 33) {
                            notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
                        }
                    },
                )
            }
        }

        if (!hasNotificationPermission(this) && Build.VERSION.SDK_INT >= 33) {
            notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleIntent(intent, (application as RigSightApp).container)
    }

    private fun handleIntent(intent: Intent?, container: AppContainer) {
        if (intent == null) return
        val openLive = intent.getBooleanExtra(ServiceActions.EXTRA_OPEN_LIVE, false)
        val sessionId = intent.getStringExtra(ServiceActions.EXTRA_SESSION_ID)
        when {
            openLive -> {
                container.navigationRequest.value = NavigationTarget("live")
                intent.removeExtra(ServiceActions.EXTRA_OPEN_LIVE)
            }
            !sessionId.isNullOrBlank() -> {
                container.navigationRequest.value = NavigationTarget("detail", sessionId)
                intent.removeExtra(ServiceActions.EXTRA_SESSION_ID)
            }
        }
    }
}
