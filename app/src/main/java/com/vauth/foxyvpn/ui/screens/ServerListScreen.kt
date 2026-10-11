package com.vauth.foxyvpn.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Bolt
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.vauth.foxyvpn.data.ProxyStateStore
import com.vauth.foxyvpn.data.RECOMMENDED_COUNTRY_CODE
import com.vauth.foxyvpn.data.ServerListClient
import com.vauth.foxyvpn.data.model.VpnCountry
import com.vauth.foxyvpn.ui.theme.IosDivider
import com.vauth.foxyvpn.ui.theme.IosRow
import com.vauth.foxyvpn.ui.theme.IosScreen
import com.vauth.foxyvpn.ui.theme.IosSection
import com.vauth.foxyvpn.ui.theme.LocalFoxyStatusColors
import com.vauth.foxyvpn.vpn.PingUtil
import kotlinx.coroutines.CancellationException

private enum class PingState { PENDING, DONE, FAILED }

@Composable
fun ServerListScreen(
    serverListClient: ServerListClient,
    proxyStateStore: ProxyStateStore,
    onServerSelected: () -> Unit,
    onBack: () -> Unit,
) {
    var countries by remember { mutableStateOf<List<VpnCountry>>(emptyList()) }
    var isLoading by remember { mutableStateOf(true) }
    var loadError by remember { mutableStateOf<String?>(null) }
    // Shown above the list when a tap cannot be honoured; the old screen only showed errors
    // while the list was empty, so these messages were never visible.
    var pickError by remember { mutableStateOf<String?>(null) }
    var reloadCounter by remember { mutableIntStateOf(0) }

    val selectedState by proxyStateStore.selectedProxyFlow.collectAsState()
    val status = LocalFoxyStatusColors.current

    val pingResults = remember { mutableStateMapOf<String, Int>() }
    val pingStates = remember { mutableStateMapOf<String, PingState>() }

    LaunchedEffect(reloadCounter) {
        isLoading = true
        loadError = null
        try {
            countries = serverListClient.fetchCountries()
        } catch (cancellation: CancellationException) {
            throw cancellation
        } catch (failure: Exception) {
            loadError = failure.message ?: "Failed to load the server list"
        }
        isLoading = false
    }

    val ordered = remember(countries) {
        countries
            .filter { it.code != RECOMMENDED_COUNTRY_CODE }
            .sortedBy { it.name }
    }
    val recommended = remember(countries) { countries.firstOrNull { it.code == RECOMMENDED_COUNTRY_CODE } }

    IosScreen(
        title = "Locations",
        onBack = onBack,
        backLabel = "Home",
        scrollable = false,
    ) {
        when {
            isLoading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator(color = MaterialTheme.colorScheme.primary)
            }

            loadError != null && countries.isEmpty() -> Column(
                modifier = Modifier.fillMaxSize().padding(32.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Spacer(Modifier.size(48.dp))
                Text(
                    text = loadError.orEmpty(),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                )
                Spacer(Modifier.size(16.dp))
                Text(
                    text = "Try again",
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier
                        .clip(RoundedCornerShape(10.dp))
                        .clickable(role = Role.Button) { reloadCounter++ }
                        .padding(horizontal = 16.dp, vertical = 10.dp),
                )
            }

            else -> LazyColumn(Modifier.fillMaxSize()) {
                if (pickError != null) {
                    item(key = "pick-error") {
                        Text(
                            text = pickError.orEmpty(),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error,
                            modifier = Modifier.padding(start = 32.dp, end = 32.dp, bottom = 12.dp),
                        )
                    }
                }
                if (recommended != null) {
                    item(key = "recommended") {
                        IosSection(footer = "Picks a fast server for you automatically.") {
                            IosRow(
                                title = "Recommended",
                                icon = Icons.Filled.Bolt,
                                iconTint = status.connecting,
                                showChevron = false,
                                trailing = {
                                    if (selectedState?.countryCode == recommended.code) {
                                        Icon(
                                            imageVector = Icons.Filled.Check,
                                            contentDescription = "Selected",
                                            tint = MaterialTheme.colorScheme.primary,
                                        )
                                    }
                                },
                                modifier = Modifier.clickable(role = Role.Button) {
                                    val chosen = ServerListClient.candidatesForCountry(countries, recommended.code).randomOrNull()
                                    if (chosen != null) {
                                        pickError = null
                                        proxyStateStore.save(chosen)
                                        onServerSelected()
                                    } else {
                                        pickError = "No servers are available right now."
                                    }
                                },
                            )
                        }
                    }
                }
                items(ordered, key = { it.code }) { country ->
                    IosSection(header = country.name) {
                        country.cities.forEachIndexed { index, city ->
                            if (index > 0) IosDivider()
                            val pingKey = "${country.code}:${city.code}"
                            val pingMs = pingResults[pingKey]
                            val pingState = pingStates[pingKey] ?: PingState.PENDING

                            LaunchedEffect(pingKey) {
                                if (pingStates.containsKey(pingKey)) return@LaunchedEffect
                                val target = city.servers.firstOrNull { !it.quarantined }
                                    ?.let { ServerListClient.defaultConnectTarget(it) }
                                if (target == null) {
                                    pingStates[pingKey] = PingState.FAILED
                                    return@LaunchedEffect
                                }
                                val result = PingUtil.measureTcpLatencyMs(target.first, target.second)
                                if (result != null) {
                                    pingResults[pingKey] = result
                                    pingStates[pingKey] = PingState.DONE
                                } else {
                                    pingStates[pingKey] = PingState.FAILED
                                }
                            }

                            val isSelected = selectedState?.countryCode == country.code &&
                                selectedState?.cityCode == city.code
                            val serverCount = city.servers.size
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .clickable(role = Role.Button) {
                                        val chosen = ServerListClient
                                            .candidatesForCity(countries, country.code, city.code)
                                            .randomOrNull()
                                        if (chosen != null) {
                                            pickError = null
                                            proxyStateStore.save(chosen)
                                            onServerSelected()
                                        } else {
                                            pickError = "No servers are available in ${city.name.ifBlank { city.code }}."
                                        }
                                    }
                                    .padding(horizontal = 16.dp, vertical = 12.dp),
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Column(Modifier.weight(1f)) {
                                    Text(
                                        text = city.name.ifBlank { city.code },
                                        style = MaterialTheme.typography.bodyLarge,
                                        color = MaterialTheme.colorScheme.onSurface,
                                    )
                                    Text(
                                        text = if (serverCount == 1) "1 server" else "$serverCount servers",
                                        style = MaterialTheme.typography.bodySmall,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    )
                                }
                                val dotColor = when {
                                    pingMs == null -> status.gray
                                    pingMs < 150 -> status.connected
                                    pingMs < 300 -> status.connecting
                                    else -> MaterialTheme.colorScheme.error
                                }
                                Box(Modifier.size(8.dp).clip(CircleShape).background(dotColor))
                                Spacer(Modifier.width(6.dp))
                                Text(
                                    text = when {
                                        pingMs != null -> "$pingMs ms"
                                        pingState == PingState.FAILED -> "—"
                                        else -> "…"
                                    },
                                    style = MaterialTheme.typography.bodyMedium,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                                Spacer(Modifier.width(10.dp))
                                Box(Modifier.size(22.dp), contentAlignment = Alignment.Center) {
                                    if (isSelected) {
                                        Icon(
                                            imageVector = Icons.Filled.Check,
                                            contentDescription = "Selected",
                                            tint = MaterialTheme.colorScheme.primary,
                                        )
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
