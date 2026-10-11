using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace FoxyVPN.Windows.Services;

public static class ThemeManager
{
    public static void Apply(string mode)
    {
        var dark = mode switch
        {
            "Dark" => true,
            "System" => IsWindowsDarkMode(),
            _ => false
        };
        Set("WindowBackgroundBrush", dark ? "#0B0B0D" : "#F2F2F7");
        Set("CardBrush", dark ? "#1C1C1E" : "#FFFFFF");
        Set("PrimaryTextBrush", dark ? "#F5F5F7" : "#1C1C1E");
        Set("SecondaryTextBrush", dark ? "#98989F" : "#8A8A8E");
        Set("BorderBrushSoft", dark ? "#2C2C2E" : "#E5E5EA");
        Set("AccentBrush", dark ? "#0A84FF" : "#007AFF");
        Set("SelectedNavBrush", dark ? "#16273F" : "#E3EFFF");
        Set("ButtonHoverBrush", dark ? "#2C2C2E" : "#EDEDF2");
        Set("SubtleAccentBrush", dark ? "#14233A" : "#EEF5FF");
        Set("AccentCyanBrush", dark ? "#40C8E0" : "#30B0C7");
        Set("AccentVioletBrush", dark ? "#BF5AF2" : "#AF52DE");
        Set("AccentGlowBrush", dark ? "#0A84FF" : "#0A84FF");
        if (Application.Current?.MainWindow is FoxyVPN.Windows.MainWindow mainWindow) mainWindow.RefreshNavigationTheme();
    }

    private static bool IsWindowsDarkMode()
    {
        try
        {
            var value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return value is int i && i == 0;
        }
        catch { return false; }
    }

    private static void Set(string key, string color)
    {
        if (Application.Current is not { } app) return;
        app.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
}
