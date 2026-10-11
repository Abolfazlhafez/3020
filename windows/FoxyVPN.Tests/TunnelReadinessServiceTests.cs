using FoxyVPN.Windows.Models;
using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class TunnelReadinessServiceTests
{
    private sealed class FakeTunProbe(bool present) : IWindowsTunRuntimeProbe
    {
        public TunRuntimeStatus Inspect(string applicationDirectory) => new(present, present ? "test runtime present" : "test runtime missing");
    }

    [Fact]
    public void ReportExplicitlyBlocksWhenVpnDataPlaneComponentsAreMissing()
    {
        var service = new TunnelReadinessService(
            new FakeTunProbe(present: true),
            isSupportedWindows: () => true,
            http2ConnectImplemented: false,
            socksRelayIntegrated: false,
            routeAndDnsLifecycleImplemented: false);
        var settings = new AppSettings { SelectedServerHost = "vpn.example.org", SelectedServerPort = 443 };

        var report = service.Evaluate(settings, signedIn: true, applicationDirectory: ".");

        Assert.False(report.CanConnect);
        Assert.Contains(report.Checks, x => x.Name == "HTTP/2 CONNECT transport" && !x.Passed);
        Assert.Contains(report.Checks, x => x.Name == "SOCKS5 stream relay" && !x.Passed);
        Assert.Contains(report.Checks, x => x.Name == "Route and DNS lifecycle" && !x.Passed);
        Assert.Contains("BLOCKED", report.ToDisplayText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportRequiresAuthenticationAndValidEndpoint()
    {
        var service = new TunnelReadinessService(new FakeTunProbe(present: false), () => true);
        var report = service.Evaluate(new AppSettings(), signedIn: false, applicationDirectory: ".");

        Assert.Contains(report.Checks, x => x.Name == "Firefox Account session" && !x.Passed);
        Assert.Contains(report.Checks, x => x.Name == "Selected server endpoint" && !x.Passed);
        Assert.Contains(report.Checks, x => x.Name == "Windows TUN runtime" && !x.Passed);
    }

    [Fact]
    public void ReadinessBlocksIncompleteSocks5UpstreamCredentials()
    {
        var service = new TunnelReadinessService(new FakeTunProbe(present: true), () => true,
            http2ConnectImplemented: true, socksRelayIntegrated: true, routeAndDnsLifecycleImplemented: true);
        var settings = new AppSettings
        {
            SelectedServerHost = "vpn.example.org",
            SelectedServerPort = 443,
            UpstreamProxyEnabled = true,
            UpstreamProxyType = "SOCKS5",
            UpstreamProxyHost = "proxy.example.test",
            UpstreamProxyPort = 1080,
            UpstreamProxyUsername = "alice",
            UpstreamProxyPassword = ""
        };

        var report = service.Evaluate(settings, signedIn: true, applicationDirectory: ".");

        var check = Assert.Single(report.Checks.Where(item => item.Name == "Upstream proxy configuration"));
        Assert.False(check.Passed);
        Assert.Contains("requires both", check.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndpointAndAccountCanPassButMissingTunStillBlocks()
    {
        var service = new TunnelReadinessService(new FakeTunProbe(present: false), () => true);
        var settings = new AppSettings { SelectedServerHost = "vpn.example.org", SelectedServerPort = 443 };
        var report = service.Evaluate(settings, signedIn: true, applicationDirectory: ".");

        Assert.Contains(report.Checks, x => x.Name == "Firefox Account session" && x.Passed);
        Assert.Contains(report.Checks, x => x.Name == "Selected server endpoint" && x.Passed);
        Assert.False(report.CanConnect);
    }
}
