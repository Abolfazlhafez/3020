using FoxyVPN.Windows.Models;
using System.Net;

namespace FoxyVPN.Windows.Services;

/// <summary>
/// Orchestrates an actual TLS/HTTP2 CONNECT session and the localhost SOCKS5 TCP proxy.
/// This is the app's proxy-only transport foundation. It does NOT redirect system-wide traffic and is NOT a TUN VPN.
/// </summary>
public sealed class WindowsProxyTunnelSession : IAsyncDisposable
{
    private readonly WindowsHttp2ConnectSession _http2;
    private readonly WindowsSocks5ProxyServer _socks5;
    private readonly Action<string, string> _log;
    private int _disposed;

    private WindowsProxyTunnelSession(WindowsHttp2ConnectSession http2, WindowsSocks5ProxyServer socks5, Action<string, string> logger)
    {
        _http2 = http2;
        _socks5 = socks5;
        _log = logger;
    }

    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _http2.IsConnected && _socks5.IsRunning;
    public int LocalProxyPort => _socks5.BoundPort;
    /// <summary>The resolved edge IP/port used by TLS; needed for a future physical-interface bypass route.</summary>
    public IPEndPoint? UpstreamRemoteEndPoint => _http2.RemoteEndPoint;
    public IPEndPoint? UpstreamLocalEndPoint => _http2.LocalEndPoint;

    public static async Task<WindowsProxyTunnelSession> StartAsync(
        ServerOption server,
        AppSettings settings,
        GuardianProxyPassProvider proxyPassProvider,
        Action<string, string>? logger = null,
        CancellationToken cancellationToken = default,
        bool forSystemTunnel = false)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(proxyPassProvider);
        var log = logger ?? ((_, _) => { });

        if (!SettingsStore.IsValidHostname(server.Host) && !SettingsStore.IsIpAddress(server.Host))
            throw new InvalidOperationException("The selected VPN server does not have a valid CONNECT hostname.");
        if (server.Port is < 1 or > 65535) throw new InvalidOperationException("The selected VPN server port is invalid.");
        WindowsUpstreamProxyOptions? upstreamProxy = null;
        if (settings.UpstreamProxyEnabled)
        {
            if (!SettingsStore.IsValidHostname(settings.UpstreamProxyHost) && !SettingsStore.IsIpAddress(settings.UpstreamProxyHost))
                throw new InvalidOperationException("Upstream proxy chaining is enabled, but its host is not a valid hostname or IP address.");
            if (settings.UpstreamProxyPort is < 1 or > 65535)
                throw new InvalidOperationException("The configured upstream proxy port is invalid.");
            upstreamProxy = new WindowsUpstreamProxyOptions(
                settings.UpstreamProxyType,
                settings.UpstreamProxyHost,
                settings.UpstreamProxyPort,
                settings.UpstreamProxyUsername,
                settings.UpstreamProxyPassword);
        }
        if (!settings.ProxyOnlyMode && !forSystemTunnel)
            throw new InvalidOperationException("This session only starts a local proxy unless called by the full-system tunnel runtime.");
        if (!IPAddress.TryParse(settings.SocksBindAddress, out var bindAddress) || !IPAddress.IsLoopback(bindAddress))
            throw new InvalidOperationException("The local proxy may bind only to 127.0.0.1 or ::1.");
        if (settings.SocksPort is < 1 or > 65535) throw new InvalidOperationException("The local SOCKS5 port is invalid.");

        var connectHost = string.IsNullOrWhiteSpace(settings.CustomEdgeAddress) ? server.Host : settings.CustomEdgeAddress.Trim();
        if (!SettingsStore.IsValidHostname(connectHost) && !SettingsStore.IsIpAddress(connectHost))
            throw new InvalidOperationException("The configured edge address is not a valid IP address or hostname.");

        // Match Android lifecycle: obtain a fresh Guardian proxy pass before opening the transport.
        // It is cached until shortly before expiry and is never written to logs.
        _ = await proxyPassProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        var http2 = await WindowsHttp2ConnectSession.ConnectAsync(
            connectHost,
            server.Host,
            server.Port,
            proxyPassProvider.GetTokenAsync,
            log,
            cancellationToken,
            upstreamProxy).ConfigureAwait(false);
        var socks = new WindowsSocks5ProxyServer(http2, log);
        try
        {
            await socks.StartAsync(bindAddress.ToString(), settings.SocksPort, cancellationToken).ConfigureAwait(false);
            log("INFO", $"Local SOCKS5 proxy is listening on {bindAddress}:{settings.SocksPort} through the authenticated HTTP/2 CONNECT transport with DNS-only UDP relay. General UDP/QUIC is dropped because the edge protocol exposes TCP CONNECT.");
            if (!forSystemTunnel) log("INFO", "Proxy-only mode selected: other Windows applications are not redirected and system routes have not been changed.");
            return new WindowsProxyTunnelSession(http2, socks, log);
        }
        catch
        {
            await socks.DisposeAsync().ConfigureAwait(false);
            await http2.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { await _socks5.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            await _http2.DisposeAsync().ConfigureAwait(false);
            _log("INFO", "Local proxy and HTTP/2 CONNECT session stopped. No system routes were touched by proxy-only mode.");
        }
    }
}
