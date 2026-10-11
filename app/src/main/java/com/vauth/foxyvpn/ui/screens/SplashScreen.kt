package com.vauth.foxyvpn.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Shield
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.vauth.foxyvpn.data.FxaAuthRepository
import com.vauth.foxyvpn.data.SessionStatus
import com.vauth.foxyvpn.ui.theme.LocalFoxyStatusColors
import kotlinx.coroutines.delay
import kotlinx.coroutines.withTimeoutOrNull

private const val SESSION_RESTORE_TIMEOUT_MS = 8_000L

@Composable
fun SplashScreen(
    authRepository: FxaAuthRepository,
    onSignedIn: () -> Unit,
    onNeedsLogin: () -> Unit,
) {
    LaunchedEffect(Unit) {
        delay(500)
        val status = withTimeoutOrNull(SESSION_RESTORE_TIMEOUT_MS) {
            authRepository.restoreSession()
        } ?: SessionStatus.UNREACHABLE

        when (status) {
            SessionStatus.ACTIVE -> onSignedIn()
            SessionStatus.UNREACHABLE -> onSignedIn()
            SessionStatus.NEEDS_LOGIN -> onNeedsLogin()
        }
    }

    Box(
        Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
            .windowInsetsPadding(WindowInsets.safeDrawing),
        contentAlignment = Alignment.Center,
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Box(
                modifier = Modifier
                    .size(88.dp)
                    .clip(RoundedCornerShape(22.dp))
                    .background(LocalFoxyStatusColors.current.blue),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    imageVector = Icons.Filled.Shield,
                    contentDescription = null,
                    tint = Color.White,
                    modifier = Modifier.size(48.dp),
                )
            }
            Spacer(Modifier.height(16.dp))
            Text("FoxyVPN", style = MaterialTheme.typography.headlineMedium, color = MaterialTheme.colorScheme.onBackground)
            Spacer(Modifier.height(28.dp))
            CircularProgressIndicator(
                modifier = Modifier.size(24.dp),
                strokeWidth = 2.dp,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}
