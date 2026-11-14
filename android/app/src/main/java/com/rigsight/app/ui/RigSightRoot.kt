package com.rigsight.app.ui

import android.app.Activity
import android.view.WindowManager
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.rigsight.app.AppContainer
import com.rigsight.app.NavigationTarget
import com.rigsight.app.net.LinkPhase
import com.rigsight.app.service.ServiceActions
import com.rigsight.app.ui.theme.RigColors
import kotlinx.coroutines.launch

private object Routes {
    const val Home = "home"
    const val Live = "live"
    const val History = "history"
    const val Detail = "detail/{sessionId}"
    const val Devices = "devices"
    const val Settings = "settings"

    fun detail(id: String) = "detail/$id"
}

@Composable
fun RigSightRoot(
    container: AppContainer,
    onRequestBlePermissions: () -> Unit,
    onRequestNotificationPermission: () -> Unit,
) {
    val manager = container.manager
    val context = LocalContext.current
    val navController = rememberNavController()
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val lifecycleOwner = LocalLifecycleOwner.current

    val connection by manager.connection.collectAsState()
    val active by manager.activeSession.collectAsState()
    val latest by manager.latestSample.collectAsState()
    val chartSamples by manager.chartSamples.collectAsState()
    val prompt by manager.prompt.collectAsState()
    val games by manager.games.collectAsState()
    val sessions by manager.sessions.collectAsState()
    val bleDevices by manager.bleDevices.collectAsState()
    val bleScanning by manager.bleScanning.collectAsState()
    val pairingError by manager.pairingError.collectAsState()

    val backStackEntry by navController.currentBackStackEntryAsState()
    val currentRoute = backStackEntry?.destination?.route

    // Track foreground state so the service knows when to stay quiet.
    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            when (event) {
                Lifecycle.Event.ON_START -> container.presence.appForeground = true
                Lifecycle.Event.ON_STOP -> container.presence.appForeground = false
                else -> Unit
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose {
            lifecycleOwner.lifecycle.removeObserver(observer)
            container.presence.appForeground = false
        }
    }

    // Start the foreground service once paired, so prompts survive backgrounding.
    LaunchedEffect(connection.phase, manager.settings.hasToken) {
        if (manager.settings.hasToken || connection.phase == LinkPhase.Connected) {
            ServiceActions.start(context)
        }
    }

    LaunchedEffect(Unit) {
        manager.notices.collect { message ->
            scope.launch {
                snackbarHostState.currentSnackbarData?.dismiss()
                snackbarHostState.showSnackbar(message, withDismissAction = true)
            }
        }
    }

    LaunchedEffect(Unit) {
        container.navigationRequest.collect { target ->
            if (target == null) return@collect
            container.navigationRequest.value = null
            val route = when (target.route) {
                Routes.Live -> Routes.Live
                "detail" -> target.sessionId?.let { Routes.detail(it) }
                else -> target.route
            } ?: return@collect
            navController.navigate(route) { launchSingleTop = true }
        }
    }

    // Keep the screen awake only on the live screen while a session is running.
    val keepScreenOn = currentRoute == Routes.Live &&
        active != null &&
        manager.settings.keepScreenOn
    DisposableEffect(keepScreenOn) {
        val window = (context as? Activity)?.window
        if (keepScreenOn) {
            window?.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        } else {
            window?.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
        onDispose { window?.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) }
    }

    DisposableEffect(currentRoute) {
        container.presence.liveVisible = currentRoute == Routes.Live
        onDispose {
            if (currentRoute == Routes.Live) container.presence.liveVisible = false
        }
    }

    Scaffold(
        containerColor = RigColors.Background,
        contentWindowInsets = WindowInsets.safeDrawing,
        snackbarHost = { SnackbarHost(snackbarHostState) },
    ) { innerPadding ->
        NavHost(
            navController = navController,
            startDestination = Routes.Home,
            modifier = Modifier.padding(innerPadding).imePadding(),
        ) {
            composable(Routes.Home) {
                HomeScreen(
                    manager = manager,
                    connection = connection,
                    active = active,
                    latest = latest,
                    chartSamples = chartSamples,
                    prompt = prompt,
                    games = games,
                    sessions = sessions,
                    onOpenLive = {
                        navController.navigate(Routes.Live) { launchSingleTop = true }
                    },
                    onOpenHistory = {
                        navController.navigate(Routes.History) { launchSingleTop = true }
                    },
                    onOpenDevices = {
                        navController.navigate(Routes.Devices) { launchSingleTop = true }
                    },
                    onOpenSession = { id -> navController.navigate(Routes.detail(id)) },
                    onOpenSettings = {
                        navController.navigate(Routes.Settings) { launchSingleTop = true }
                    },
                )
            }

            composable(Routes.Live) {
                LiveScreen(
                    session = active,
                    connection = connection,
                    latest = latest,
                    samples = chartSamples,
                    onBack = { navController.popBackStack() },
                    onOpenHistory = {
                        navController.navigate(Routes.History) { launchSingleTop = true }
                    },
                    onStop = { manager.stopActiveSession() },
                )
            }

            composable(Routes.History) {
                HistoryScreen(
                    sessions = sessions,
                    connected = connection.wifiConnected,
                    onBack = { navController.popBackStack() },
                    onRefresh = { manager.refreshHistory() },
                    onOpenSession = { id -> navController.navigate(Routes.detail(id)) },
                )
            }

            composable(
                route = Routes.Detail,
                arguments = listOf(navArgument("sessionId") { type = NavType.StringType }),
            ) { entry ->
                val sessionId = entry.arguments?.getString("sessionId").orEmpty()
                val detail by manager.detail.collectAsState()
                val loading by manager.detailLoading.collectAsState()
                LaunchedEffect(sessionId) { manager.requestDetail(sessionId) }
                DetailScreen(
                    summary = sessions.firstOrNull { it.id == sessionId },
                    detail = detail,
                    loading = loading,
                    connected = connection.wifiConnected,
                    onBack = { navController.popBackStack() },
                    onSync = { manager.requestDetail(sessionId) },
                    onDelete = {
                        manager.deleteSession(sessionId)
                        navController.popBackStack()
                    },
                )
            }

            composable(Routes.Devices) {
                DevicesScreen(
                    manager = manager,
                    connection = connection,
                    devices = bleDevices,
                    scanning = bleScanning,
                    pairingError = pairingError,
                    onBack = { navController.popBackStack() },
                    onRequestBlePermissions = onRequestBlePermissions,
                )
            }

            composable(Routes.Settings) {
                SettingsScreen(
                    manager = manager,
                    connection = connection,
                    sessionCount = sessions.size,
                    onBack = { navController.popBackStack() },
                    onOpenDevices = {
                        navController.navigate(Routes.Devices) { launchSingleTop = true }
                    },
                    onRequestNotificationPermission = onRequestNotificationPermission,
                )
            }
        }
    }
}
