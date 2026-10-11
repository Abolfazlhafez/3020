using System.Net;
using System.Net.Sockets;
using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class WindowsIpPacketInspectorTests
{
    [Fact]
    public void InspectsIPv4UdpDnsPacket()
    {
        var packet = new byte[28];
        packet[0] = 0x45;
        packet[2] = 0;
        packet[3] = 28;
        packet[8] = 64;
        packet[9] = 17;
        new byte[] { 10, 8, 0, 2 }.CopyTo(packet, 12);
        new byte[] { 1, 1, 1, 1 }.CopyTo(packet, 16);
        packet[20] = 0x30; packet[21] = 0x39; // source 12345
        packet[22] = 0; packet[23] = 53;     // destination DNS

        Assert.True(WindowsIpPacketInspector.TryInspect(packet, out var info));
        Assert.NotNull(info);
        Assert.Equal(AddressFamily.InterNetwork, info!.AddressFamily);
        Assert.Equal(IPAddress.Parse("10.8.0.2"), info.SourceAddress);
        Assert.Equal(IPAddress.Parse("1.1.1.1"), info.DestinationAddress);
        Assert.Equal(WindowsIpProtocol.Udp, info.Protocol);
        Assert.Equal(12345, info.SourcePort);
        Assert.Equal(53, info.DestinationPort);
        Assert.True(info.IsDnsPacket);
        Assert.Equal(28, info.PacketLength);
    }

    [Fact]
    public void RejectsMalformedIPv4HeaderAndTruncatedPacket()
    {
        var badHeader = new byte[20];
        badHeader[0] = 0x44; // IHL is 16 bytes, smaller than the IPv4 minimum.
        Assert.False(WindowsIpPacketInspector.TryInspect(badHeader, out _));

        var truncated = new byte[20];
        truncated[0] = 0x45;
        truncated[2] = 0;
        truncated[3] = 40;
        Assert.False(WindowsIpPacketInspector.TryInspect(truncated, out _));
    }

    [Fact]
    public void InspectsIPv6UdpPacket()
    {
        var packet = new byte[48];
        packet[0] = 0x60;
        packet[4] = 0;
        packet[5] = 8;
        packet[6] = 17;
        packet[7] = 64;
        packet[8] = 0x20; packet[9] = 0x01; packet[10] = 0x0d; packet[11] = 0xb8;
        packet[24] = 0x20; packet[25] = 0x01; packet[26] = 0x48; packet[27] = 0x60;
        packet[40] = 0xD4; packet[41] = 0x31; // source port 54321
        packet[42] = 0; packet[43] = 53;
        packet[44] = 0; packet[45] = 8;

        Assert.True(WindowsIpPacketInspector.TryInspect(packet, out var info));
        Assert.NotNull(info);
        Assert.Equal(AddressFamily.InterNetworkV6, info!.AddressFamily);
        Assert.Equal(WindowsIpProtocol.Udp, info.Protocol);
        Assert.Equal(54321, info.SourcePort);
        Assert.Equal(53, info.DestinationPort);
        Assert.True(info.IsDnsPacket);
    }

    [Fact]
    public void DoesNotReadPortsFromNonInitialIPv4Fragment()
    {
        var packet = new byte[28];
        packet[0] = 0x45;
        packet[2] = 0; packet[3] = 28;
        packet[6] = 0; packet[7] = 1; // fragment offset = 1
        packet[9] = 17;

        Assert.True(WindowsIpPacketInspector.TryInspect(packet, out var info));
        Assert.NotNull(info);
        Assert.Null(info!.SourcePort);
        Assert.Null(info.DestinationPort);
        Assert.False(info.IsDnsPacket);
    }
}
