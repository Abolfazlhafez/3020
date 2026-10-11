package com.vauth.foxyvpn.ui.screens

import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.Apps
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Code
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Dns
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.PowerSettingsNew
import androidx.compose.material.icons.filled.Shield
import androidx.compose.material.icons.filled.Vibration
import androidx.compose.material.icons.filled.Router
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import com.vauth.foxyvpn.BuildConfig
import com.vauth.foxyvpn.data.SettingsStore
import com.vauth.foxyvpn.data.model.ConnectionState
import com.vauth.foxyvpn.ui.theme.IosDivider
import com.vauth.foxyvpn.ui.theme.IosIconRowIndent
import com.vauth.foxyvpn.ui.theme.IosRow
import com.vauth.foxyvpn.ui.theme.IosScreen
import com.vauth.foxyvpn.ui.theme.IosSection
import com.vauth.foxyvpn.ui.theme.IosSegmented
import com.vauth.foxyvpn.ui.theme.IosSwitchRow
import com.vauth.foxyvpn.ui.theme.LocalFoxyStatusColors
import com.vauth.foxyvpn.ui.theme.ThemeMode
import com.vauth.foxyvpn.vpn.FoxyVpnService

private const val GITHUB_URL = "https://github.com/vauth/foxyvpn"

private val THEME_OPTIONS = listOf(
    ThemeMode.SYSTEM to "System",
    ThemeMode.LIGHT to "Light",
    ThemeMode.DARK to "Dark",
)

@Composable
fun SettingsScreen(
    settingsStore: SettingsStore,
    themeMode: ThemeMode,
    onThemeModeChange: (ThemeMode) -> Unit,
    onOpenLogs: () -> Unit,
    onOpenAccount: () -> Unit,
    onSignOut: () -> Unit,
    onBack: () -> Unit,
) {
    val context = LocalContext.current
    val status = LocalFoxyStatusColors.current
    val vpnState by FoxyVpnService.state.collectAsState()
    var connectOnLaunch by remember { mutableStateOf(settingsStore.connectOnLaunch) }
    var hapticsEnabled by remember { mutableStateOf(settingsStore.hapticsEnabled) }
    var confirmDisconnect by remember { mutableStateOf(settingsStore.confirmDisconnect) }
    var exitCheckEnabled by remember { mutableStateOf(settingsStore.exitCheckEnabled) }
    var dohProvider by remember { mutableStateOf(settingsStore.dohProvider) }
    var customDnsEnabled by remember { mutableStateOf(settingsStore.customDnsEnabled) }
    var customDnsServer by remember { mutableStateOf(settingsStore.customDnsServer) }
    var socksBindAddress by remember { mutableStateOf(settingsStore.socksBindAddress) }
    var socksPort by remember { mutableStateOf(settingsStore.socksPort) }
    var proxyOnlyMode by remember { mutableStateOf(settingsStore.proxyOnlyMode) }
    var customEdgeAddress by remember { mutableStateOf(settingsStore.customEdgeAddress) }
    var upstreamProxyEnabled by remember { mutableStateOf(settingsStore.upstreamProxyEnabled) }
    var upstreamProxyType by remember { mutableStateOf(settingsStore.upstreamProxyType) }
    var upstreamProxyHost by remember { mutableStateOf(settingsStore.upstreamProxyHost) }
    var upstreamProxyPort by remember { mutableStateOf(settingsStore.upstreamProxyPort) }
    var upstreamProxyUsername by remember { mutableStateOf(settingsStore.upstreamProxyUsername) }
    var upstreamProxyPassword by remember { mutableStateOf(settingsStore.upstreamProxyPassword) }
    var excludedApps by remember { mutableStateOf(settingsStore.excludedApps) }
    var showDohProviderDialog by remember { mutableStateOf(false) }
    var showCustomDnsDialog by remember { mutableStateOf(false) }
    var showSocksBindDialog by remember { mutableStateOf(false) }
    var showSocksPortDialog by remember { mutableStateOf(false) }
    var showEdgeAddressDialog by remember { mutableStateOf(false) }
    var showUpstreamProxyTypeDialog by remember { mutableStateOf(false) }
    var showUpstreamProxyAddressDialog by remember { mutableStateOf(false) }
    var showUpstreamProxyCredentialsDialog by remember { mutableStateOf(false) }
    var showSplitTunnelDialog by remember { mutableStateOf(false) }
    var showSignOutDialog by remember { mutableStateOf(false) }

    fun openUrl(url: String) {
        // No browser installed (or a locked-down profile) used to crash the app here.
        runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) }
    }

    if (showDohProviderDialog) {
        DohProviderPickerDialog(
            current = dohProvider,
            onDismiss = { showDohProviderDialog = false },
            onConfirm = {
                dohProvider = it
                settingsStore.dohProvider = it
                showDohProviderDialog = false
            },
        )
    }
    if (showCustomDnsDialog) {
        CustomDnsPickerDialog(
            current = customDnsServer,
            onDismiss = { showCustomDnsDialog = false },
            onConfirm = {
                customDnsServer = it
                settingsStore.customDnsServer = it
                showCustomDnsDialog = false
            },
        )
    }
    if (showSocksBindDialog) {
        SocksBindAddressPickerDialog(
            current = socksBindAddress,
            onDismiss = { showSocksBindDialog = false },
            onConfirm = {
                socksBindAddress = it
                settingsStore.socksBindAddress = it
                showSocksBindDialog = false
            },
        )
    }
    if (showSocksPortDialog) {
        SocksPortPickerDialog(
            current = socksPort,
            onDismiss = { showSocksPortDialog = false },
            onConfirm = {
                socksPort = it
                settingsStore.socksPort = it
                showSocksPortDialog = false
            },
        )
    }
    if (showEdgeAddressDialog) {
        CustomEdgeAddressDialog(
            current = customEdgeAddress,
            onDismiss = { showEdgeAddressDialog = false },
            onConfirm = {
                customEdgeAddress = it
                settingsStore.customEdgeAddress = it
                showEdgeAddressDialog = false
            },
        )
    }
    if (showUpstreamProxyTypeDialog) {
        UpstreamProxyTypePickerDialog(
            current = upstreamProxyType,
            onDismiss = { showUpstreamProxyTypeDialog = false },
            onConfirm = {
                upstreamProxyType = it
                settingsStore.upstreamProxyType = it
                showUpstreamProxyTypeDialog = false
            },
        )
    }
    if (showUpstreamProxyAddressDialog) {
        UpstreamProxyAddressDialog(
            currentHost = upstreamProxyHost,
            currentPort = upstreamProxyPort,
            onDismiss = { showUpstreamProxyAddressDialog = false },
            onConfirm = { host, port ->
                upstreamProxyHost = host
                upstreamProxyPort = port
                settingsStore.upstreamProxyHost = host
                settingsStore.upstreamProxyPort = port
                showUpstreamProxyAddressDialog = false
            },
        )
    }
    if (showUpstreamProxyCredentialsDialog) {
        UpstreamProxyCredentialsDialog(
            currentUsername = upstreamProxyUsername,
            currentPassword = upstreamProxyPassword,
            onDismiss = { showUpstreamProxyCredentialsDialog = false },
            onConfirm = { username, password ->
                upstreamProxyUsername = username
                upstreamProxyPassword = password
                settingsStore.upstreamProxyUsername = username
                settingsStore.upstreamProxyPassword = password
                showUpstreamProxyCredentialsDialog = false
            },
        )
    }
    if (showSplitTunnelDialog) {
        SplitTunnelDialog(
            current = excludedApps,
            onDismiss = { showSplitTunnelDialog = false },
            onConfirm = {
                excludedApps = it
                settingsStore.excludedApps = it
                showSplitTunnelDialog = false
            },
        )
    }
    if (showSignOutDialog) {
        AlertDialog(
        shape = RoundedCornerShape(20.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
            onDismissRequest = { showSignOutDialog = false },
            title = { Text("Sign out?") },
            text = { Text("The VPN will disconnect and you will need to sign in again to reconnect.") },
            confirmButton = {
                TextButton(onClick = {
                    showSignOutDialog = false
                    onSignOut()
                }) { Text("Sign out", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = {
                TextButton(onClick = { showSignOutDialog = false }) { Text("Cancel") }
            },
        )
    }

    IosScreen(title = "Settings", onBack = onBack, backLabel = "Home") {
        if (vpnState != ConnectionState.DISCONNECTED) {
            Text(
                text = "You are connected. Changes made here apply the next time you connect.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(start = 32.dp, end = 32.dp, bottom = 16.dp),
            )
        }

        // ---- Appearance (new option) ----
        IosSection(header = "Appearance") {
            Box(Modifier.padding(12.dp)) {
                IosSegmented(
                    options = THEME_OPTIONS.map { it.second },
                    selectedIndex = THEME_OPTIONS.indexOfFirst { it.first == themeMode }.coerceAtLeast(0),
                    onSelect = { index -> onThemeModeChange(THEME_OPTIONS[index].first) },
                )
            }
        }

        // ---- General ----
        IosSection(
            header = "General",
            footer = "Connect on launch starts the VPN as soon as FoxyVPN opens. Android asks for VPN permission the first time.",
        ) {
            IosSwitchRow(
                title = "Connect on launch",
                checked = connectOnLaunch,
                onCheckedChange = {
                    connectOnLaunch = it
                    settingsStore.connectOnLaunch = it
                },
                icon = Icons.Filled.PowerSettingsNew,
                iconTint = status.connected,
            )
            IosDivider(IosIconRowIndent)
            IosSwitchRow(
                title = "Confirm before disconnecting",
                subtitle = "Ask first when you tap the power button while connected",
                checked = confirmDisconnect,
                onCheckedChange = {
                    confirmDisconnect = it
                    settingsStore.confirmDisconnect = it
                },
                icon = Icons.Filled.Shield,
                iconTint = status.indigo,
            )
            IosDivider(IosIconRowIndent)
            IosSwitchRow(
                title = "Haptic feedback",
                subtitle = "A small vibration when you tap the power button",
                checked = hapticsEnabled,
                onCheckedChange = {
                    hapticsEnabled = it
                    settingsStore.hapticsEnabled = it
                },
                icon = Icons.Filled.Vibration,
                iconTint = status.teal,
            )
            IosDivider(IosIconRowIndent)
            IosSwitchRow(
                title = "Verify exit location",
                subtitle = "Check that traffic leaves in the selected country",
                checked = exitCheckEnabled,
                onCheckedChange = {
                    exitCheckEnabled = it
                    settingsStore.exitCheckEnabled = it
                },
                icon = Icons.Filled.CheckCircle,
                iconTint = status.blue,
            )
        }

        // ---- DNS ----
        IosSection(header = "DNS") {
            IosRow(
                title = "Encrypted DNS",
                value = if (dohProvider == SettingsStore.DohProvider.OFF) "Off" else dohProvider.label,
                icon = Icons.Filled.Lock,
                iconTint = status.indigo,
                onClick = { showDohProviderDialog = true },
            )
            IosDivider(IosIconRowIndent)
            IosSwitchRow(
                title = "Custom DNS server",
                subtitle = "Send app DNS queries to a server you choose",
                checked = customDnsEnabled,
                onCheckedChange = {
                    customDnsEnabled = it
                    settingsStore.customDnsEnabled = it
                },
                icon = Icons.Filled.Dns,
                iconTint = status.teal,
            )
            if (customDnsEnabled) {
                IosDivider(IosIconRowIndent)
                IosRow(
                    title = "DNS server",
                    value = SettingsStore.CUSTOM_DNS_PRESETS.firstOrNull { it.first == customDnsServer }?.second
                        ?: customDnsServer,
                    onClick = { showCustomDnsDialog = true },
                )
            }
        }

        // ---- Local proxy ----
        IosSection(
            header = "Local proxy",
            footer = if (socksBindAddress == "0.0.0.0") {
                "Warning: with all interfaces selected, other devices on your network can use this proxy without a password."
            } else {
                null
            },
            footerColor = if (socksBindAddress == "0.0.0.0") {
                MaterialTheme.colorScheme.error
            } else {
                MaterialTheme.colorScheme.onSurfaceVariant
            },
        ) {
            IosSwitchRow(
                title = "Proxy-only mode",
                subtitle = "Run just the local SOCKS5 proxy, without a VPN interface",
                checked = proxyOnlyMode,
                onCheckedChange = {
                    proxyOnlyMode = it
                    settingsStore.proxyOnlyMode = it
                },
                icon = Icons.Filled.Router,
                iconTint = status.connecting,
            )
            IosDivider()
            IosRow(
                title = "Local address",
                value = socksBindAddress,
                onClick = { showSocksBindDialog = true },
            )
            IosDivider()
            IosRow(
                title = "Local port",
                value = socksPort.toString(),
                onClick = { showSocksPortDialog = true },
            )
        }

        // ---- Split tunneling ----
        IosSection(header = "Split tunneling") {
            IosRow(
                title = "Excluded apps",
                subtitle = "These apps use your normal connection",
                value = if (excludedApps.isEmpty()) "None" else excludedApps.size.toString(),
                icon = Icons.Filled.Apps,
                iconTint = status.blue,
                onClick = { showSplitTunnelDialog = true },
            )
        }

        // ---- Advanced ----
        IosSection(header = "Advanced") {
            IosRow(
                title = "Custom edge address",
                value = customEdgeAddress.ifBlank { "Not set" },
                onClick = { showEdgeAddressDialog = true },
            )
            IosDivider()
            IosSwitchRow(
                title = "Chain through upstream proxy",
                subtitle = "Reach the VPN server through another proxy first",
                checked = upstreamProxyEnabled,
                onCheckedChange = {
                    upstreamProxyEnabled = it
                    settingsStore.upstreamProxyEnabled = it
                },
            )
            if (upstreamProxyEnabled) {
                IosDivider()
                IosRow(
                    title = "Proxy type",
                    value = upstreamProxyType.name,
                    onClick = { showUpstreamProxyTypeDialog = true },
                )
                IosDivider()
                IosRow(
                    title = "Proxy address",
                    value = if (upstreamProxyHost.isBlank()) "Not set" else "$upstreamProxyHost:$upstreamProxyPort",
                    onClick = { showUpstreamProxyAddressDialog = true },
                )
                IosDivider()
                IosRow(
                    title = "Proxy credentials",
                    value = if (upstreamProxyUsername.isBlank()) "None" else upstreamProxyUsername,
                    onClick = { showUpstreamProxyCredentialsDialog = true },
                )
            }
        }

        // ---- About ----
        IosSection(header = "About") {
            IosRow(
                title = "Logs",
                subtitle = "Recent connection activity",
                icon = Icons.Filled.Description,
                iconTint = status.gray,
                onClick = onOpenLogs,
            )
            IosDivider(IosIconRowIndent)
            IosRow(
                title = "Source code",
                icon = Icons.Filled.Code,
                iconTint = status.indigo,
                onClick = { openUrl(GITHUB_URL) },
            )
            IosDivider(IosIconRowIndent)
            IosRow(title = "Version", value = BuildConfig.VERSION_NAME)
        }

        // ---- Account ----
        IosSection(header = "Account") {
            IosRow(
                title = "Manage account",
                subtitle = "Plan and data remaining",
                icon = Icons.Filled.AccountCircle,
                iconTint = status.blue,
                onClick = onOpenAccount,
            )
            IosDivider(IosIconRowIndent)
            IosRow(
                title = "Sign out",
                titleColor = MaterialTheme.colorScheme.error,
                showChevron = false,
                onClick = { showSignOutDialog = true },
            )
        }
    }
}

@Composable
private fun DohProviderPickerDialog(
    current: SettingsStore.DohProvider,
    onDismiss: () -> Unit,
    onConfirm: (SettingsStore.DohProvider) -> Unit,
) {
    var selected by remember { mutableStateOf(current) }

    AlertDialog(
        shape = RoundedCornerShape(20.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
        onDismissRequest = onDismiss,
        title = { Text("Encrypted DNS") },
        text = {
            Column {
                SettingsStore.DohProvider.entries.forEach { provider ->
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier
                            .fillMaxWidth()
                            .selectable(selected = selected == provider, onClick = { selected = provider })
                            .padding(vertical = 4.dp),
                    ) {
                        RadioButton(selected = selected == provider, onClick = { selected = provider })
                        Column(Modifier.padding(start = 8.dp)) {
                            Text(provider.label)
                            val detail = when (provider) {
                                SettingsStore.DohProvider.AUTOMATIC -> "Use whichever answers first"
                                SettingsStore.DohProvider.OFF -> "Use the network's own DNS"
                                else -> provider.addresses.joinToString(", ")
                            }
                            Text(
                                detail,
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { onConfirm(selected) }) { Text("Save") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        },
    )
}

@Composable
private fun CustomDnsPickerDialog(
    current: String,
    onDismiss: () -> Unit,
    onConfirm: (String) -> Unit,
) {
    var value by remember { mutableStateOf(current) }
    val isValid = SettingsStore.isValidDnsServer(value)

    AlertDialog(
        shape = RoundedCornerShape(20.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
        onDismissRequest = onDismiss,
        title = { Text("DNS server") },
        text = {
            Column {
                SettingsStore.CUSTOM_DNS_PRESETS.forEach { (address, label) ->
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier
                            .fillMaxWidth()
                            .selectable(selected = value == address, onClick = { value = address })
                            .padding(vertical = 4.dp),
                    ) {
                        RadioButton(selected = value == address, onClick = { value = address })
                        Text(label, modifier = Modifier.padding(start = 8.dp))
                    }
                }
                OutlinedTextField(
                    value = value,
                    onValueChange = { value = it.trim() },
                    label = { Text("IPv4 address") },
                    singleLine = true,
                    isError = value.isNotBlank() && !isValid,
                    modifier = Modifier.padding(top = 8.dp),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { onConfirm(value) }, enabled = isValid) { Text("Save") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        },
    )
}

@Composable
private fun CustomEdgeAddressDialog(
    current: String,
    onDismiss: () -> Unit,
    onConfirm: (String) -> Unit,
) {
    var value by remember { mutableStateOf(current) }
    val trimmed = value.trim()
    val isValid = trimmed.isEmpty() || SettingsStore.isValidEdgeHost(trimmed)

    AlertDialog(
        shape = RoundedCornerShape(20.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
        onDismissRequest = onDismiss,
        title = { Text("Custom edge address") },
        text = {
            Column {
                OutlinedTextField(
                    value = value,
                    onValueChange = { value = it },
                    label = { Text("Host or IP") },
                    singleLine = true,
                    isError = !isValid,
                )
                Text(
                    "Leave empty to resolve the server hostname normally.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 8.dp),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { onConfirm(trimmed) }, enabled = isValid) { Text("Save") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        },
    )
}

@Composable
private fun SocksBindAddressPickerDialog(
    current: String,
    onDismiss: () -> Unit,
    onConfirm: (String) -> Unit,
) {
    var selected by remember { mutableStateOf(current) }

    AlertDialog(
        shape = RoundedCornerShape(20.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
        onDismissRequest = onDismiss,
        title = { Text("Local address") },
        text = {
            Column {
                SettingsStore.SOCKS_BIND_ADDRESS_PRESETS.forEach { (address, label) ->
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier
                            .fillMaxWidth()
                            .selectable(selected = selected == address, onClick = { selected = address })
                            .padding(vertical = 4.dp),
                    ) {
                        RadioButton(selected = selected == address, onClick = { selected = address })
                        Text(label, modifier = Modifier.padding(start = 8.dp))
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { onConfirm(selected) }) { Text("Save") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        },
    )
}

@Composable
private fun SocksPortPickerDialog(
    current: Int,
    onDismiss: () -> Unit,
    onConfirm: (Int) -> Unit,
) {
    var portText by remember { mutableStateOf(current.toString()) }
    val parsedPort = portText.toIntOrNull()
    val isValid = parsedPort != null && parsedPort in 1..65_535

    AlertDialog(
        shape = RoundedCornerShape(20.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
        onDismissRequest = onDismiss,
        title = { Text("Local port") },
        text = {
            OutlinedTextField(
                value = portText,
                onValueChange = { portText = it.filter(Char::isDigit).take(5) },
                label = { Text("Port (1\u201365535)") },
                singleLine = true,
                isError = portText.isNotBlank() && !isValid,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            )
        },
        confirmButton = {
            TextButton(onClick = { parsedPort?.let { onConfirm(it) } }, enabled = isValid) { Text("Save") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        },
    )
}

@Composable
private fun UpstreamProxyTypePickerDialog(
    current: SettingsStore.UpstreamProxyType,
    onDismiss: () -> Unit,
    onConfirm: (SettingsStore.UpstreamProxyType) -> Unit,
) {
    var selected by remember { mutableStateOf(current) }
    AlertDialog(
        shape = RoundedCornerShape(20.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
        onDismissRequest = onDismiss,
        title = { Text("Proxy type") },
        text = {
            Column {
                SettingsStore.UpstreamProxyType.entries.forEach { type ->
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier
                            .fillMaxWidth()
                            .selectable(selected = selected == type, onClick = { selected = type })
                            .padding(vertical = 4.dp),
                    ) {
                        RadioButton(selected = selected == type, onClick = { selected = type })
                        Text(type.name, modifier = Modifier.padding(start = 8.dp))
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { onConfirm(selected) }) { Text("Save") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        },
    )
}

@Composable
private fun UpstreamProxyAddressDialog(
    currentHost: String,
    currentPort: Int,
    onDismiss: () -> Unit,
    onConfirm: (String, Int) -> Unit,
) {
    var host by remember { mutableStateOf(currentHost) }
    var portText by remember { mutableStateOf(currentPort.toString()) }
    val parsedPort = portText.toIntOrNull()
    val isValid = host.isNotBlank() && parsedPort != null && parsedPort in 1..65_535

    AlertDialog(
        shape = RoundedCornerShape(20.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
        onDismissRequest = onDismiss,
        title = { Text("Proxy address") },
        text = {
            Column {
                OutlinedTextField(
                    value = host,
                    onValueChange = { host = it.trim() },
                    label = { Text("Host or IP") },
                    singleLine = true,
                )
                OutlinedTextField(
                    value = portText,
                    onValueChange = { portText = it.filter(Char::isDigit).take(5) },
                    label = { Text("Port (1\u201365535)") },
                    singleLine = true,
                    isError = portText.isNotBlank() && (parsedPort == null || parsedPort !in 1..65_535),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.padding(top = 8.dp),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { parsedPort?.let { onConfirm(host, it) } }, enabled = isValid) { Text("Save") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        },
    )
}

@Composable
private fun UpstreamProxyCredentialsDialog(
    currentUsername: String,
    currentPassword: String,
    onDismiss: () -> Unit,
    onConfirm: (String, String) -> Unit,
) {
    var username by remember { mutableStateOf(currentUsername) }
    var password by remember { mutableStateOf(currentPassword) }
    AlertDialog(
        shape = RoundedCornerShape(20.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
        onDismissRequest = onDismiss,
        title = { Text("Proxy credentials") },
        text = {
            Column {
                OutlinedTextField(
                    value = username,
                    onValueChange = { username = it },
                    label = { Text("Username (optional)") },
                    singleLine = true,
                )
                OutlinedTextField(
                    value = password,
                    onValueChange = { password = it },
                    label = { Text("Password (optional)") },
                    singleLine = true,
                    visualTransformation = PasswordVisualTransformation(),
                    modifier = Modifier.padding(top = 8.dp),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { onConfirm(username, password) }) { Text("Save") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        },
    )
}

private data class InstalledAppEntry(val packageName: String, val label: String)

@Composable
private fun SplitTunnelDialog(
    current: Set<String>,
    onDismiss: () -> Unit,
    onConfirm: (Set<String>) -> Unit,
) {
    val context = LocalContext.current
    var selected by remember { mutableStateOf(current) }
    var searchQuery by remember { mutableStateOf("") }

    val installedApps by androidx.compose.runtime.produceState<List<InstalledAppEntry>>(initialValue = emptyList()) {
        value = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            val packageManager = context.packageManager
            packageManager.getInstalledApplications(PackageManager.GET_META_DATA)
                .asSequence()
                .filter { it.packageName != context.packageName }
                .filter { packageManager.getLaunchIntentForPackage(it.packageName) != null }
                .map { info: android.content.pm.ApplicationInfo ->
                    InstalledAppEntry(info.packageName, info.loadLabel(packageManager).toString())
                }
                .sortedBy { it.label.lowercase() }
                .toList()
        }
    }
    val filteredApps = remember(installedApps, searchQuery) {
        if (searchQuery.isBlank()) {
            installedApps
        } else {
            installedApps.filter { it.label.contains(searchQuery, ignoreCase = true) }
        }
    }

    AlertDialog(
        shape = RoundedCornerShape(20.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
        onDismissRequest = onDismiss,
        title = { Text("Split tunneling") },
        text = {
            Column {
                Text(
                    "Selected apps bypass the VPN and use your normal connection.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(bottom = 8.dp),
                )
                OutlinedTextField(
                    value = searchQuery,
                    onValueChange = { searchQuery = it },
                    label = { Text("Search apps") },
                    leadingIcon = { Icon(Icons.Filled.Search, contentDescription = null) },
                    singleLine = true,
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(bottom = 8.dp),
                )
                if (installedApps.isEmpty()) {
                    androidx.compose.material3.CircularProgressIndicator(
                        modifier = Modifier.padding(24.dp).align(Alignment.CenterHorizontally)
                    )
                } else if (filteredApps.isEmpty()) {
                    Text(
                        "No apps match \"$searchQuery\".",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(vertical = 12.dp),
                    )
                } else {
                    LazyColumn(modifier = Modifier.height(360.dp)) {
                        items(filteredApps, key = { it.packageName }) { app ->
                            val isChecked = selected.contains(app.packageName)
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .clickable {
                                        selected = if (isChecked) {
                                            selected - app.packageName
                                        } else {
                                            selected + app.packageName
                                        }
                                    }
                                    .padding(vertical = 2.dp),
                            ) {
                                Checkbox(
                                    checked = isChecked,
                                    onCheckedChange = {
                                        selected = if (it) selected + app.packageName else selected - app.packageName
                                    },
                                )
                                Text(app.label, modifier = Modifier.padding(start = 8.dp))
                            }
                        }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { onConfirm(selected) }) { Text("Save") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        },
    )
}
