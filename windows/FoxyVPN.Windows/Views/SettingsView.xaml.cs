using System.Net;
using System.Windows;
using System.Windows.Controls;
using FoxyVPN.Windows.Services;

namespace FoxyVPN.Windows.Views;

public partial class SettingsView : UserControl, IRefreshableView
{
    private bool _initialized;

    public SettingsView()
    {
        InitializeComponent();
        StartWithWindowsCheck.Checked += (_, _) => StartMinimizedCheck.IsEnabled = true;
        StartWithWindowsCheck.Unchecked += (_, _) => { StartMinimizedCheck.IsEnabled = false; StartMinimizedCheck.IsChecked = false; };
        AutoRefreshServersCheck.Checked += (_, _) => RefreshIntervalCombo.IsEnabled = true;
        AutoRefreshServersCheck.Unchecked += (_, _) => RefreshIntervalCombo.IsEnabled = false;
        LoadFromSettings();
        _initialized = true;
    }

    public Task RefreshAsync()
    {
        if (_initialized) LoadFromSettings();
        return Task.CompletedTask;
    }

    private void LoadFromSettings()
    {
        var s = App.Services.Settings.Current;
        SelectCombo(ThemeCombo, s.ThemeMode);
        ReduceMotionCheck.IsChecked = s.ReduceMotion;
        StartWithWindowsCheck.IsChecked = WindowsStartupService.IsEnabled();
        StartMinimizedCheck.IsChecked = s.StartMinimized;
        StartMinimizedCheck.IsEnabled = StartWithWindowsCheck.IsChecked == true;
        AutoRefreshServersCheck.IsChecked = s.AutoRefreshServers;
        SelectCombo(RefreshIntervalCombo, $"{s.ServerRefreshIntervalMinutes} minutes");
        RefreshIntervalCombo.IsEnabled = s.AutoRefreshServers;
        ConnectOnLaunchCheck.IsChecked = s.ConnectOnLaunch;
        ConfirmDisconnectCheck.IsChecked = s.ConfirmDisconnect;
        ExitCheckCheck.IsChecked = s.ExitCheckEnabled;
        ProxyOnlyModeCheck.IsChecked = s.ProxyOnlyMode;
        SelectCombo(SocksBindCombo, "127.0.0.1 — This PC only");
        SocksPortText.Text = s.SocksPort.ToString();
        CustomDnsEnabledCheck.IsChecked = s.CustomDnsEnabled;
        SelectCombo(DohCombo, s.DohProvider);
        CustomDnsText.Text = s.CustomDnsServer;
        CustomEdgeText.Text = s.CustomEdgeAddress;
        UpstreamEnabledCheck.IsChecked = s.UpstreamProxyEnabled;
        SelectCombo(UpstreamTypeCombo, s.UpstreamProxyType);
        UpstreamHostText.Text = s.UpstreamProxyHost;
        UpstreamPortText.Text = s.UpstreamProxyPort.ToString();
        UpstreamUsernameText.Text = s.UpstreamProxyUsername;
        UpstreamPasswordInput.Password = s.UpstreamProxyPassword;
    }

    private static void SelectCombo(ComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            var content = item.Content?.ToString() ?? "";
            if (string.Equals(content, value, StringComparison.OrdinalIgnoreCase) ||
                content.StartsWith(value + " —", StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SocksPortText.Text, out var socksPort) || socksPort is < 1 or > 65535)
        {
            StatusText.Text = "SOCKS port must be a number between 1 and 65535.";
            SocksPortText.Focus();
            return;
        }
        if (!int.TryParse(UpstreamPortText.Text, out var upstreamPort) || upstreamPort is < 1 or > 65535)
        {
            StatusText.Text = "Upstream proxy port must be a number between 1 and 65535.";
            UpstreamPortText.Focus();
            return;
        }
        if (CustomDnsEnabledCheck.IsChecked == true && !IPAddress.TryParse(CustomDnsText.Text.Trim(), out _))
        {
            StatusText.Text = "Enter a valid numeric IP address for custom DNS.";
            CustomDnsText.Focus();
            return;
        }
        if (!string.IsNullOrWhiteSpace(CustomEdgeText.Text) && !SettingsStore.IsIpAddress(CustomEdgeText.Text) && !SettingsStore.IsValidHostname(CustomEdgeText.Text))
        {
            StatusText.Text = "Custom edge must be a valid hostname or IP address.";
            CustomEdgeText.Focus();
            return;
        }
        if (UpstreamEnabledCheck.IsChecked == true &&
            (string.IsNullOrWhiteSpace(UpstreamHostText.Text) ||
             (!SettingsStore.IsIpAddress(UpstreamHostText.Text) && !SettingsStore.IsValidHostname(UpstreamHostText.Text))))
        {
            StatusText.Text = "Enter a valid upstream proxy hostname/IP or turn off upstream proxy.";
            UpstreamHostText.Focus();
            return;
        }

        var requestedProxyOnlyMode = ProxyOnlyModeCheck.IsChecked == true;
        var currentSettings = App.Services.Settings.Current;
        if (requestedProxyOnlyMode != currentSettings.ProxyOnlyMode &&
            (App.Services.IsProxyOnlySessionActive || App.Services.Tunnel.State != WindowsTunnelState.Disconnected))
        {
            StatusText.Text = "Disconnect the current VPN session before switching Proxy-only mode. This avoids running the proxy and system tunnel at the same time.";
            return;
        }

        StatusText.Text = string.Empty;
        try
        {
            var startWithWindows = StartWithWindowsCheck.IsChecked == true;
            var startMinimized = StartMinimizedCheck.IsChecked == true;
            var refreshInterval = int.TryParse(ComboValue(RefreshIntervalCombo, "15 minutes").Split(' ')[0], out var minutes) ? minutes : 15;
            App.Services.Settings.Update(s =>
            {
                s.ThemeMode = ComboValue(ThemeCombo, "System");
                s.ReduceMotion = ReduceMotionCheck.IsChecked == true;
                s.StartWithWindows = startWithWindows;
                s.StartMinimized = startMinimized;
                s.AutoRefreshServers = AutoRefreshServersCheck.IsChecked == true;
                s.ServerRefreshIntervalMinutes = refreshInterval;
                s.ConnectOnLaunch = ConnectOnLaunchCheck.IsChecked == true;
                s.ConfirmDisconnect = ConfirmDisconnectCheck.IsChecked == true;
                s.ExitCheckEnabled = ExitCheckCheck.IsChecked == true;
                s.ProxyOnlyMode = ProxyOnlyModeCheck.IsChecked == true;
                s.SocksBindAddress = "127.0.0.1";
                s.SocksPort = socksPort;
                s.CustomDnsEnabled = CustomDnsEnabledCheck.IsChecked == true;
                s.DohProvider = ComboValue(DohCombo, "Automatic");
                s.CustomDnsServer = CustomDnsText.Text.Trim();
                s.CustomEdgeAddress = CustomEdgeText.Text.Trim();
                s.UpstreamProxyEnabled = UpstreamEnabledCheck.IsChecked == true;
                s.UpstreamProxyType = ComboValue(UpstreamTypeCombo, "SOCKS5");
                s.UpstreamProxyHost = UpstreamHostText.Text.Trim();
                s.UpstreamProxyPort = upstreamPort;
                s.UpstreamProxyUsername = UpstreamUsernameText.Text;
                s.UpstreamProxyPassword = UpstreamPasswordInput.Password;
            });
            try
            {
                WindowsStartupService.Apply(startWithWindows, startMinimized);
            }
            catch (Exception startupEx)
            {
                StatusText.Text = $"Settings saved, but Windows auto-start could not be updated: {startupEx.Message}";
                App.Services.Log.Error("Startup", $"Unable to apply auto-start settings: {startupEx.Message}");
            }
            ThemeManager.Apply(ComboValue(ThemeCombo, "System"));
            App.Services.Log.Info("Settings", "Application settings updated and stored with Windows DPAPI. Motion preferences take effect on the next page entry.");
            if (string.IsNullOrWhiteSpace(StatusText.Text) || !StatusText.Text.StartsWith("Settings saved, but Windows auto-start", StringComparison.Ordinal))
                StatusText.Text = "Settings saved. Auto-start, server refresh and motion preferences are stored. VPN transport-dependent settings remain inactive until the Windows tunnel engine is ported.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save settings: {ex.Message}";
            App.Services.Log.Error("Settings", $"Failed to save settings: {ex.Message}");
        }
    }

    private static string ComboValue(ComboBox combo, string fallback) => (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;
}
