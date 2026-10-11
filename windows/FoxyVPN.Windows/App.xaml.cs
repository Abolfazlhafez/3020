using System.IO;
using System.Windows;
using FoxyVPN.Windows.Services;

namespace FoxyVPN.Windows;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    private void Card_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is UIElement element) Motion.Lift(element, true);
    }

    private void Card_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is UIElement element) Motion.Lift(element, false);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Services = new AppServices();
        Services.Log.Info("Application", "FoxyVPN Windows started.");
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        ThemeManager.Apply(Services.Settings.Current.ThemeMode);
        var startMinimized = e.Args.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        if (startMinimized) window.WindowState = WindowState.Minimized;
        window.Show();

        // Recovery must complete before Connect on launch is allowed to touch the data plane.
        // Starting automatically while a prior route journal remains would make ownership ambiguous.
        _ = FinishStartupSequenceAsync(window);
    }

    private async Task FinishStartupSequenceAsync(Window owner)
    {
        var hasPendingRecovery = WindowsSystemRouteTransaction.HasPendingRecoveryJournal;
        if (hasPendingRecovery)
        {
            if (!WindowsSystemTunnelRuntime.IsCurrentProcessAdministrator())
            {
                MessageBox.Show(owner,
                    "A previous FoxyVPN system-tunnel session left a recovery journal. Some public network traffic may remain blocked by the safety guard. Restart FoxyVPN with Run as administrator to safely restore its owned routes and firewall rules. Automatic connection has been skipped.",
                    "FoxyVPN network recovery required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!await RecoverPreviousNetworkStateAsync(owner)) return;
        }
        await TryConnectOnLaunchAsync(owner);
    }

    private async Task<bool> RecoverPreviousNetworkStateAsync(Window owner)
    {
        try
        {
            await WindowsSystemRouteTransaction.RecoverStaleJournalAsync((level, message) =>
            {
                if (level.Equals("ERROR", StringComparison.OrdinalIgnoreCase)) Services.Log.Error("SystemTunnel", message);
                else if (level.Equals("WARN", StringComparison.OrdinalIgnoreCase)) Services.Log.Warn("SystemTunnel", message);
                else Services.Log.Info("SystemTunnel", message);
            });
            if (WindowsSystemRouteTransaction.HasPendingRecoveryJournal)
                throw new InvalidOperationException("The route recovery journal still exists after recovery returned.");
            Services.Log.Info("SystemTunnel", "Recovered a previous system-tunnel journal during startup.");
            return true;
        }
        catch (Exception ex)
        {
            Services.Log.Error("SystemTunnel", $"Previous route recovery failed; the journal and any persistent safety guard were retained ({ex.GetType().Name}).");
            MessageBox.Show(owner,
                "FoxyVPN could not safely finish restoring a previous VPN session. Its recovery journal and safety firewall rules have been retained. Do not delete the journal or disable Windows Firewall; review the diagnostic log and retry recovery from an elevated FoxyVPN launch.\n\nDetails: " + ex.Message,
                "FoxyVPN recovery incomplete", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async Task TryConnectOnLaunchAsync(Window owner)
    {
        var settings = Services.Settings.Current;
        if (!settings.ConnectOnLaunch) return;
        if (!Services.Auth.HasSession)
        {
            Services.Log.Info("Startup", "Connect on launch was skipped because no Firefox Account session is stored.");
            return;
        }
        if (string.IsNullOrWhiteSpace(settings.SelectedServerHost) || settings.SelectedServerPort is < 1 or > 65535)
        {
            Services.Log.Warn("Startup", "Connect on launch was skipped because no valid VPN endpoint is selected.");
            return;
        }
        if (!settings.ProxyOnlyMode && !WindowsSystemTunnelRuntime.IsCurrentProcessAdministrator())
        {
            Services.Log.Warn("Startup", "Connect on launch was skipped because full-system VPN requires an elevated Windows session.");
            if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
            MessageBox.Show(owner, "Connect on launch is enabled, but full-system VPN requires FoxyVPN to be run as administrator. No routes were changed. You can use Proxy-only mode without elevation.", "Elevation required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var handledChallenges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                try
                {
                    if (settings.ProxyOnlyMode)
                        await Services.StartProxyOnlySessionAsync();
                    else
                    {
                        await Services.StartSystemTunnelAsync();
                        if (!Services.Tunnel.IsConnected) throw new IOException("The runtime did not confirm an active system tunnel.");
                    }
                    Services.Log.Info("Startup", settings.ProxyOnlyMode
                        ? "Connect on launch started the local SOCKS5 proxy; Windows system routes remain unchanged."
                        : "Connect on launch completed all current runtime startup checks.");
                    return;
                }
                catch (GuardianChallengeRequiredException challenge) when (handledChallenges.Add("guardian"))
                {
                    PrepareOwnerForInteractiveChallenge(owner);
                    var result = Views.InteractiveChallengeDialogFlow.ShowAndImport(owner, challenge.ChallengeOrigin, Services.Guardian.ImportInteractiveChallengeCookies);
                    if (!result.UserCompleted || result.ImportedCookies == 0) throw;
                    Services.Log.Info("Startup", "Guardian interactive verification completed by the user; startup connection will retry once for this service.");
                }
                catch (FirefoxChallengeRequiredException challenge) when (handledChallenges.Add("firefox-accounts"))
                {
                    PrepareOwnerForInteractiveChallenge(owner);
                    var result = Views.InteractiveChallengeDialogFlow.ShowAndImport(owner, challenge.ChallengeOrigin, Services.Auth.ImportInteractiveChallengeCookies);
                    if (!result.UserCompleted || result.ImportedCookies == 0) throw;
                    Services.Log.Info("Startup", "Firefox Accounts interactive verification completed by the user; startup connection will retry once for this service.");
                }
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warn("Startup", $"Connect on launch did not complete ({ex.GetType().Name}); the app remains available for manual recovery.");
            if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
            MessageBox.Show(owner, "Connect on launch could not establish a verified connection. No Connected state is assumed. Open FoxyVPN and review the diagnostic log.\n\n" + ex.Message,
                "FoxyVPN startup connection", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void PrepareOwnerForInteractiveChallenge(Window owner)
    {
        // A challenge can arrive while --minimized auto-connect is running; the modal dialog must not be hidden behind a minimized owner.
        if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
        owner.ShowInTaskbar = true;
        owner.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is not null) Services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
