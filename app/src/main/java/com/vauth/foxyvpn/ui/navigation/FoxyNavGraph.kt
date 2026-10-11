package com.vauth.foxyvpn.ui.navigation

import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.IntOffset
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.vauth.foxyvpn.FoxyVpnApp
import com.vauth.foxyvpn.ui.screens.AccountScreen
import com.vauth.foxyvpn.ui.screens.HomeScreen
import com.vauth.foxyvpn.ui.screens.LoginScreen
import com.vauth.foxyvpn.ui.screens.LogsScreen
import com.vauth.foxyvpn.ui.screens.ServerListScreen
import com.vauth.foxyvpn.ui.screens.SettingsScreen
import com.vauth.foxyvpn.ui.screens.SplashScreen
import com.vauth.foxyvpn.ui.theme.ThemeController

object FoxyRoutes {
    const val SPLASH = "splash"
    const val LOGIN = "login"
    const val HOME = "home"
    const val SERVERS = "servers"
    const val SETTINGS = "settings"
    const val ACCOUNT = "account"
    const val LOGS = "logs"
}

private const val NAV_ANIMATION_MS = 340

private fun isEntryScreen(route: String?): Boolean = route == FoxyRoutes.SPLASH || route == FoxyRoutes.LOGIN

@Composable
fun FoxyNavGraph(
    navController: NavHostController = rememberNavController(),
    app: FoxyVpnApp,
    themeController: ThemeController,
    onRequestConnect: () -> Unit,
    onDisconnect: () -> Unit,
) {
    // iOS-style push / pop: new screens slide in from the trailing edge while the old one drifts back.
    // Entering or leaving the splash / sign-in screens cross-fades instead.
    val slideSpec = tween<IntOffset>(NAV_ANIMATION_MS, easing = FastOutSlowInEasing)
    NavHost(
        navController = navController,
        startDestination = FoxyRoutes.SPLASH,
        enterTransition = {
            if (isEntryScreen(initialState.destination.route)) {
                fadeIn(tween(250))
            } else {
                slideInHorizontally(slideSpec) { fullWidth -> fullWidth }
            }
        },
        exitTransition = {
            if (isEntryScreen(initialState.destination.route)) {
                fadeOut(tween(250))
            } else {
                slideOutHorizontally(slideSpec) { fullWidth -> -fullWidth / 4 }
            }
        },
        popEnterTransition = { slideInHorizontally(slideSpec) { fullWidth -> -fullWidth / 4 } },
        popExitTransition = { slideOutHorizontally(slideSpec) { fullWidth -> fullWidth } },
    ) {
        composable(FoxyRoutes.SPLASH) {
            SplashScreen(
                authRepository = app.authRepository,
                onSignedIn = {
                    navController.navigate(FoxyRoutes.HOME) { popUpTo(FoxyRoutes.SPLASH) { inclusive = true } }
                },
                onNeedsLogin = {
                    navController.navigate(FoxyRoutes.LOGIN) { popUpTo(FoxyRoutes.SPLASH) { inclusive = true } }
                },
            )
        }
        composable(FoxyRoutes.LOGIN) {
            LoginScreen(
                authRepository = app.authRepository,
                onSignedIn = {
                    navController.navigate(FoxyRoutes.HOME) { popUpTo(FoxyRoutes.LOGIN) { inclusive = true } }
                },
            )
        }
        composable(FoxyRoutes.HOME) {
            HomeScreen(
                app = app,
                onRequestConnect = onRequestConnect,
                onDisconnect = onDisconnect,
                onOpenServers = { navController.navigate(FoxyRoutes.SERVERS) },
                onOpenSettings = { navController.navigate(FoxyRoutes.SETTINGS) },
            )
        }
        composable(FoxyRoutes.SERVERS) {
            ServerListScreen(
                serverListClient = app.serverListClient,
                proxyStateStore = app.proxyStateStore,
                onServerSelected = { navController.popBackStack() },
                onBack = { navController.popBackStack() },
            )
        }
        composable(FoxyRoutes.SETTINGS) {
            SettingsScreen(
                settingsStore = app.settingsStore,
                themeMode = themeController.mode,
                onThemeModeChange = { themeController.set(it) },
                onOpenLogs = { navController.navigate(FoxyRoutes.LOGS) },
                onOpenAccount = { navController.navigate(FoxyRoutes.ACCOUNT) },
                onSignOut = {
                    onDisconnect()
                    app.tokenStore.clear()
                    navController.navigate(FoxyRoutes.LOGIN) { popUpTo(0) { inclusive = true } }
                },
                onBack = { navController.popBackStack() },
            )
        }
        composable(FoxyRoutes.ACCOUNT) {
            AccountScreen(
                authRepository = app.authRepository,
                onBack = { navController.popBackStack() },
            )
        }
        composable(FoxyRoutes.LOGS) {
            LogsScreen(onBack = { navController.popBackStack() })
        }
    }
}
