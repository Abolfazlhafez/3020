using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FoxyVPN.Windows.Services;

namespace FoxyVPN.Windows.Views;

public partial class HomeView : UserControl, IRefreshableView
{
    private readonly DispatcherTimer _runtimeUiTimer;
    private readonly DispatcherTimer _uptimeTimer;
    public event Action<string>? RequestNavigation;

    private enum OrbMode { Offline, Busy, Active, Fault }
    private OrbMode _orbMode = OrbMode.Offline;
    private bool _busy;

    public HomeView()
    {
        InitializeComponent();
        _runtimeUiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _runtimeUiTimer.Tick += (_, _) => UpdateRuntimeUi();
        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) => UpdateUptime();
    }

    public Task RefreshAsync()
    {
        var settings = App.Services.Settings.Current;
        SelectedLocationText.Text = settings.SelectedLocationDisplay;
        SelectedEndpointText.Text = string.IsNullOrWhiteSpace(settings.SelectedServerHost)
            ? "No endpoint selected" : $"{settings.SelectedServerHost}:{settings.SelectedServerPort}";
        AccountStateText.Text = App.Services.Auth.HasSession ? "Signed in" : "Not signed in";
        AccountButton.Content = App.Services.Auth.HasSession ? "Open account  →" : "Sign in  →";
        UpdateRuntimeUi();
        return Task.CompletedTask;
    }

    private void HomeView_Loaded(object sender, RoutedEventArgs e)
    {
        PlayEntrance();
        StartMotion();
        UpdateRuntimeUi();
        _runtimeUiTimer.Start();
        UpdateUptime();
        _uptimeTimer.Start();
    }

    private void HomeView_Unloaded(object sender, RoutedEventArgs e)
    {
        _runtimeUiTimer.Stop();
        _uptimeTimer.Stop();
        StopMotion();
    }

    private void UpdateUptime()
    {
        var uptime = App.Services.ConnectionUptime;
        if (uptime is null)
        {
            UptimeText.Visibility = Visibility.Collapsed;
            return;
        }
        var elapsed = uptime.Value;
        UptimeText.Text = $"Connected for {(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        UptimeText.Visibility = Visibility.Visible;
    }

    private bool ConfirmDisconnectIfRequested()
    {
        if (!App.Services.Settings.Current.ConfirmDisconnect) return true;
        return MessageBox.Show(Window.GetWindow(this), "Disconnect FoxyVPN now?", "FoxyVPN",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void PlayEntrance()
    {
        var targets = new List<UIElement>();
        foreach (UIElement child in ContentStack.Children)
        {
            if (child is Grid row)
            {
                foreach (UIElement card in row.Children) targets.Add(card);
            }
            else targets.Add(child);
        }
        for (var i = 0; i < targets.Count; i++) Motion.Reveal(targets[i], i);
    }

    private (double PulseOne, double PulseTwo, double Rotor, double GlowLow, double GlowHigh, double GlowSeconds) Timings() => _orbMode switch
    {
        OrbMode.Busy => (1.1, 1.3, 3.5, 0.08, 0.30, 1.2),
        OrbMode.Active => (2.4, 2.7, 14, 0.08, 0.25, 3.0),
        OrbMode.Fault => (1.8, 2.0, 40, 0.06, 0.20, 2.0),
        _ => (3.2, 3.6, 28, 0.04, 0.16, 4.2)
    };

    private void StartMotion()
    {
        StopMotion();
        if (!Motion.Enabled) return;
        var t = Timings();
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (PulseRingOne.RenderTransform is ScaleTransform scaleOne)
        {
            scaleOne.ScaleX = scaleOne.ScaleY = 0.82;
            var pulseScaleOne = new DoubleAnimation(0.82, 1.24, TimeSpan.FromSeconds(t.PulseOne))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = easeOut
            };
            var pulseOpacityOne = new DoubleAnimation(0.5, 0.02, TimeSpan.FromSeconds(t.PulseOne)) { RepeatBehavior = RepeatBehavior.Forever };
            scaleOne.BeginAnimation(ScaleTransform.ScaleXProperty, pulseScaleOne);
            scaleOne.BeginAnimation(ScaleTransform.ScaleYProperty, pulseScaleOne);
            PulseRingOne.BeginAnimation(OpacityProperty, pulseOpacityOne);
        }

        if (PulseRingTwo.RenderTransform is ScaleTransform scaleTwo)
        {
            scaleTwo.ScaleX = scaleTwo.ScaleY = 0.8;
            var offset = TimeSpan.FromSeconds(t.PulseTwo * 0.4);
            var pulseScaleTwo = new DoubleAnimation(0.8, 1.17, TimeSpan.FromSeconds(t.PulseTwo))
            {
                BeginTime = offset,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = easeOut
            };
            var pulseOpacityTwo = new DoubleAnimation(0.4, 0.015, TimeSpan.FromSeconds(t.PulseTwo)) { BeginTime = offset, RepeatBehavior = RepeatBehavior.Forever };
            scaleTwo.BeginAnimation(ScaleTransform.ScaleXProperty, pulseScaleTwo);
            scaleTwo.BeginAnimation(ScaleTransform.ScaleYProperty, pulseScaleTwo);
            PulseRingTwo.BeginAnimation(OpacityProperty, pulseOpacityTwo);
        }

        if (OrbitRotor.RenderTransform is RotateTransform rotation)
        {
            rotation.Angle = 0;
            rotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(t.Rotor)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        AmbientGlow.BeginAnimation(OpacityProperty,
            new DoubleAnimation(t.GlowLow, t.GlowHigh, TimeSpan.FromSeconds(t.GlowSeconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
    }

    private void ApplyOrbMode(OrbMode mode)
    {
        if (mode == _orbMode) return;
        _orbMode = mode;
        switch (mode)
        {
            case OrbMode.Active: TintOrb(Color.FromRgb(0x34, 0xC7, 0x59)); break;
            case OrbMode.Fault: TintOrb(Color.FromRgb(0xFF, 0x9F, 0x0A)); break;
            default: ClearOrbTint(); break;
        }
        if (IsLoaded) StartMotion();
    }

    private void TintOrb(Color color)
    {
        var brush = new SolidColorBrush(color);
        PulseRingOne.Stroke = brush;
        AmbientGlow.Fill = brush;
    }

    private void ClearOrbTint()
    {
        PulseRingOne.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
        AmbientGlow.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
    }

    private void SetBadge(string text)
    {
        if (RuntimeBadgeText.Text == text) return;
        RuntimeBadgeText.Text = text;
        Motion.Flash(RuntimeBadgeText);
    }

    private void StopMotion()
    {
        foreach (var element in new UIElement[] { PulseRingOne, PulseRingTwo, AmbientGlow })
            element.BeginAnimation(OpacityProperty, null);
        if (PulseRingOne.RenderTransform is System.Windows.Media.ScaleTransform scaleOne)
        {
            scaleOne.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            scaleOne.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            scaleOne.ScaleX = scaleOne.ScaleY = 1;
        }
        if (PulseRingTwo.RenderTransform is System.Windows.Media.ScaleTransform scaleTwo)
        {
            scaleTwo.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            scaleTwo.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            scaleTwo.ScaleX = scaleTwo.ScaleY = 1;
        }
        if (OrbitRotor.RenderTransform is System.Windows.Media.RotateTransform rotation)
        {
            rotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
            rotation.Angle = 0;
        }
    }

    private void UpdateRuntimeUi()
    {
        if (_busy)
        {
            // Keep the progress text set by the connect flow; the 2-second refresh must not overwrite it.
            ApplyOrbMode(OrbMode.Busy);
            return;
        }
        ApplyOrbMode(
            App.Services.IsProxyOnlySessionActive || App.Services.Tunnel.IsConnected ? OrbMode.Active
            : App.Services.Tunnel.State == WindowsTunnelState.Faulted ? OrbMode.Fault
            : OrbMode.Offline);
        if (App.Services.IsProxyOnlySessionActive)
        {
            SetBadge("LOCAL PROXY ACTIVE");
            ConnectButton.Content = "Stop local SOCKS5 proxy";
            PlatformRuntimeText.Text = $"Proxy-only active · 127.0.0.1:{App.Services.ProxyOnlyPort}";
        }
        else if (App.Services.Settings.Current.ProxyOnlyMode)
        {
            SetBadge("PROXY-ONLY MODE READY");
            ConnectButton.Content = "Start local SOCKS5 proxy";
            PlatformRuntimeText.Text = "Local proxy ready · system routes unchanged";
        }
        else if (App.Services.Tunnel.IsConnected)
        {
            SetBadge("SYSTEM TUNNEL ACTIVE");
            ConnectButton.Content = "Disconnect system VPN";
            PlatformRuntimeText.Text = "Wintun + HEV + HTTP/2 active · general UDP is restricted by the current edge protocol";
        }
        else if (App.Services.Tunnel.State == WindowsTunnelState.Faulted)
        {
            SetBadge("VPN CLEANUP / RETRY REQUIRED");
            ConnectButton.Content = "Retry safe disconnect";
            PlatformRuntimeText.Text = App.Services.Tunnel.IsSupported
                ? "A prior start/stop failed; retry cleanup before reconnecting"
                : "Native runtime unavailable or recovery is incomplete";
        }
        else
        {
            SetBadge("SYSTEM VPN OFFLINE");
            ConnectButton.Content = "Connect system VPN   ↗";
            PlatformRuntimeText.Text = App.Services.Tunnel.IsSupported
                ? "Native runtime available · awaiting connection"
                : "Requires elevated Windows session and bundled HEV/Wintun runtime";
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        _busy = true;
        ApplyOrbMode(OrbMode.Busy);
        try
        {
            if (App.Services.IsProxyOnlySessionActive)
            {
                if (!ConfirmDisconnectIfRequested()) return;
                await App.Services.StopProxyOnlySessionAsync();
                App.Services.Log.Info("VPNTransport", "User stopped proxy-only mode.");
                MessageBox.Show(Window.GetWindow(this), "The local SOCKS5 proxy has stopped. No Windows system routes were changed.", "FoxyVPN", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!App.Services.Settings.Current.ProxyOnlyMode &&
                (App.Services.Tunnel.IsConnected || App.Services.Tunnel.State == WindowsTunnelState.Faulted))
            {
                // Cleanup after a failed session must never be blocked by a confirmation prompt.
                if (App.Services.Tunnel.IsConnected && !ConfirmDisconnectIfRequested()) return;
                ConnectButton.Content = "Restoring routes and DNS…";
                await App.Services.StopSystemTunnelAsync();
                App.Services.Log.Info("Tunnel", "User requested system VPN disconnect; owned routes and DNS policy were restored.");
                MessageBox.Show(Window.GetWindow(this),
                    "FoxyVPN has disconnected. The DNS policy and routes owned by the tunnel were removed.",
                    "FoxyVPN", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (App.Services.Settings.Current.ProxyOnlyMode)
            {
                if (!App.Services.Auth.HasSession)
                    throw new UnauthorizedAccessException("Sign in to your Firefox Account before starting proxy-only mode.");
                ConnectButton.Content = "Opening TLS / HTTP/2…";
                try { await App.Services.StartProxyOnlySessionAsync(); }
                catch (GuardianChallengeRequiredException challenge)
                {
                    if (!TryCompleteGuardianChallenge(challenge)) throw;
                    await App.Services.StartProxyOnlySessionAsync(); // one user-mediated retry only
                }
                catch (FirefoxChallengeRequiredException challenge)
                {
                    if (!TryCompleteFirefoxChallenge(challenge)) throw;
                    await App.Services.StartProxyOnlySessionAsync(); // one user-mediated retry only
                }
                MessageBox.Show(Window.GetWindow(this),
                    $"The local SOCKS5 proxy is active at 127.0.0.1:{App.Services.ProxyOnlyPort}.\n\nConfigure an application explicitly to use SOCKS5 at this address. Other applications and Windows system traffic are NOT redirected automatically. DNS-only UDP relay is not yet live-verified; other UDP (including QUIC) is dropped. Full-system VPN has not passed final Windows acceptance tests.",
                    "FoxyVPN · Proxy-only mode", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ConnectButton.Content = "Checking runtime…";
            var report = App.Services.TunnelReadiness.Evaluate(
                App.Services.Settings.Current,
                App.Services.Auth.HasSession,
                AppContext.BaseDirectory);

            foreach (var check in report.Checks)
            {
                var detail = $"{check.Name}: {check.Detail}";
                if (check.Passed) App.Services.Log.Info("TunnelPreflight", detail);
                else App.Services.Log.Warn("TunnelPreflight", detail);
            }

            if (!report.CanConnect)
            {
                MessageBox.Show(Window.GetWindow(this), report.ToDisplayText(), "VPN runtime readiness", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // A completed preflight is necessary but not sufficient. ConnectAsync still demands
            // an installed runtime that confirms a real data plane before reporting Connected.
            try { await App.Services.StartSystemTunnelAsync(); }
            catch (GuardianChallengeRequiredException challenge)
            {
                if (!TryCompleteGuardianChallenge(challenge)) throw;
                await App.Services.StartSystemTunnelAsync(); // one user-mediated retry only
            }
            catch (FirefoxChallengeRequiredException challenge)
            {
                if (!TryCompleteFirefoxChallenge(challenge)) throw;
                await App.Services.StartSystemTunnelAsync(); // one user-mediated retry only
            }
            if (!App.Services.Tunnel.IsConnected)
                throw new IOException("The system tunnel runtime did not confirm every required layer active.");
            MessageBox.Show(Window.GetWindow(this),
                "The system tunnel is active. TCP traffic and DNS use the authenticated tunnel; the current edge protocol does not carry general UDP/QUIC, so those datagrams are dropped rather than sent directly. Run the final Windows forwarding and leak tests before relying on this build for sensitive traffic.",
                "FoxyVPN · System tunnel", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (WindowsRouteTransactionException ex) when (ex.RollbackIncomplete)
        {
            App.Services.Log.Error("Tunnel", "Route rollback is incomplete; the data plane is being kept alive for safety.");
            MessageBox.Show(Window.GetWindow(this),
                ex.Message + "\n\nDo not close this app if traffic appears routed through FoxyVPN. Retry safe disconnect after the network stack is ready; the next elevated launch can also recover the journal.",
                "FoxyVPN · Cleanup required", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (GuardianChallengeRequiredException ex)
        {
            App.Services.Log.Warn("Guardian", "The Guardian challenge was not accepted after one user-mediated retry.");
            MessageBox.Show(Window.GetWindow(this), ex.Message + " Complete the verification and retry the connection. FoxyVPN did not bypass the challenge.", "Guardian verification required", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (FirefoxChallengeRequiredException ex)
        {
            App.Services.Log.Warn("FirefoxAuth", "The Firefox Accounts challenge was not accepted after one user-mediated retry.");
            MessageBox.Show(Window.GetWindow(this), ex.Message + " Complete the verification and retry the connection. FoxyVPN did not bypass the challenge.", "Firefox Accounts verification required", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (TunnelEngineUnavailableException ex)
        {
            App.Services.Log.Warn("Tunnel", $"Connection request was rejected: {ex.Message}");
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Windows VPN engine unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            App.Services.Log.Warn("Tunnel", "Tunnel startup was cancelled.");
        }
        catch (Exception ex)
        {
            App.Services.Log.Error("Tunnel", ex.Message);
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Could not start VPN", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            UpdateRuntimeUi();
            ConnectButton.IsEnabled = true;
        }
    }

    private bool TryCompleteGuardianChallenge(GuardianChallengeRequiredException challenge)
    {
        var result = InteractiveChallengeDialogFlow.ShowAndImport(
            Window.GetWindow(this), challenge.ChallengeOrigin,
            App.Services.Guardian.ImportInteractiveChallengeCookies);
        if (!result.UserCompleted)
        {
            App.Services.Log.Info("Guardian", "User canceled the interactive Guardian challenge; no retry was made.");
            return false;
        }
        if (result.ImportedCookies == 0)
        {
            App.Services.Log.Warn("Guardian", "The interactive challenge returned no eligible service cookies; no retry was made.");
            return false;
        }
        App.Services.Log.Info("Guardian", "User-mediated challenge state imported for the approved Guardian origin; the original operation will retry once.");
        return true;
    }

    private bool TryCompleteFirefoxChallenge(FirefoxChallengeRequiredException challenge)
    {
        var result = InteractiveChallengeDialogFlow.ShowAndImport(
            Window.GetWindow(this), challenge.ChallengeOrigin,
            App.Services.Auth.ImportInteractiveChallengeCookies);
        if (!result.UserCompleted)
        {
            App.Services.Log.Info("FirefoxAuth", "User canceled the Firefox Accounts challenge; no retry was made.");
            return false;
        }
        if (result.ImportedCookies == 0)
        {
            App.Services.Log.Warn("FirefoxAuth", "The interactive Firefox Accounts challenge returned no eligible cookies; no retry was made.");
            return false;
        }
        App.Services.Log.Info("FirefoxAuth", "User-mediated challenge state imported for the approved Firefox Accounts origin; original operation will retry once.");
        return true;
    }

    private void ChooseLocation_Click(object sender, RoutedEventArgs e) => RequestNavigation?.Invoke("Locations");
    private void AccountButton_Click(object sender, RoutedEventArgs e) => RequestNavigation?.Invoke(App.Services.Auth.HasSession ? "Account usage" : "Firefox account");
}
