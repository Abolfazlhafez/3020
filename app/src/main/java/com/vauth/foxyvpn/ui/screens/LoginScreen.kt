package com.vauth.foxyvpn.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Shield
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusDirection
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.vauth.foxyvpn.data.FxaAuthRepository
import com.vauth.foxyvpn.ui.theme.IosButton
import com.vauth.foxyvpn.ui.theme.IosTextField
import com.vauth.foxyvpn.ui.theme.LocalFoxyStatusColors
import kotlinx.coroutines.launch

@Composable
fun LoginScreen(
    authRepository: FxaAuthRepository,
    onSignedIn: () -> Unit,
) {
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var code by remember { mutableStateOf("") }
    var passwordVisible by remember { mutableStateOf(false) }
    var awaitingTwoFactor by remember { mutableStateOf(false) }
    var isLoading by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()
    val focusManager = LocalFocusManager.current
    val status = LocalFoxyStatusColors.current

    fun submitCredentials() {
        if (isLoading || email.isBlank() || password.isBlank()) return
        errorMessage = null
        isLoading = true
        scope.launch {
            val result = authRepository.startLogin(email.trim(), password)
            isLoading = false
            result.onSuccess { needsVerification ->
                if (needsVerification) awaitingTwoFactor = true else onSignedIn()
            }.onFailure { errorMessage = it.message ?: "Sign-in failed" }
        }
    }

    fun submitCode() {
        if (isLoading || code.isBlank()) return
        errorMessage = null
        isLoading = true
        scope.launch {
            val result = authRepository.submitTwoFactorCode(code.trim())
            isLoading = false
            result.onSuccess { onSignedIn() }
                .onFailure { errorMessage = it.message ?: "Verification failed" }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
            .windowInsetsPadding(WindowInsets.safeDrawing)
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 24.dp, vertical = 24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(
            modifier = Modifier.size(72.dp).clip(RoundedCornerShape(18.dp)).background(status.blue),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                imageVector = Icons.Filled.Shield,
                contentDescription = null,
                tint = Color.White,
                modifier = Modifier.size(40.dp),
            )
        }
        Spacer(Modifier.height(20.dp))
        Text(
            text = if (awaitingTwoFactor) "Check your email" else "Sign in",
            style = MaterialTheme.typography.headlineMedium,
            color = MaterialTheme.colorScheme.onBackground,
            textAlign = TextAlign.Center,
        )
        Spacer(Modifier.height(8.dp))
        Text(
            text = if (awaitingTwoFactor) {
                "Enter the confirmation code Mozilla sent to $email."
            } else {
                "Use your Firefox account. FoxyVPN runs on the free monthly VPN allowance that comes with it — no subscription needed."
            },
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
        Spacer(Modifier.height(28.dp))

        if (!awaitingTwoFactor) {
            IosTextField(
                value = email,
                onValueChange = { email = it },
                placeholder = "Email",
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email, imeAction = ImeAction.Next),
                keyboardActions = KeyboardActions(onNext = { focusManager.moveFocus(FocusDirection.Down) }),
            )
            Spacer(Modifier.height(12.dp))
            IosTextField(
                value = password,
                onValueChange = { password = it },
                placeholder = "Password",
                visualTransformation = if (passwordVisible) VisualTransformation.None else PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(onDone = {
                    focusManager.clearFocus()
                    submitCredentials()
                }),
                trailingIcon = {
                    Icon(
                        imageVector = if (passwordVisible) Icons.Filled.VisibilityOff else Icons.Filled.Visibility,
                        contentDescription = if (passwordVisible) "Hide password" else "Show password",
                        modifier = Modifier
                            .clip(RoundedCornerShape(20.dp))
                            .clickable(role = Role.Button) { passwordVisible = !passwordVisible }
                            .padding(8.dp),
                    )
                },
            )
            Spacer(Modifier.height(20.dp))
            IosButton(
                text = "Continue",
                onClick = { submitCredentials() },
                enabled = email.isNotBlank() && password.isNotBlank(),
                loading = isLoading,
            )
        } else {
            IosTextField(
                value = code,
                onValueChange = { code = it },
                placeholder = "Confirmation code",
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(onDone = {
                    focusManager.clearFocus()
                    submitCode()
                }),
            )
            Spacer(Modifier.height(20.dp))
            IosButton(
                text = "Verify",
                onClick = { submitCode() },
                enabled = code.isNotBlank(),
                loading = isLoading,
            )
            Spacer(Modifier.height(8.dp))
            Text(
                text = "Use a different account",
                style = MaterialTheme.typography.bodyLarge,
                color = MaterialTheme.colorScheme.primary,
                modifier = Modifier
                    .clip(RoundedCornerShape(10.dp))
                    .clickable(role = Role.Button, enabled = !isLoading) {
                        awaitingTwoFactor = false
                        code = ""
                        errorMessage = null
                    }
                    .padding(horizontal = 16.dp, vertical = 12.dp),
            )
        }

        errorMessage?.let {
            Spacer(Modifier.height(12.dp))
            Text(
                text = it,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.error,
                textAlign = TextAlign.Center,
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }
}
