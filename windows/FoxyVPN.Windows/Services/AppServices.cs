namespace FoxyVPN.Windows.Services;

public sealed class AppServices : IDisposable, IAsyncDisposable
{
    private int _disposeStarted;
    private WindowsProxyTunnelSession? _proxyOnlySession;
    public SettingsStore Settings { get; } = new();
    public AppLog Log { get; } = new();
    public ServerCatalogService Servers { get; }
    public FirefoxAuthService Auth { get; }
    public GuardianClient Guardian { get; }
    public GuardianProxyPassProvider ProxyPasses { get; }
    public WindowsTunnelEngine Tunnel { get; }
    public TunnelReadinessService TunnelReadiness { get; }
    private readonly WindowsHevNativeCore _hevCore;
    public bool IsProxyOnlySessionActive => _proxyOnlySession?.IsConnected == true;
    public int? ProxyOnlyPort => _proxyOnlySession?.LocalProxyPort;

    // Environment.TickCount64 value when the current connection was first seen active; 0 = not connected.
    private long _connectionStartedAt;

    /// <summary>How long the current proxy-only or system connection has been active; null when offline.</summary>
    public TimeSpan? ConnectionUptime
    {
        get
        {
            if (!IsProxyOnlySessionActive && !Tunnel.IsConnected)
            {
                Interlocked.Exchange(ref _connectionStartedAt, 0);
                return null;
            }
            var now = Math.Max(1L, Environment.TickCount64);
            Interlocked.CompareExchange(ref _connectionStartedAt, now, 0);
            return TimeSpan.FromMilliseconds(Math.Max(0L, now - Interlocked.Read(ref _connectionStartedAt)));
        }
    }

    public AppServices()
    {
        Servers = new ServerCatalogService(Log);
        Auth = new FirefoxAuthService(Log);
        Guardian = new GuardianClient(Log);
        ProxyPasses = new GuardianProxyPassProvider(Guardian, Auth, Log);
        _hevCore = new WindowsHevNativeCore(logger: (level, message) => WriteTunnelLog(level, message));
        var systemRuntime = new WindowsSystemTunnelRuntime(
            Settings,
            Auth,
            ProxyPasses,
            _hevCore,
            (level, message) => WriteTunnelLog(level, message));
        Tunnel = new WindowsTunnelEngine(systemRuntime);
        TunnelReadiness = new TunnelReadinessService(
            http2ConnectImplemented: true,
            socksRelayIntegrated: true,
            routeAndDnsLifecycleImplemented: true);
    }

    public async Task StartProxyOnlySessionAsync(CancellationToken cancellationToken = default)
    {
        if (IsProxyOnlySessionActive) return;
        if (_proxyOnlySession is not null)
        {
            await _proxyOnlySession.DisposeAsync().ConfigureAwait(false);
            _proxyOnlySession = null;
        }
        var settings = Settings.Current;
        if (string.IsNullOrWhiteSpace(settings.SelectedServerHost) || settings.SelectedServerPort is < 1 or > 65535)
            throw new InvalidOperationException("Choose and save a live VPN location before starting proxy-only mode.");
        var server = new FoxyVPN.Windows.Models.ServerOption(
            settings.SelectedLocationDisplay,
            settings.SelectedCountryCode,
            string.Empty,
            settings.SelectedCityCode,
            settings.SelectedServerHost,
            settings.SelectedServerPort);
        _proxyOnlySession = await WindowsProxyTunnelSession.StartAsync(
            server,
            settings,
            ProxyPasses,
            (level, message) =>
            {
                if (level.Equals("ERROR", StringComparison.OrdinalIgnoreCase)) Log.Error("VPNTransport", message);
                else if (level.Equals("WARN", StringComparison.OrdinalIgnoreCase)) Log.Warn("VPNTransport", message);
                else Log.Info("VPNTransport", message);
            },
            cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _connectionStartedAt, 0);
        _ = ConnectionUptime;
    }

    public async Task StopProxyOnlySessionAsync()
    {
        var session = Interlocked.Exchange(ref _proxyOnlySession, null);
        if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
        Interlocked.Exchange(ref _connectionStartedAt, 0);
    }

    public async Task StartSystemTunnelAsync(CancellationToken cancellationToken = default)
    {
        await Tunnel.ConnectAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _connectionStartedAt, 0);
        _ = ConnectionUptime;
    }

    public async Task StopSystemTunnelAsync(CancellationToken cancellationToken = default)
    {
        await Tunnel.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _connectionStartedAt, 0);
    }

    private void WriteTunnelLog(string level, string message)
    {
        if (level.Equals("ERROR", StringComparison.OrdinalIgnoreCase)) Log.Error("SystemTunnel", message);
        else if (level.Equals("WARN", StringComparison.OrdinalIgnoreCase)) Log.Warn("SystemTunnel", message);
        else if (level.Equals("DEBUG", StringComparison.OrdinalIgnoreCase)) Log.Info("SystemTunnel", "DEBUG: " + message);
        else Log.Info("SystemTunnel", message);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        try { await StopProxyOnlySessionAsync().ConfigureAwait(false); } catch (Exception ex) { Log.Warn("VPNTransport", $"Proxy-only shutdown reported {ex.GetType().Name}."); }
        await Tunnel.DisposeAsync().ConfigureAwait(false);
        Servers.Dispose();
        ProxyPasses.Dispose();
        Auth.Dispose();
        Guardian.Dispose();
    }
}
