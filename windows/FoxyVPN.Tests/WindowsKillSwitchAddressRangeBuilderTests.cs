using System.Net;
using System.Net.Sockets;
using System.Numerics;
using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class WindowsKillSwitchAddressRangeBuilderTests
{
    [Fact]
    public void Ipv4BlocksPublicRangesAndExemptsPrivateAndExactUpstreamEndpoint()
    {
        var endpoint = IPAddress.Parse("203.0.113.45");
        var ranges = WindowsKillSwitchAddressRangeBuilder.Build(AddressFamily.InterNetwork, endpoint);

        Assert.Contains(ranges, range => Contains(IPAddress.Parse("8.8.8.8"), range));
        Assert.Contains(ranges, range => Contains(IPAddress.Parse("1.1.1.1"), range));
        Assert.DoesNotContain(ranges, range => Contains(IPAddress.Parse("10.12.0.1"), range));
        Assert.DoesNotContain(ranges, range => Contains(IPAddress.Parse("172.20.0.1"), range));
        Assert.DoesNotContain(ranges, range => Contains(IPAddress.Parse("192.168.1.1"), range));
        Assert.DoesNotContain(ranges, range => Contains(IPAddress.Parse("169.254.2.3"), range));
        Assert.DoesNotContain(ranges, range => Contains(IPAddress.Parse("127.0.0.1"), range));
        Assert.DoesNotContain(ranges, range => Contains(endpoint, range));
        AssertNoOverlaps(ranges, AddressFamily.InterNetwork);
    }

    [Fact]
    public void Ipv6BlocksPublicRangesAndExemptsUlaLinkLocalAndExactEndpoint()
    {
        var endpoint = IPAddress.Parse("2001:db8::5");
        var ranges = WindowsKillSwitchAddressRangeBuilder.Build(AddressFamily.InterNetworkV6, endpoint);

        Assert.Contains(ranges, range => Contains(IPAddress.Parse("2001:4860:4860::8888"), range));
        Assert.DoesNotContain(ranges, range => Contains(IPAddress.Parse("fc00::12"), range));
        Assert.DoesNotContain(ranges, range => Contains(IPAddress.Parse("fe80::1"), range));
        Assert.DoesNotContain(ranges, range => Contains(IPAddress.Parse("::1"), range));
        Assert.DoesNotContain(ranges, range => Contains(IPAddress.Parse("ff02::1"), range));
        Assert.DoesNotContain(ranges, range => Contains(endpoint, range));
        AssertNoOverlaps(ranges, AddressFamily.InterNetworkV6);
    }

    [Fact]
    public void RejectsAnEndpointFromTheWrongAddressFamily()
    {
        Assert.Throws<ArgumentException>(() => WindowsKillSwitchAddressRangeBuilder.Build(AddressFamily.InterNetwork, IPAddress.IPv6Loopback));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowsKillSwitchAddressRangeBuilder.Build(AddressFamily.Unspecified));
    }

    private static bool Contains(IPAddress address, string cidr)
    {
        var pieces = cidr.Split('/', 2);
        var network = IPAddress.Parse(pieces[0]).GetAddressBytes();
        var candidate = address.GetAddressBytes();
        var prefix = int.Parse(pieces[1], System.Globalization.CultureInfo.InvariantCulture);
        if (network.Length != candidate.Length) return false;
        for (var i = 0; i < network.Length; i++)
        {
            var bits = Math.Clamp(prefix - 8 * i, 0, 8);
            var mask = bits == 0 ? 0 : (0xFF << (8 - bits)) & 0xFF;
            if ((network[i] & mask) != (candidate[i] & mask)) return false;
        }
        return true;
    }

    private static void AssertNoOverlaps(IReadOnlyList<string> ranges, AddressFamily family)
    {
        for (var i = 0; i < ranges.Count; i++)
        for (var j = i + 1; j < ranges.Count; j++)
        {
            var a = ranges[i].Split('/', 2);
            var b = ranges[j].Split('/', 2);
            var prefixA = int.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
            var prefixB = int.Parse(b[1], System.Globalization.CultureInfo.InvariantCulture);
            var addrA = AddressValue(IPAddress.Parse(a[0]));
            var addrB = AddressValue(IPAddress.Parse(b[0]));
            var bits = family == AddressFamily.InterNetwork ? 32 : 128;
            var common = Math.Min(prefixA, prefixB);
            var shift = bits - common;
            Assert.NotEqual(addrA >> shift, addrB >> shift);
        }
    }

    private static BigInteger AddressValue(IPAddress address) => new(address.GetAddressBytes(), isUnsigned: true, isBigEndian: true);
}
