package com.rigsight.app.ui.theme

import android.app.Activity
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import androidx.core.view.WindowCompat

object RigColors {
    val Ink = Color(0xFF0B0D10)
    val Background = Color(0xFF0E1013)
    val Surface = Color(0xFF171A1F)
    val SurfaceHigh = Color(0xFF1E232B)
    val SurfaceHigher = Color(0xFF262D37)
    val Outline = Color(0xFF2E3642)
    val Mint = Color(0xFF3DE8B0)
    val MintDark = Color(0xFF1B8F6C)
    val MintSoft = Color(0x1F3DE8B0)
    val Violet = Color(0xFFA78BFA)
    val VioletSoft = Color(0x1FA78BFA)
    val Text = Color(0xFFF1F5F7)
    val TextDim = Color(0xFF98A5B3)
    val TextFaint = Color(0xFF64717E)
    val Danger = Color(0xFFFF6B6B)
    val DangerSoft = Color(0x1FFF6B6B)
    val Amber = Color(0xFFFFC857)
    val AmberSoft = Color(0x1FFFC857)
}

private val Scheme = darkColorScheme(
    primary = RigColors.Mint,
    onPrimary = RigColors.Ink,
    primaryContainer = RigColors.MintSoft,
    onPrimaryContainer = RigColors.Mint,
    secondary = RigColors.Violet,
    onSecondary = RigColors.Ink,
    secondaryContainer = RigColors.VioletSoft,
    onSecondaryContainer = RigColors.Violet,
    background = RigColors.Background,
    onBackground = RigColors.Text,
    surface = RigColors.Surface,
    onSurface = RigColors.Text,
    surfaceVariant = RigColors.SurfaceHigh,
    onSurfaceVariant = RigColors.TextDim,
    outline = RigColors.Outline,
    outlineVariant = RigColors.Outline,
    error = RigColors.Danger,
    onError = RigColors.Ink,
    errorContainer = RigColors.DangerSoft,
    onErrorContainer = RigColors.Danger,
)

private val RigTypography = Typography(
    displayLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 64.sp,
        lineHeight = 66.sp,
        letterSpacing = (-2).sp,
    ),
    displayMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 44.sp,
        lineHeight = 46.sp,
        letterSpacing = (-1.2).sp,
    ),
    headlineLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 30.sp,
        lineHeight = 34.sp,
        letterSpacing = (-0.6).sp,
    ),
    headlineMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 24.sp,
        lineHeight = 28.sp,
        letterSpacing = (-0.4).sp,
    ),
    headlineSmall = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 20.sp,
        lineHeight = 24.sp,
    ),
    titleLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 18.sp,
        lineHeight = 22.sp,
    ),
    titleMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Medium,
        fontSize = 15.sp,
        lineHeight = 20.sp,
    ),
    bodyLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Normal,
        fontSize = 15.sp,
        lineHeight = 21.sp,
    ),
    bodyMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Normal,
        fontSize = 13.sp,
        lineHeight = 18.sp,
    ),
    labelLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 13.sp,
        lineHeight = 16.sp,
        letterSpacing = 0.2.sp,
    ),
    labelMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Medium,
        fontSize = 11.sp,
        lineHeight = 14.sp,
        letterSpacing = 0.8.sp,
    ),
    labelSmall = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Medium,
        fontSize = 10.sp,
        lineHeight = 12.sp,
        letterSpacing = 1.1.sp,
    ),
)

@Composable
fun RigSightTheme(content: @Composable () -> Unit) {
    val view = LocalView.current
    if (!view.isInEditMode) {
        SideEffect {
            val window = (view.context as? Activity)?.window ?: return@SideEffect
            WindowCompat.getInsetsController(window, view).apply {
                isAppearanceLightStatusBars = false
                isAppearanceLightNavigationBars = false
            }
        }
    }
    MaterialTheme(
        colorScheme = Scheme,
        typography = RigTypography,
        content = content,
    )
}
