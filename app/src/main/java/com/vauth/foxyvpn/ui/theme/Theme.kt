package com.vauth.foxyvpn.ui.theme

import android.app.Activity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialExpressiveTheme
import androidx.compose.material3.MotionScheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat

private val LightColors = lightColorScheme(
    primary = IosBlueLight,
    onPrimary = Color.White,
    primaryContainer = Color(0xFFD6E8FF),
    onPrimaryContainer = Color(0xFF003A80),
    secondary = IosGrayLight,
    onSecondary = Color.White,
    secondaryContainer = Color(0xFFE5E5EA),
    onSecondaryContainer = IosLabelLight,
    tertiary = IosGreenLight,
    onTertiary = Color.White,
    tertiaryContainer = Color(0xFFD9F5E0),
    onTertiaryContainer = Color(0xFF0B4A1E),
    error = IosRedLight,
    onError = Color.White,
    errorContainer = Color(0xFFFFE3E0),
    onErrorContainer = Color(0xFF8C1710),
    background = IosGroupedBackgroundLight,
    onBackground = IosLabelLight,
    surface = IosCardLight,
    onSurface = IosLabelLight,
    surfaceVariant = Color(0xFFE5E5EA),
    onSurfaceVariant = IosSecondaryLabelLight,
    surfaceTint = Color.Transparent,
    surfaceBright = IosCardLight,
    surfaceDim = Color(0xFFE5E5EA),
    surfaceContainerLowest = IosCardLight,
    surfaceContainerLow = IosCardLight,
    surfaceContainer = IosCardLight,
    surfaceContainerHigh = IosCardLight,
    surfaceContainerHighest = Color(0xFFE5E5EA),
    outline = Color(0xFFC6C6C8),
    outlineVariant = Color(0xFFD1D1D6),
    inverseSurface = Color(0xFF2C2C2E),
    inverseOnSurface = Color.White,
    inversePrimary = IosBlueDark,
    scrim = Color(0x66000000),
)

private val DarkColors = darkColorScheme(
    primary = IosBlueDark,
    onPrimary = Color.White,
    primaryContainer = Color(0xFF0A3A7A),
    onPrimaryContainer = Color(0xFFD6E8FF),
    secondary = IosGrayDark,
    onSecondary = Color.Black,
    secondaryContainer = IosCardElevatedDark,
    onSecondaryContainer = IosLabelDark,
    tertiary = IosGreenDark,
    onTertiary = Color.Black,
    tertiaryContainer = Color(0xFF0B3D1B),
    onTertiaryContainer = Color(0xFFD9F5E0),
    error = IosRedDark,
    onError = Color.Black,
    errorContainer = Color(0xFF5C1511),
    onErrorContainer = Color(0xFFFFDAD6),
    background = IosGroupedBackgroundDark,
    onBackground = IosLabelDark,
    surface = IosCardDark,
    onSurface = IosLabelDark,
    surfaceVariant = IosCardElevatedDark,
    onSurfaceVariant = IosSecondaryLabelDark,
    surfaceTint = Color.Transparent,
    surfaceBright = IosCardElevatedDark,
    surfaceDim = IosGroupedBackgroundDark,
    surfaceContainerLowest = IosGroupedBackgroundDark,
    surfaceContainerLow = IosCardDark,
    surfaceContainer = IosCardDark,
    surfaceContainerHigh = IosCardElevatedDark,
    surfaceContainerHighest = Color(0xFF3A3A3C),
    outline = Color(0xFF48484A),
    outlineVariant = Color(0xFF38383A),
    inverseSurface = Color(0xFFE5E5EA),
    inverseOnSurface = Color.Black,
    inversePrimary = IosBlueLight,
    scrim = Color(0x99000000),
)

@Composable
fun FoxyVpnTheme(
    themeMode: ThemeMode = ThemeMode.SYSTEM,
    @Suppress("UNUSED_PARAMETER") dynamicColor: Boolean = false,
    content: @Composable () -> Unit,
) {
    // ThemeMode.SYSTEM follows Android's appearance (including its day/night schedule);
    // LIGHT and DARK are the user's explicit override from Settings.
    val darkTheme = when (themeMode) {
        ThemeMode.SYSTEM -> isSystemInDarkTheme()
        ThemeMode.LIGHT -> false
        ThemeMode.DARK -> true
    }
    val view = LocalView.current
    if (!view.isInEditMode) {
        SideEffect {
            val window = (view.context as? Activity)?.window ?: return@SideEffect
            WindowCompat.getInsetsController(window, view).apply {
                isAppearanceLightStatusBars = !darkTheme
                isAppearanceLightNavigationBars = !darkTheme
            }
        }
    }
    CompositionLocalProvider(
        LocalFoxyStatusColors provides if (darkTheme) DarkStatusColors else LightStatusColors,
    ) {
        MaterialExpressiveTheme(
            colorScheme = if (darkTheme) DarkColors else LightColors,
            typography = FoxyTypography,
            motionScheme = MotionScheme.expressive(),
            content = content,
        )
    }
}
