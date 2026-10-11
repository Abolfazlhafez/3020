package com.vauth.foxyvpn.ui.screens

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.vauth.foxyvpn.data.FxaAuthRepository
import com.vauth.foxyvpn.data.GUARDIAN_ENDPOINT_DEFAULT
import com.vauth.foxyvpn.data.GuardianClient
import com.vauth.foxyvpn.data.formatBytes
import com.vauth.foxyvpn.data.model.Entitlement
import com.vauth.foxyvpn.ui.theme.IosDivider
import com.vauth.foxyvpn.ui.theme.IosRow
import com.vauth.foxyvpn.ui.theme.IosScreen
import com.vauth.foxyvpn.ui.theme.IosSection
import com.vauth.foxyvpn.ui.theme.LocalFoxyStatusColors
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch

@Composable
fun AccountScreen(
    authRepository: FxaAuthRepository,
    onBack: () -> Unit,
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val status = LocalFoxyStatusColors.current
    var entitlement by remember { mutableStateOf<Entitlement?>(null) }
    var isLoading by remember { mutableStateOf(true) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    fun refresh() {
        isLoading = true
        errorMessage = null
        scope.launch {
            val accessToken = authRepository.currentAccessToken()
            if (accessToken == null) {
                isLoading = false
                errorMessage = "You are not signed in."
                return@launch
            }
            try {
                entitlement = GuardianClient().fetchUserInfo(GUARDIAN_ENDPOINT_DEFAULT, accessToken)
            } catch (cancellation: CancellationException) {
                throw cancellation
            } catch (failure: Exception) {
                errorMessage = failure.message ?: "Could not load account info"
            }
            isLoading = false
        }
    }

    LaunchedEffect(Unit) { refresh() }

    IosScreen(
        title = "Account",
        onBack = onBack,
        backLabel = "Settings",
        actions = {
            Box(
                modifier = Modifier
                    .padding(end = 8.dp)
                    .size(38.dp)
                    .clip(CircleShape)
                    .clickable(role = Role.Button) { refresh() },
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    imageVector = Icons.Filled.Refresh,
                    contentDescription = "Refresh",
                    tint = MaterialTheme.colorScheme.primary,
                )
            }
        },
    ) {
        // Avatar header
        Column(
            modifier = Modifier.fillMaxWidth().padding(bottom = 24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Box(
                modifier = Modifier.size(72.dp).clip(CircleShape).background(status.blue),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    imageVector = Icons.Filled.Person,
                    contentDescription = null,
                    tint = Color.White,
                    modifier = Modifier.size(40.dp),
                )
            }
            Spacer(Modifier.height(10.dp))
            Text("Firefox account", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onBackground)
        }

        val info = entitlement
        when {
            isLoading -> Box(Modifier.fillMaxWidth().padding(top = 24.dp), contentAlignment = Alignment.Center) {
                CircularProgressIndicator(color = MaterialTheme.colorScheme.primary)
            }

            errorMessage != null -> Column(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 32.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Text(
                    text = errorMessage.orEmpty(),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.error,
                    textAlign = TextAlign.Center,
                )
                Spacer(Modifier.height(12.dp))
                Text(
                    text = "Try again",
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier
                        .clip(RoundedCornerShape(10.dp))
                        .clickable(role = Role.Button) { refresh() }
                        .padding(horizontal = 16.dp, vertical = 10.dp),
                )
            }

            info != null -> {
                IosSection(header = "Plan") {
                    // A free Firefox account is not an "inactive" subscription — it is the free tier.
                    IosRow(title = "Plan", value = if (info.subscribed) "Subscribed" else "Free")
                    IosDivider()
                    IosRow(title = "Account ID", value = info.uid.ifBlank { "—" })
                }

                val remaining = info.quotaRemaining
                val max = info.maxBytes
                IosSection(
                    header = "Data",
                    footer = if (info.limitedBandwidth) "Your allowance resets monthly." else null,
                ) {
                    IosRow(
                        title = "Remaining",
                        value = when {
                            !info.limitedBandwidth -> "Unlimited"
                            remaining != null -> formatBytes(remaining)
                            max != null -> "Up to ${formatBytes(max)}"
                            else -> "Limited"
                        },
                    )
                    if (info.limitedBandwidth && remaining != null && max != null && max > 0L) {
                        val usedFraction = (1f - remaining.toFloat() / max.toFloat()).coerceIn(0f, 1f)
                        val barColor = if (usedFraction > .9f) {
                            MaterialTheme.colorScheme.error
                        } else {
                            MaterialTheme.colorScheme.primary
                        }
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
                                    .background(barColor),
                            )
                        }
                    }
                }

                IosSection {
                    IosRow(
                        title = "Manage subscription",
                        onClick = {
                            runCatching {
                                context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(GUARDIAN_ENDPOINT_DEFAULT)))
                            }
                        },
                    )
                }
            }
        }
    }
}
