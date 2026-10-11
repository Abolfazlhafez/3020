package com.vauth.foxyvpn.vpn

import android.app.PendingIntent
import android.content.Intent
import android.graphics.drawable.Icon
import android.net.VpnService
import android.os.Build
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import com.vauth.foxyvpn.FoxyVpnApp
import com.vauth.foxyvpn.MainActivity
import com.vauth.foxyvpn.R
import com.vauth.foxyvpn.data.model.ConnectionState
import com.vauth.foxyvpn.data.model.ProxyCandidate
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch

/**
 * Quick Settings tile with three clearly different looks:
 *  - Off:        inactive tile, crossed-out shield, "Off"
 *  - Connecting: inactive tile, shield whose dots fill in (animated), "Connecting…" - tap cancels
 *  - On:         active (coloured) tile, shield with a check mark, "On · <country>"
 * A failed attempt shows "Failed · tap to retry"; signed-out shows "Sign in to connect".
 */
class FoxyVpnTileService : TileService() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var stateJob: Job? = null
    private var animJob: Job? = null
    private var signedIn = true

    private data class Snapshot(
        val state: ConnectionState,
        val error: String?,
        val proxy: ProxyCandidate?,
    )

    override fun onStartListening() {
        super.onStartListening()
        val app = application as FoxyVpnApp
        signedIn = runCatching { app.tokenStore.hasStoredSession() }.getOrDefault(true)
        stateJob?.cancel()
        stateJob = scope.launch {
            combine(
                FoxyVpnService.state,
                FoxyVpnService.lastError,
                app.proxyStateStore.selectedProxyFlow,
            ) { state, error, proxy -> Snapshot(state, error, proxy) }
                .collect { render(it) }
        }
    }

    override fun onStopListening() {
        stateJob?.cancel()
        stateJob = null
        animJob?.cancel()
        animJob = null
        super.onStopListening()
    }

    override fun onClick() {
        super.onClick()
        // Connecting or connected: a tap turns it off (and cancels a connect in progress).
        if (FoxyVpnService.state.value != ConnectionState.DISCONNECTED) {
            FoxyVpnService.stop(this)
            return
        }

        val app = application as FoxyVpnApp
        signedIn = runCatching { app.tokenStore.hasStoredSession() }.getOrDefault(true)
        if (!signedIn) {
            // Nothing to connect with yet: open the app so the person can sign in.
            launchApp(action = null)
            return
        }

        FoxyVpnService.clearError()
        val vpnConsentIntent = if (app.settingsStore.proxyOnlyMode) null else VpnService.prepare(this)
        if (vpnConsentIntent == null) {
            // No UI is required once Android's VPN permission has been granted.
            FoxyVpnService.start(this)
        } else {
            // Android requires an Activity to show its first-time VPN consent screen.
            launchApp(action = MainActivity.ACTION_CONNECT_FROM_TILE)
        }
    }

    private fun launchApp(action: String?) {
        val launchIntent = Intent(this, MainActivity::class.java).apply {
            if (action != null) this.action = action
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP)
        }
        if (Build.VERSION.SDK_INT >= 34) {
            val pendingIntent = PendingIntent.getActivity(
                this,
                REQUEST_CODE_LAUNCH_FROM_TILE,
                launchIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )
            startActivityAndCollapse(pendingIntent)
        } else {
            @Suppress("DEPRECATION")
            startActivityAndCollapse(launchIntent)
        }
    }

    private fun render(snapshot: Snapshot) {
        val tile = qsTile ?: return
        val connecting = snapshot.state == ConnectionState.CONNECTING
        val connected = snapshot.state == ConnectionState.CONNECTED

        // Only the connected tile is "active" (coloured); connecting stays neutral but animates.
        tile.state = if (connected) Tile.STATE_ACTIVE else Tile.STATE_INACTIVE

        val status = when {
            connected -> {
                val place = snapshot.proxy?.let { it.countryName.ifBlank { it.countryCode } }.orEmpty()
                if (place.isBlank()) getString(R.string.qs_status_on)
                else getString(R.string.qs_status_on_location, place)
            }
            connecting -> getString(R.string.qs_status_connecting)
            !signedIn -> getString(R.string.qs_status_sign_in)
            snapshot.error != null -> getString(R.string.qs_status_failed)
            else -> getString(R.string.qs_status_off)
        }
        val label = getString(R.string.qs_tile_label)
        // Before Android 10 tiles have no subtitle, so the status goes into the label.
        tile.label = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) label else "$label · $status"
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            tile.subtitle = status
        }
        tile.contentDescription = "$label: $status"

        if (connecting) {
            startConnectingAnimation()
        } else {
            stopConnectingAnimation()
            tile.icon = Icon.createWithResource(this, if (connected) R.drawable.ic_qs_vpn_on else R.drawable.ic_qs_vpn_off)
        }
        tile.updateTile()
    }

    private fun startConnectingAnimation() {
        if (animJob?.isActive == true) return
        animJob = scope.launch {
            var frame = 0
            while (true) {
                val tile = qsTile ?: break
                tile.icon = Icon.createWithResource(this@FoxyVpnTileService, CONNECTING_FRAMES[frame % CONNECTING_FRAMES.size])
                tile.updateTile()
                frame++
                delay(FRAME_DELAY_MS)
            }
        }
    }

    private fun stopConnectingAnimation() {
        animJob?.cancel()
        animJob = null
    }

    override fun onDestroy() {
        stateJob?.cancel()
        animJob?.cancel()
        scope.cancel()
        super.onDestroy()
    }

    private companion object {
        const val REQUEST_CODE_LAUNCH_FROM_TILE = 42
        const val FRAME_DELAY_MS = 420L
        val CONNECTING_FRAMES = intArrayOf(
            R.drawable.ic_qs_vpn_connecting_0,
            R.drawable.ic_qs_vpn_connecting_1,
            R.drawable.ic_qs_vpn_connecting_2,
            R.drawable.ic_qs_vpn_connecting_3,
        )
    }
}
