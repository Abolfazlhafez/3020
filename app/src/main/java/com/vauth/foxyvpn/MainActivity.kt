package com.vauth.foxyvpn

import android.Manifest
import android.content.pm.PackageManager
import android.net.VpnService
import android.os.Build
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.ui.Modifier
import androidx.core.content.ContextCompat
import androidx.core.view.WindowCompat
import androidx.navigation.compose.rememberNavController
import com.vauth.foxyvpn.data.model.ConnectionState
import com.vauth.foxyvpn.ui.navigation.FoxyNavGraph
import com.vauth.foxyvpn.ui.theme.FoxyVpnTheme
import com.vauth.foxyvpn.ui.theme.rememberThemeController
import com.vauth.foxyvpn.vpn.FoxyVpnService

class MainActivity : ComponentActivity() {

    private val vpnPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult(),
    ) { result ->
        if (result.resultCode == RESULT_OK) {
            FoxyVpnService.start(this)
        } else {
            Toast.makeText(
                this,
                "FoxyVPN needs Android's VPN permission to tunnel this device's traffic. " +
                    "Proxy-only mode in Settings works without it.",
                Toast.LENGTH_LONG,
            ).show()
        }
    }

    private val notificationPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) {
        // Continue only after the notification permission dialog has closed. This avoids
        // launching the VPN consent screen at the same time on a first connection.
        continueConnectRequest()
    }

    private fun requestConnect() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            val alreadyGranted = ContextCompat.checkSelfPermission(
                this,
                Manifest.permission.POST_NOTIFICATIONS,
            ) == PackageManager.PERMISSION_GRANTED
            if (!alreadyGranted) {
                notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                return
            }
        }
        continueConnectRequest()
    }

    private fun continueConnectRequest() {
        if ((application as FoxyVpnApp).settingsStore.proxyOnlyMode) {
            FoxyVpnService.start(this)
            return
        }

        val prepareIntent = VpnService.prepare(this)
        if (prepareIntent != null) {
            vpnPermissionLauncher.launch(prepareIntent)
        } else {
            FoxyVpnService.start(this)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Draw edge to edge; every screen applies its own safe-drawing insets.
        WindowCompat.setDecorFitsSystemWindows(window, false)
        @Suppress("DEPRECATION")
        run {
            window.statusBarColor = android.graphics.Color.TRANSPARENT
            window.navigationBarColor = android.graphics.Color.TRANSPARENT
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            window.isStatusBarContrastEnforced = false
            window.isNavigationBarContrastEnforced = false
        }
        val app = application as FoxyVpnApp
        setContent {
            // System / Light / Dark is the user's choice in Settings > Appearance.
            val themeController = rememberThemeController(app.settingsStore)
            FoxyVpnTheme(themeMode = themeController.mode, dynamicColor = false) {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background,
                    contentColor = MaterialTheme.colorScheme.onBackground,
                ) {
                    val navController = rememberNavController()
                    FoxyNavGraph(
                        navController = navController,
                        app = app,
                        themeController = themeController,
                        onRequestConnect = { requestConnect() },
                        onDisconnect = { FoxyVpnService.stop(this@MainActivity) },
                    )
                }
            }
        }
        // handleTileConnectIntent() clears the intent action, so remember it first.
        val launchedFromTile = intent?.action == ACTION_CONNECT_FROM_TILE
        handleTileConnectIntent(intent)
        // "Connect on launch": only on a genuinely fresh start (not rotation / process-restored
        // recreation), never when the Quick Settings tile already asked to connect, and only when
        // there is a stored session so a signed-out user is not shown a "Not signed in" error.
        if (savedInstanceState == null &&
            !launchedFromTile &&
            app.settingsStore.connectOnLaunch &&
            app.tokenStore.hasStoredSession() &&
            FoxyVpnService.state.value == ConnectionState.DISCONNECTED
        ) {
            requestConnect()
        }
    }

    override fun onNewIntent(intent: android.content.Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleTileConnectIntent(intent)
    }

    private fun handleTileConnectIntent(intent: android.content.Intent?) {
        if (intent?.action != ACTION_CONNECT_FROM_TILE) return
        // Clear the action so a later activity recreation cannot trigger a second connection.
        intent.action = null
        requestConnect()
    }

    companion object {
        const val ACTION_CONNECT_FROM_TILE = "com.vauth.foxyvpn.action.CONNECT_FROM_TILE"
    }
}
