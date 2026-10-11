using FoxyVPN.Windows.Models;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class ServerOptionTests
{
    [Fact]
    public void Key_IsStableAndCaseInsensitiveForEndpointIdentity()
    {
        var a = new ServerOption("Country", "us", "City", "nyc", "VPN.EXAMPLE.COM", 443);
        var b = new ServerOption("Country", "US", "City", "NYC", "vpn.example.com", 443);
        Assert.Equal(a.Key, b.Key);
    }

    [Fact]
    public void Key_DistinguishesDifferentPorts()
    {
        var a = new ServerOption("Country", "us", "City", "nyc", "vpn.example.com", 443);
        var b = new ServerOption("Country", "us", "City", "nyc", "vpn.example.com", 8443);
        Assert.NotEqual(a.Key, b.Key);
    }
}
