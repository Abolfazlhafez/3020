using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace FoxyVPN.Windows.Services;

public enum WindowsIpProtocol
{
    IcmpV4 = 1,
    Tcp = 6,
    Udp = 17,
    IcmpV6 = 58,
    Other = 255,
}

/// <summary>Metadata parsed from a layer-3 IPv4/IPv6 packet. The parser never mutates packet bytes.</summary>
public sealed record WindowsIpPacketInfo(
    AddressFamily AddressFamily,
    IPAddress SourceAddress,
    IPAddress DestinationAddress,
    WindowsIpProtocol Protocol,
    int PacketLength,
    int NetworkHeaderLength,
    int? SourcePort,
    int? DestinationPort,
    bool IsDnsPacket);

/// <summary>
/// Bounds-checked L3 packet parser used by the Wintun boundary and tests. It does not forward,
/// rewrite, or synthesize packets and must not be mistaken for a TCP/IP stack.
/// </summary>
public static class WindowsIpPacketInspector
{
    private const int IPv4MinimumHeaderLength = 20;
    private const int IPv6HeaderLength = 40;
    private const int MaximumIPv6ExtensionHeaders = 8;
    private const int MaximumIPv6ExtensionBytes = 2048;
    private const int DnsPort = 53;

    public static bool TryInspect(ReadOnlySpan<byte> packet, out WindowsIpPacketInfo? info)
    {
        info = null;
        if (packet.Length < 1) return false;

        var version = packet[0] >> 4;
        return version switch
        {
            4 => TryInspectIPv4(packet, out info),
            6 => TryInspectIPv6(packet, out info),
            _ => false,
        };
    }

    private static bool TryInspectIPv4(ReadOnlySpan<byte> packet, out WindowsIpPacketInfo? info)
    {
        info = null;
        if (packet.Length < IPv4MinimumHeaderLength) return false;

        var headerLength = (packet[0] & 0x0F) * 4;
        if (headerLength < IPv4MinimumHeaderLength || headerLength > packet.Length) return false;

        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2));
        if (totalLength < headerLength || totalLength > packet.Length) return false;

        var protocolByte = packet[9];
        var protocol = ToProtocol(protocolByte);
        var source = new IPAddress(packet.Slice(12, 4));
        var destination = new IPAddress(packet.Slice(16, 4));
        var fragmentField = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(6, 2));
        var isNonInitialFragment = (fragmentField & 0x1FFF) != 0;

        int? sourcePort = null;
        int? destinationPort = null;
        if (!isNonInitialFragment && IsTcpOrUdp(protocol) && totalLength >= headerLength + 4)
        {
            sourcePort = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(headerLength, 2));
            destinationPort = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(headerLength + 2, 2));
        }

        info = new WindowsIpPacketInfo(
            AddressFamily.InterNetwork,
            source,
            destination,
            protocol,
            totalLength,
            headerLength,
            sourcePort,
            destinationPort,
            IsDns(protocol, sourcePort, destinationPort));
        return true;
    }

    private static bool TryInspectIPv6(ReadOnlySpan<byte> packet, out WindowsIpPacketInfo? info)
    {
        info = null;
        if (packet.Length < IPv6HeaderLength) return false;

        var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(4, 2));
        // IPv6 jumbograms use a zero payload length and a Jumbo Payload option; not used by this client.
        if (payloadLength == 0) return false;
        var totalLength = IPv6HeaderLength + payloadLength;
        if (totalLength > packet.Length) return false;

        var source = new IPAddress(packet.Slice(8, 16));
        var destination = new IPAddress(packet.Slice(24, 16));
        var nextHeader = packet[6];
        var offset = IPv6HeaderLength;
        var walkedBytes = 0;
        var nonInitialFragment = false;
        var extensions = 0;

        while (IsIPv6ExtensionHeader(nextHeader))
        {
            if (++extensions > MaximumIPv6ExtensionHeaders || offset >= totalLength) return false;
            var currentType = nextHeader;
            var next = packet[offset];
            int extensionLength;

            if (currentType == 44) // Fragment header is always 8 bytes.
            {
                if (totalLength - offset < 8) return false;
                var fragmentField = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset + 2, 2));
                nonInitialFragment = (fragmentField & 0xFFF8) != 0;
                extensionLength = 8;
            }
            else if (currentType == 51) // Authentication Header length is in 32-bit words, excluding first two.
            {
                if (totalLength - offset < 2) return false;
                extensionLength = (packet[offset + 1] + 2) * 4;
            }
            else // Hop-by-hop, routing, destination, mobility, HIP, and Shim6 use 8-octet units.
            {
                if (totalLength - offset < 2) return false;
                extensionLength = (packet[offset + 1] + 1) * 8;
            }

            if (extensionLength < 8 || extensionLength > totalLength - offset) return false;
            walkedBytes += extensionLength;
            if (walkedBytes > MaximumIPv6ExtensionBytes) return false;
            offset += extensionLength;
            nextHeader = next;
        }

        var protocol = ToProtocol(nextHeader);
        int? sourcePort = null;
        int? destinationPort = null;
        if (!nonInitialFragment && IsTcpOrUdp(protocol) && totalLength - offset >= 4)
        {
            sourcePort = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset, 2));
            destinationPort = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset + 2, 2));
        }

        info = new WindowsIpPacketInfo(
            AddressFamily.InterNetworkV6,
            source,
            destination,
            protocol,
            totalLength,
            offset,
            sourcePort,
            destinationPort,
            IsDns(protocol, sourcePort, destinationPort));
        return true;
    }

    private static bool IsIPv6ExtensionHeader(byte value) => value is 0 or 43 or 44 or 51 or 60 or 135 or 139 or 140;
    private static bool IsTcpOrUdp(WindowsIpProtocol protocol) => protocol is WindowsIpProtocol.Tcp or WindowsIpProtocol.Udp;
    private static bool IsDns(WindowsIpProtocol protocol, int? sourcePort, int? destinationPort) =>
        IsTcpOrUdp(protocol) && (sourcePort == DnsPort || destinationPort == DnsPort);

    private static WindowsIpProtocol ToProtocol(byte protocol) => protocol switch
    {
        1 => WindowsIpProtocol.IcmpV4,
        6 => WindowsIpProtocol.Tcp,
        17 => WindowsIpProtocol.Udp,
        58 => WindowsIpProtocol.IcmpV6,
        _ => WindowsIpProtocol.Other,
    };
}
