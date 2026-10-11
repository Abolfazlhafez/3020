package com.vauth.foxyvpn.ui.theme

import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color

// Flat, high-contrast palette modelled on iOS system colors:
// grouped background + white / elevated-dark cards, one blue accent, semantic green / orange / red.

// ---- Light ----
val IosBlueLight = Color(0xFF007AFF)
val IosGreenLight = Color(0xFF34C759)
val IosOrangeLight = Color(0xFFFF9500)
val IosRedLight = Color(0xFFFF3B30)
val IosIndigoLight = Color(0xFF5856D6)
val IosTealLight = Color(0xFF30B0C7)
val IosGrayLight = Color(0xFF8E8E93)

val IosGroupedBackgroundLight = Color(0xFFF2F2F7)
val IosCardLight = Color(0xFFFFFFFF)
val IosFillLight = Color(0x1F787880)
val IosSeparatorLight = Color(0x4A3C3C43)
val IosLabelLight = Color(0xFF000000)
val IosSecondaryLabelLight = Color(0xFF6C6C70)

// ---- Dark ----
val IosBlueDark = Color(0xFF0A84FF)
val IosGreenDark = Color(0xFF30D158)
val IosOrangeDark = Color(0xFFFF9F0A)
val IosRedDark = Color(0xFFFF453A)
val IosIndigoDark = Color(0xFF5E5CE6)
val IosTealDark = Color(0xFF40C8E0)
val IosGrayDark = Color(0xFF8E8E93)

val IosGroupedBackgroundDark = Color(0xFF000000)
val IosCardDark = Color(0xFF1C1C1E)
val IosCardElevatedDark = Color(0xFF2C2C2E)
val IosFillDark = Color(0x5C787880)
val IosSeparatorDark = Color(0x99545458)
val IosLabelDark = Color(0xFFFFFFFF)
val IosSecondaryLabelDark = Color(0xFF9A9AA0)

/** Semantic colors that are not part of the Material scheme. */
data class FoxyStatusColors(
    val connected: Color,
    val connecting: Color,
    val disconnected: Color,
    val blue: Color,
    val indigo: Color,
    val teal: Color,
    val gray: Color,
    val separator: Color,
    val fill: Color,
)

val LightStatusColors = FoxyStatusColors(
    connected = IosGreenLight,
    connecting = IosOrangeLight,
    disconnected = IosGrayLight,
    blue = IosBlueLight,
    indigo = IosIndigoLight,
    teal = IosTealLight,
    gray = IosGrayLight,
    separator = IosSeparatorLight,
    fill = IosFillLight,
)

val DarkStatusColors = FoxyStatusColors(
    connected = IosGreenDark,
    connecting = IosOrangeDark,
    disconnected = IosGrayDark,
    blue = IosBlueDark,
    indigo = IosIndigoDark,
    teal = IosTealDark,
    gray = IosGrayDark,
    separator = IosSeparatorDark,
    fill = IosFillDark,
)

val LocalFoxyStatusColors = staticCompositionLocalOf { LightStatusColors }
