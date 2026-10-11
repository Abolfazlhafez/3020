package com.vauth.foxyvpn.ui.screens

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.animation.core.animateFloat
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDownward
import androidx.compose.material.icons.filled.ArrowUpward
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.PowerSettingsNew
import androidx.compose.material.icons.filled.Public
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.vauth.foxyvpn.FoxyVpnApp
import com.vauth.foxyvpn.data.GUARDIAN_ENDPOINT_DEFAULT
import com.vauth.foxyvpn.data.formatBytes
import com.vauth.foxyvpn.data.formatBytesPerSecond
import com.vauth.foxyvpn.data.model.ConnectionState
import com.vauth.foxyvpn.data.model.Entitlement
import com.vauth.foxyvpn.ui.theme.IosRow
import com.vauth.foxyvpn.ui.theme.IosScreen
import com.vauth.foxyvpn.ui.theme.IosSection
import com.vauth.foxyvpn.ui.theme.LocalFoxyStatusColors
import com.vauth.foxyvpn.vpn.FoxyVpnService
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.delay

@Composable
private fun rememberPulse(active: Boolean): Float {
    if (!active) return 0f
    val transition = rememberInfiniteTransition(label = "connecting-pulse")
    val alpha by transition.animateFloat(
        initialValue = .25f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(900), RepeatMode.Reverse),
        label = "connecting-pulse-alpha",
    )
    return alpha
}

@Composable
fun HomeScreen(
    app: FoxyVpnApp,
    onRequestConnect: () -> Unit,
    onDisconnect: () -> Unit,
    onOpenServers: () -> Unit,
    onOpenSettings: () -> Unit,
) {
    val state by FoxyVpnService.state.collectAsState()
    val lastError by FoxyVpnService.lastError.collectAsState()
    val trafficUsage by FoxyVpnService.trafficUsage.collectAsState()
    val selectedProxy by app.proxyStateStore.selectedProxyFlow.collectAsState()
    val status = LocalFoxyStatusColors.current
    val haptics = LocalHapticFeedback.current
    var showDisconnectConfirm by remember { mutableStateOf(false) }
    var entitlement by remember { mutableStateOf<Entitlement?>(null) }
    val connectedSince by FoxyVpnService.connectedSinceMs.collectAsState()
    var nowMs by remember { mutableLongStateOf(System.currentTimeMillis()) }
    // Tick once a second, but only while connected.
    LaunchedEffect(state) {
        while (state == ConnectionState.CONNECTED) {
            nowMs = System.currentTimeMillis()
            delay(1_000)
        }
    }

    // Monthly allowance: loaded when the screen opens and again after every connect / disconnect.
    // A failure just hides the card; the Account screen shows the detailed error.
    LaunchedEffect(state) {
        if (state == ConnectionState.CONNECTING) return@LaunchedEffect
        val token = app.authRepository.currentAccessToken() ?: return@LaunchedEffect
        try {
            entitlement = app.guardianClient.fetchUserInfo(GUARDIAN_ENDPOINT_DEFAULT, token)
        } catch (cancellation: CancellationException) {
            throw cancellation
        } catch (_: Exception) {
        }
    }

    val connected = state == ConnectionState.CONNECTED
    val connecting = state == ConnectionState.CONNECTING
    val countryLabel = selectedProxy?.let { it.countryName.ifBlank { it.countryCode } }

    val accent by animateColorAsState(
        targetValue = when (state) {
            ConnectionState.CONNECTED -> status.connected
            ConnectionState.CONNECTING -> status.connecting
            ConnectionState.DISCONNECTED -> status.gray
        },
        label = "status-accent",
    )
    val buttonFill by animateColorAsState(
        targetValue = if (connected) status.connected else MaterialTheme.colorScheme.surface,
        label = "power-fill",
    )
    val glyphColor by animateColorAsState(
        targetValue = when (state) {
            ConnectionState.CONNECTED -> Color.White
            ConnectionState.CONNECTING -> status.connecting
            ConnectionState.DISCONNECTED -> MaterialTheme.colorScheme.primary
        },
        label = "power-glyph",
    )
    val pulse = rememberPulse(connecting)
    // iOS-style press feedback: the button shrinks slightly under the finger and springs back.
    val powerInteraction = remember { MutableInteractionSource() }
    val powerPressed by powerInteraction.collectIsPressedAsState()
    val powerScale by animateFloatAsState(
        targetValue = if (powerPressed) .94f else 1f,
        animationSpec = spring(dampingRatio = Spring.DampingRatioMediumBouncy, stiffness = Spring.StiffnessMedium),
        label = "power-press-scale",
    )

    // The dialog only makes sense while connected; it disappears if the tunnel drops meanwhile.
    if (showDisconnectConfirm && connected) {
        AlertDialog(
            shape = RoundedCornerShape(20.dp),
            containerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
            onDismissRequest = { showDisconnectConfirm = false },
            title = { Text("Disconnect?") },
            text = { Text("Your connection will no longer be protected.") },
            confirmButton = {
                TextButton(onClick = {
                    showDisconnectConfirm = false
                    onDisconnect()
                }) { Text("Disconnect", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = {
                TextButton(onClick = { showDisconnectConfirm = false }) { Text("Cancel") }
            },
        )
    }

    IosScreen(
        title = "FoxyVPN",
        actions = {
            Box(
                modifier = Modifier
                    .padding(end = 8.dp)
                    .size(38.dp)
                    .clip(CircleShape)
                    .background(MaterialTheme.colorScheme.surface)
                    .clickable(role = Role.Button, onClick = onOpenSettings),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    imageVector = Icons.Filled.Settings,
                    contentDescription = "Settings",
                    tint = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.size(20.dp),
                )
            }
        },
    ) {
        // ---- Status ----
        Column(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 24.dp, vertical = 8.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(9.dp).clip(CircleShape).background(accent))
                Spacer(Modifier.width(8.dp))
                Text(
                    text = when (state) {
                        ConnectionState.CONNECTED -> "Connected"
                        ConnectionState.CONNECTING -> "Connecting…"
                        ConnectionState.DISCONNECTED -> "Not connected"
                    },
                    style = MaterialTheme.typography.headlineSmall,
                    color = MaterialTheme.colorScheme.onBackground,
                )
            }
            Spacer(Modifier.height(4.dp))
            Text(
                text = when {
                    connected && countryLabel != null -> "Your traffic is going through $countryLabel"
                    connected -> "Your traffic is protected"
                    connecting -> "Setting up a secure tunnel"
                    else -> "Your connection is not protected"
                },
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
            )
            if (connected && connectedSince > 0L) {
                val seconds = ((nowMs - connectedSince) / 1000L).coerceAtLeast(0L)
                Spacer(Modifier.height(6.dp))
                Text(
                    text = "%d:%02d:%02d".format(seconds / 3600, (seconds % 3600) / 60, seconds % 60),
                    style = MaterialTheme.typography.titleMedium,
                    color = status.connected,
                )
            }
        }

        // ---- Power button ----
        Box(
            modifier = Modifier.fillMaxWidth().padding(top = 20.dp, bottom = 12.dp),
            contentAlignment = Alignment.Center,
        ) {
            Box(contentAlignment = Alignment.Center, modifier = Modifier.size(212.dp)) {
                // Soft halo: steady when connected, pulsing while connecting, absent otherwise.
                val haloAlpha = when {
                    connecting -> pulse * .22f
                    connected -> .16f
                    else -> 0f
                }
                Box(Modifier.size(212.dp).clip(CircleShape).background(accent.copy(alpha = haloAlpha)))
                Box(
                    modifier = Modifier
                        .size(156.dp)
                        .scale(powerScale)
                        .clip(CircleShape)
                        .background(buttonFill)
                        .border(1.dp, MaterialTheme.colorScheme.outline.copy(alpha = if (connected) 0f else .6f), CircleShape)
                        .clickable(
                            interactionSource = powerInteraction,
                            indication = null,
                            role = Role.Button,
                            onClickLabel = if (state == ConnectionState.DISCONNECTED) "Connect" else "Disconnect",
                        ) {
                            if (app.settingsStore.hapticsEnabled) {
                                haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                            }
                            when {
                                state == ConnectionState.DISCONNECTED -> onRequestConnect()
                                // Only ask when fully connected; cancelling a connect in progress is instant.
                                connected && app.settingsStore.confirmDisconnect -> showDisconnectConfirm = true
                                else -> onDisconnect()
                            }
                        },
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        imageVector = if (connected) Icons.Filled.Check else Icons.Filled.PowerSettingsNew,
                        contentDescription = when (state) {
                            ConnectionState.DISCONNECTED -> "Connect VPN"
                            ConnectionState.CONNECTING -> "Cancel connecting"
                            ConnectionState.CONNECTED -> "Disconnect VPN"
                        },
                        tint = glyphColor,
                        modifier = Modifier.size(64.dp),
                    )
                }
                if (connecting) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(156.dp),
                        color = status.connecting,
                        strokeWidth = 3.dp,
                    )
                }
            }
        }
        Text(
            text = when (state) {
                ConnectionState.DISCONNECTED -> "Tap to connect"
                ConnectionState.CONNECTING -> "Tap to cancel"
                ConnectionState.CONNECTED -> "Tap to disconnect"
            },
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
            modifier = Modifier.fillMaxWidth().padding(bottom = 26.dp),
        )

        // ---- Error (dismissible) ----
        val error = lastError
        if (error != null && !connecting) {
            IosSection {
                Row(
                    modifier = Modifier.fillMaxWidth().padding(16.dp),
                    verticalAlignment = Alignment.Top,
                ) {
                    Icon(
                        imageVector = Icons.Filled.Warning,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.error,
                        modifier = Modifier.size(22.dp),
                    )
                    Spacer(Modifier.width(12.dp))
                    Column(Modifier.weight(1f)) {
                        Text(error, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface)
                        Spacer(Modifier.height(6.dp))
                        Text(
                            text = "Dismiss",
                            style = MaterialTheme.typography.labelLarge,
                            color = MaterialTheme.colorScheme.primary,
                            modifier = Modifier
                                .clip(RoundedCornerShape(6.dp))
                                .clickable(role = Role.Button) { FoxyVpnService.clearError() }
                                .padding(vertical = 4.dp),
                        )
                    }
                }
            }
        }

        // ---- Location ----
        IosSection(
            header = "Location",
            footer = if (state != ConnectionState.DISCONNECTED) {
                "A new location is used the next time you connect."
            } else {
                null
            },
        ) {
            IosRow(
                title = countryLabel ?: "Recommended",
                subtitle = if (countryLabel == null) "Picked automatically for the best speed" else null,
                icon = Icons.Filled.Public,
                iconTint = status.blue,
                onClick = onOpenServers,
            )
        }

        // ---- Monthly data ----
        val quota = entitlement
        val quotaMax = quota?.maxBytes
        val quotaLeft = quota?.quotaRemaining
        if (quota != null && quota.limitedBandwidth && quotaMax != null && quotaMax > 0L && quotaLeft != null) {
            val usedFraction = (1f - quotaLeft.toFloat() / quotaMax.toFloat()).coerceIn(0f, 1f)
            IosSection(
                header = "Data this month",
                footer = if (usedFraction > .9f) "Almost used up. Connections stop when it runs out." else null,
                footerColor = if (usedFraction > .9f) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant,
            ) {
                IosRow(title = "Remaining", value = "${formatBytes(quotaLeft)} of ${formatBytes(quotaMax)}")
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(start = 16.dp, end = 16.dp, bottom = 16.dp)
                        .height(8.dp)
                        .clip(RoundedCornerShape(4.dp))
                        .background(status.fill),
                ) {
                    Box(
                        Modifier
                            .fillMaxHeight()
                            .fillMaxWidth(usedFraction)
                            .clip(RoundedCornerShape(4.dp))
                            .background(if (usedFraction > .9f) MaterialTheme.colorScheme.error else status.blue),
                    )
                }
            }
        }

        // ---- Traffic ----
        IosSection(header = if (connected) "This session" else "Last session") {
            Row(Modifier.fillMaxWidth().height(IntrinsicSize.Min)) {
                StatCell(
                    modifier = Modifier.weight(1f),
                    label = "Download",
                    icon = Icons.Filled.ArrowDownward,
                    tint = status.blue,
                    total = formatBytes(trafficUsage.downloadedBytes),
                    rate = formatBytesPerSecond(if (connected) trafficUsage.downloadBytesPerSecond else 0L),
                )
                Box(Modifier.width(0.5.dp).fillMaxHeight().background(status.separator))
                StatCell(
                    modifier = Modifier.weight(1f),
                    label = "Upload",
                    icon = Icons.Filled.ArrowUpward,
                    tint = status.indigo,
                    total = formatBytes(trafficUsage.uploadedBytes),
                    rate = formatBytesPerSecond(if (connected) trafficUsage.uploadBytesPerSecond else 0L),
                )
            }
        }
    }
}

@Composable
private fun StatCell(
    modifier: Modifier,
    label: String,
    icon: ImageVector,
    tint: Color,
    total: String,
    rate: String,
) {
    Column(modifier.padding(horizontal = 16.dp, vertical = 14.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(imageVector = icon, contentDescription = null, tint = tint, modifier = Modifier.size(16.dp))
            Spacer(Modifier.width(6.dp))
            Text(label, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Spacer(Modifier.height(6.dp))
        Text(
            text = total,
            style = MaterialTheme.typography.headlineSmall,
            color = MaterialTheme.colorScheme.onSurface,
            maxLines = 1,
        )
        Text(text = rate, style = MaterialTheme.typography.bodySmall, color = tint, maxLines = 1)
    }
}
