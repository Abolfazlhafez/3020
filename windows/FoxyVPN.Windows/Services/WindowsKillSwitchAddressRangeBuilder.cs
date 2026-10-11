using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace FoxyVPN.Windows.Services;

/// <summary>
/// Computes public-address CIDR ranges blocked on physical adapters. RFC1918/ULA, loopback,
/// link-local, multicast/reserved ranges and one explicit transport endpoint are left outside the
/// block set. This is pure logic and can be tested without modifying Windows Firewall.
/// </summary>
public static class WindowsKillSwitchAddressRangeBuilder
{
    public static IReadOnlyList<string> Build(AddressFamily family, IPAddress? allowedEndpoint = null)
    {
        if (family is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            throw new ArgumentOutOfRangeException(nameof(family), "Only IPv4 and IPv6 are supported.");
        if (allowedEndpoint is not null && allowedEndpoint.AddressFamily != family)
            throw new ArgumentException("The allowed endpoint must match the requested address family.", nameof(allowedEndpoint));

        var exclusions = new List<string>();
        if (family == AddressFamily.InterNetwork)
        {
            exclusions.AddRange(["0.0.0.0/8", "10.0.0.0/8", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12", "192.168.0.0/16", "224.0.0.0/4", "240.0.0.0/4"]);
            if (allowedEndpoint is not null) exclusions.Add(ToPrefix(allowedEndpoint, 32));
        }
        else
        {
            exclusions.AddRange(["::/128", "::1/128", "fc00::/7", "fe80::/10", "ff00::/8"]);
            if (allowedEndpoint is not null) exclusions.Add(ToPrefix(allowedEndpoint, 128));
        }

        var bits = family == AddressFamily.InterNetwork ? 32 : 128;
        var blocks = exclusions.Select(item => ParseCidr(item, family)).ToArray();
        var results = new List<string>();
        void Split(BigInteger network, int prefixLength)
        {
            network = MaskNetwork(network, bits, prefixLength);
            if (blocks.Any(block => block.PrefixLength <= prefixLength && MaskNetwork(network, bits, block.PrefixLength) == block.Network))
                return;
            var hasExcludedChild = blocks.Any(block => block.PrefixLength > prefixLength && MaskNetwork(block.Network, bits, prefixLength) == network);
            if (!hasExcludedChild)
            {
                results.Add(ToCidr(network, bits, prefixLength));
                return;
            }
            if (prefixLength >= bits) return;
            var childPrefix = prefixLength + 1;
            var half = BigInteger.One << (bits - childPrefix);
            Split(network, childPrefix);
            Split(network + half, childPrefix);
        }

        Split(BigInteger.Zero, 0);
        return results;
    }

    private static (BigInteger Network, int PrefixLength) ParseCidr(string cidr, AddressFamily family)
    {
        var pieces = cidr.Split('/', 2);
        if (pieces.Length != 2 || !IPAddress.TryParse(pieces[0], out var address) || address.AddressFamily != family ||
            !int.TryParse(pieces[1], out var prefix) || prefix < 0 || prefix > address.GetAddressBytes().Length * 8)
            throw new InvalidOperationException("Invalid built-in CIDR used by the VPN kill switch.");
        var bits = address.GetAddressBytes().Length * 8;
        var value = new BigInteger(address.GetAddressBytes(), isUnsigned: true, isBigEndian: true);
        return (MaskNetwork(value, bits, prefix), prefix);
    }

    private static BigInteger MaskNetwork(BigInteger value, int bits, int prefixLength)
    {
        if (prefixLength <= 0) return BigInteger.Zero;
        var shift = bits - prefixLength;
        return (value >> shift) << shift;
    }

    private static string ToCidr(BigInteger network, int bits, int prefixLength)
    {
        var byteLength = bits / 8;
        var raw = network.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length > byteLength) raw = raw[^byteLength..];
        var bytes = new byte[byteLength];
        Buffer.BlockCopy(raw, 0, bytes, byteLength - raw.Length, raw.Length);
        return new IPAddress(bytes) + "/" + prefixLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ToPrefix(IPAddress address, int prefixLength) => $"{new IPAddress(address.GetAddressBytes())}/{prefixLength}";
}
