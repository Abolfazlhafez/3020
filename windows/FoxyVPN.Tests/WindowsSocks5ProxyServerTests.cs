using System.IO;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class WindowsSocks5ProxyServerTests
{
    [Fact]
    public async Task StartAsync_RejectsNonLoopbackBinding()
    {
        await using var server = new WindowsSocks5ProxyServer(new TestUpstreamProvider(1));
        Assert.Throws<InvalidOperationException>(() => server.StartAsync("0.0.0.0", 0));
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task StartAsync_RefusesToStartWithoutConnectedUpstream()
    {
        var upstream = new TestUpstreamProvider(1) { IsConnected = false };
        await using var server = new WindowsSocks5ProxyServer(upstream);
        Assert.Throws<InvalidOperationException>(() => server.StartAsync("127.0.0.1", 0));
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task ConnectCommand_RelaysDataThroughInjectedUpstream_AndSupportsClientHalfClose()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var token = timeout.Token;
        var echoListener = new TcpListener(IPAddress.Loopback, 0);
        echoListener.Start();
        var echoPort = ((IPEndPoint)echoListener.LocalEndpoint).Port;
        var upstream = new TestUpstreamProvider(echoPort);
        await using var proxy = new WindowsSocks5ProxyServer(upstream);

        try
        {
            var echoTask = Task.Run(async () =>
            {
                using var remote = await echoListener.AcceptTcpClientAsync(token);
                var stream = remote.GetStream();
                var payload = new byte[4];
                await ReadExactlyAsync(stream, payload, token);
                Assert.Equal("ping", Encoding.ASCII.GetString(payload));
                await stream.WriteAsync(Encoding.ASCII.GetBytes("pong"), token);
            }, token);

            await proxy.StartAsync("127.0.0.1", 0, token);
            Assert.True(proxy.IsRunning);
            Assert.InRange(proxy.BoundPort, 1, 65535);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.BoundPort, token);
            var clientStream = client.GetStream();

            await clientStream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, token);
            var greetingReply = new byte[2];
            await ReadExactlyAsync(clientStream, greetingReply, token);
            Assert.Equal(new byte[] { 0x05, 0x00 }, greetingReply);

            var hostBytes = Encoding.ASCII.GetBytes("example.com");
            var request = new byte[5 + hostBytes.Length + 2];
            request[0] = 0x05;
            request[1] = 0x01; // CONNECT
            request[2] = 0x00;
            request[3] = 0x03; // domain name
            request[4] = (byte)hostBytes.Length;
            hostBytes.CopyTo(request, 5);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(5 + hostBytes.Length, 2), 443);
            await clientStream.WriteAsync(request, token);

            var connectReply = new byte[10];
            await ReadExactlyAsync(clientStream, connectReply, token);
            Assert.Equal(0x05, connectReply[0]);
            Assert.Equal(0x00, connectReply[1]);
            Assert.Equal("example.com", upstream.OpenedHost);
            Assert.Equal(443, upstream.OpenedPort);
            Assert.Equal(1, upstream.OpenCallCount);

            await clientStream.WriteAsync(Encoding.ASCII.GetBytes("ping"), token);
            client.Client.Shutdown(SocketShutdown.Send);

            var response = new byte[4];
            await ReadExactlyAsync(clientStream, response, token);
            Assert.Equal("pong", Encoding.ASCII.GetString(response));
            await echoTask.WaitAsync(TimeSpan.FromSeconds(4), token);
        }
        finally
        {
            echoListener.Stop();
            await proxy.StopAsync();
        }
    }


    [Fact]
    public async Task ConnectCommand_BlocksLoopbackDestinationBeforeOpeningUpstream()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var upstream = new TestUpstreamProvider(1);
        await using var proxy = new WindowsSocks5ProxyServer(upstream);
        await proxy.StartAsync("127.0.0.1", 0, timeout.Token);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.BoundPort, timeout.Token);
        var stream = client.GetStream();

        await NegotiateNoAuthAsync(stream, timeout.Token);
        byte[] request = [0x05, 0x01, 0x00, 0x01, 127, 0, 0, 1, 0x01, 0xBB]; // 127.0.0.1:443
        await stream.WriteAsync(request, timeout.Token);
        var reply = new byte[10];
        await ReadExactlyAsync(stream, reply, timeout.Token);
        Assert.Equal(0x02, reply[1]); // connection not allowed by ruleset
        Assert.Equal(0, upstream.OpenCallCount); // must not query/connect the upstream
    }

    [Fact]
    public async Task UdpAssociate_RelaysDnsOverUpstreamAndKeepsAssociationLocal()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var token = timeout.Token;
        var upstream = new TestDnsUpstreamProvider();
        await using var proxy = new WindowsSocks5ProxyServer(upstream);
        await proxy.StartAsync("127.0.0.1", 0, token);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.BoundPort, token);
        var stream = client.GetStream();

        await NegotiateNoAuthAsync(stream, token);
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var clientUdpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        byte[] association = [0x05, 0x03, 0x00, 0x01, 127, 0, 0, 1,
            (byte)(clientUdpPort >> 8), (byte)(clientUdpPort & 0xff)]; // client UDP endpoint, not destination port
        await stream.WriteAsync(association, token);
        var reply = new byte[10];
        await ReadExactlyAsync(stream, reply, token);
        Assert.Equal(0x05, reply[0]);
        Assert.Equal(0x00, reply[1]);
        Assert.Equal(0x01, reply[3]);
        var relayPort = BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(8, 2));
        Assert.InRange(relayPort, 1, 65535);

        byte[] query = [0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0];
        byte[] datagram = new byte[10 + query.Length];
        datagram[0] = 0; datagram[1] = 0; datagram[2] = 0; datagram[3] = 0x01;
        datagram[4] = 8; datagram[5] = 8; datagram[6] = 8; datagram[7] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(8, 2), 53);
        query.CopyTo(datagram, 10);

        await udp.SendAsync(datagram, new IPEndPoint(IPAddress.Loopback, relayPort), token);
        var dnsReply = await udp.ReceiveAsync(token);
        Assert.Equal(IPAddress.Loopback, dnsReply.RemoteEndPoint.Address);
        Assert.Equal(relayPort, dnsReply.RemoteEndPoint.Port);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00 }, dnsReply.Buffer[..3]);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x81, 0x80 }, dnsReply.Buffer[10..14]);
        Assert.Contains(upstream.Openings, opening => opening.Port == 443); // TLS DoH tried over upstream CONNECT first
        Assert.Contains(upstream.Openings, opening => opening.Port == 53);  // then DNS-over-TCP fallback
        Assert.NotNull(upstream.LastDnsStream);
        var framedQuery = upstream.LastDnsStream!.Output.ToArray();
        Assert.Equal((byte)0, framedQuery[0]);
        Assert.Equal((byte)query.Length, framedQuery[1]);
        Assert.Equal(query, framedQuery[2..]);
    }

    [Fact]
    public async Task UdpAssociate_DropsNonDnsDatagramsWithoutOpeningUpstream()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var token = timeout.Token;
        var upstream = new TestDnsUpstreamProvider();
        var unsupportedLogged = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var proxy = new WindowsSocks5ProxyServer(upstream, (_, message) =>
        {
            if (message.Contains("Dropped non-DNS UDP", StringComparison.Ordinal)) unsupportedLogged.TrySetResult(true);
        });
        await proxy.StartAsync("127.0.0.1", 0, token);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.BoundPort, token);
        var control = client.GetStream();
        await NegotiateNoAuthAsync(control, token);
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var clientUdpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        byte[] association = [0x05, 0x03, 0x00, 0x01, 127, 0, 0, 1,
            (byte)(clientUdpPort >> 8), (byte)(clientUdpPort & 0xff)];
        await control.WriteAsync(association, token);
        var reply = new byte[10];
        await ReadExactlyAsync(control, reply, token);
        var relayPort = BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(8, 2));
        byte[] packet = [0, 0, 0, 0x01, 8, 8, 8, 8, 0x01, 0xBB, 1, 2, 3, 4]; // UDP port 443 (QUIC)
        await udp.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, relayPort), token);
        await unsupportedLogged.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
        Assert.Empty(upstream.Openings);
    }

    [Fact]
    public async Task UdpAssociate_DropsDatagramsFromDifferentLocalUdpPort()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var token = timeout.Token;
        var upstream = new TestDnsUpstreamProvider();
        var foreignSourceLogged = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var proxy = new WindowsSocks5ProxyServer(upstream, (_, message) =>
        {
            if (message.Contains("outside its TCP association", StringComparison.Ordinal)) foreignSourceLogged.TrySetResult(true);
        });
        await proxy.StartAsync("127.0.0.1", 0, token);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.BoundPort, token);
        var control = client.GetStream();
        await NegotiateNoAuthAsync(control, token);
        using var associatedUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var associatedPort = ((IPEndPoint)associatedUdp.Client.LocalEndPoint!).Port;
        byte[] association = [0x05, 0x03, 0x00, 0x01, 127, 0, 0, 1,
            (byte)(associatedPort >> 8), (byte)(associatedPort & 0xff)];
        await control.WriteAsync(association, token);
        var reply = new byte[10];
        await ReadExactlyAsync(control, reply, token);
        var relayPort = BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(8, 2));
        byte[] packet = [0, 0, 0, 0x01, 8, 8, 8, 8, 0, 53, 0x12, 0x34, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0];
        using var foreignUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await foreignUdp.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, relayPort), token);
        await foreignSourceLogged.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
        Assert.Empty(upstream.Openings);
    }

    private static async Task NegotiateNoAuthAsync(Stream stream, CancellationToken token)
    {
        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, token);
        var response = new byte[2];
        await ReadExactlyAsync(stream, response, token);
        Assert.Equal(new byte[] { 0x05, 0x00 }, response);
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> destination, CancellationToken token)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var count = await stream.ReadAsync(destination[offset..], token);
            if (count == 0) throw new EndOfStreamException("Unexpected EOF in test protocol response.");
            offset += count;
        }
    }

    private sealed class TestUpstreamProvider(int remotePort) : IUpstreamStreamProvider
    {
        public bool IsConnected { get; set; } = true;
        public string? OpenedHost { get; private set; }
        public int OpenedPort { get; private set; }
        public int OpenCallCount { get; private set; }

        public async ValueTask<IUpstreamDuplexStream> OpenConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            OpenCallCount++;
            OpenedHost = host;
            OpenedPort = port;
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, remotePort, cancellationToken);
                return new TestDuplexStream(client);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }

    private sealed class TestDuplexStream(TcpClient client) : IUpstreamDuplexStream
    {
        private readonly NetworkStream _stream = client.GetStream();
        public Stream Input => _stream;
        public Stream Output => _stream;

        public ValueTask CompleteOutputAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { client.Client.Shutdown(SocketShutdown.Send); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync();
            client.Dispose();
        }
    }

    private sealed class TestDnsUpstreamProvider : IUpstreamStreamProvider
    {
        public bool IsConnected => true;
        public ConcurrentQueue<(string Host, int Port)> Openings { get; } = new();
        public TestDnsDuplexStream? LastDnsStream { get; private set; }

        public ValueTask<IUpstreamDuplexStream> OpenConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Openings.Enqueue((host, port));
            if (port == 443)
                return ValueTask.FromException<IUpstreamDuplexStream>(new IOException("DoH deliberately unavailable in this deterministic test."));
            if (port != 53)
                return ValueTask.FromException<IUpstreamDuplexStream>(new IOException("Unexpected destination port in DNS test."));

            byte[] response = [0x12, 0x34, 0x81, 0x80, 0, 1, 0, 0, 0, 0, 0, 0];
            byte[] framed = new byte[response.Length + 2];
            BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)response.Length);
            response.CopyTo(framed, 2);
            LastDnsStream = new TestDnsDuplexStream(framed);
            return ValueTask.FromResult<IUpstreamDuplexStream>(LastDnsStream);
        }
    }

    private sealed class TestDnsDuplexStream(byte[] response) : IUpstreamDuplexStream
    {
        public Stream Input { get; } = new MemoryStream(response, writable: false);
        public MemoryStream Output { get; } = new();
        Stream IUpstreamDuplexStream.Output => Output;
        public ValueTask CompleteOutputAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
