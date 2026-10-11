using FoxyVPN.Windows.Models;

namespace FoxyVPN.Windows.Services;

/// <summary>Builds a non-invasive, explicit preflight report. It never installs drivers or changes routes.</summary>
public sealed class TunnelReadinessService
{
    private readonly IWindowsTunRuntimeProbe _tunProbe;
    private readonly Func<bool> _isSupportedWindows;
    private readonly bool _http2ConnectImplemented;
    private readonly bool _socksRelayIntegrated;
    private readonly bool _routeAndDnsLifecycleImplemented;

    public TunnelReadinessService(
        IWindowsTunRuntimeProbe? tunProbe = null,
        Func<bool>? isSupportedWindows = null,
        bool http2ConnectImplemented = false,
        bool socksRelayIntegrated = false,
        bool routeAndDnsLifecycleImplemented = false)
    {
        _tunProbe = tunProbe ?? new WindowsTunRuntimeProbe();
        _isSupportedWindows = isSupportedWindows ?? (() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393));
        _http2ConnectImplemented = http2ConnectImplemented;
        _socksRelayIntegrated = socksRelayIntegrated;
        _routeAndDnsLifecycleImplemented = routeAndDnsLifecycleImplemented;
    }

    public TunnelReadinessReport Evaluate(AppSettings settings, bool signedIn, string applicationDirectory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var checks = new List<TunnelReadinessCheck>();

        var supportedOs = _isSupportedWindows();
        checks.Add(new TunnelReadinessCheck(
            "Windows platform",
            supportedOs,
            supportedOs
                ? "Windows 10 version 1607+ or Windows 11 is supported by the .NET 8 desktop target; the selected TUN driver may have additional requirements."
                : "A supported Windows 10/11 build is required. This check does not validate WPF launch or driver compatibility."));

        checks.Add(new TunnelReadinessCheck(
            "Firefox Account session",
            signedIn,
            signedIn ? "An account session is available for control-plane operations. A session alone does not prove a Guardian proxy pass can be issued."
                     : "Sign in first. An authenticated session is required before requesting the Guardian proxy pass."));

        var endpointValid = SettingsStore.IsValidHostname(settings.SelectedServerHost) || SettingsStore.IsIpAddress(settings.SelectedServerHost);
        endpointValid &= settings.SelectedServerPort is >= 1 and <= 65535;
        checks.Add(new TunnelReadinessCheck(
            "Selected server endpoint",
            endpointValid,
            endpointValid
                ? $"A server endpoint is selected ({settings.SelectedServerHost}:{settings.SelectedServerPort}). TCP reachability and protocol acceptance still need to be checked separately."
                : "Choose a live endpoint from Locations before connecting."));

        var upstreamProxySupported = !settings.UpstreamProxyEnabled ||
            ((SettingsStore.IsValidHostname(settings.UpstreamProxyHost) || SettingsStore.IsIpAddress(settings.UpstreamProxyHost)) &&
             settings.UpstreamProxyPort is >= 1 and <= 65535 && (settings.UpstreamProxyType is "HTTP" or "SOCKS5"));
        string? upstreamProxyCredentialIssue = null;
        if (upstreamProxySupported && settings.UpstreamProxyEnabled)
        {
            try
            {
                WindowsHttp2ConnectSession.ValidateUpstreamProxyCredentials(new WindowsUpstreamProxyOptions(
                    settings.UpstreamProxyType,
                    settings.UpstreamProxyHost,
                    settings.UpstreamProxyPort,
                    settings.UpstreamProxyUsername,
                    settings.UpstreamProxyPassword));
            }
            catch (ArgumentException ex)
            {
                upstreamProxySupported = false;
                upstreamProxyCredentialIssue = ex.Message;
            }
        }
        checks.Add(new TunnelReadinessCheck(
            "Upstream proxy configuration",
            upstreamProxySupported,
            upstreamProxySupported
                ? settings.UpstreamProxyEnabled
                    ? $"The configured {settings.UpstreamProxyType} proxy will be used for the TCP/TLS edge socket; the proxy endpoint receives the physical route bypass. Credentials are not logged."
                    : "No upstream proxy chaining is configured."
                : upstreamProxyCredentialIssue ?? "Upstream proxy chaining is enabled but its type, host or port is invalid; correct Settings rather than silently connecting directly."));

        var elevated = WindowsSystemTunnelRuntime.IsCurrentProcessAdministrator();
        checks.Add(new TunnelReadinessCheck(
            "Administrator privileges",
            elevated,
            elevated
                ? "The current process can create the Wintun tunnel and apply system routes."
                : "Full-system VPN changes routes and system DNS policy; restart FoxyVPN with Run as administrator. Proxy-only mode does not require elevation."));

        var tunStatus = _tunProbe.Inspect(applicationDirectory);
        checks.Add(new TunnelReadinessCheck("Windows TUN runtime", tunStatus.Present, tunStatus.Detail));

        checks.Add(new TunnelReadinessCheck(
            "HTTP/2 CONNECT transport",
            _http2ConnectImplemented,
            _http2ConnectImplemented
                ? "The Windows HTTP/2 CONNECT transport source is present: TLS certificate validation, ALPN h2, RFC 7540 frames, HPACK headers, flow control, per-destination CONNECT and optional HTTP/SOCKS5 upstream chaining. Interoperability has not yet been verified against the real service."
                : "The transport code is present but is not marked ready. The source uses TLS/ALPN h2 and CONNECT with proxy-authorization: Bearer."));

        checks.Add(new TunnelReadinessCheck(
            "SOCKS5 stream relay",
            _socksRelayIntegrated,
            _socksRelayIntegrated
                ? "The loopback SOCKS5 relay is wired to the authenticated HTTP/2 provider. DNS uses the local DNS relay and general UDP/QUIC is deliberately dropped because the edge protocol only carries TCP CONNECT. Live edge interoperability still requires final testing."
                : "The SOCKS5 CONNECT/DNS-only UDP relay and HTTP/2 provider can be assembled in explicit proxy-only mode. General UDP/QUIC is dropped and system-wide routes are not changed."));

        checks.Add(new TunnelReadinessCheck(
            "Route and DNS lifecycle",
            _routeAndDnsLifecycleImplemented,
            _routeAndDnsLifecycleImplemented
                ? "The registered runtime wires a route/DNS transaction with physical gateway and edge bypass, IPv4/IPv6 split routes, root NRPT fake DNS, interface-metric journal, cross-process serialization and persistent physical-interface public-egress block rules. Firewall profile state is checked before route mutation; all behavior still needs final Windows integration and leak testing."
                : "The registered runtime uses the pinned HEV native C-API binding and Android-aligned config. This preflight only checks files and never loads native code or changes routes; Windows CI, live service interoperability and DNS/IPv4/IPv6 leak tests remain necessary."));

        return new TunnelReadinessReport(checks);
    }
}
