using System.Globalization;
using System.IO;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace FoxyVPN.Windows.Services;

/// <summary>
/// SOCKS5 UDP ASSOCIATE implementation compatible with the Android policy: DNS on port 53 only;
/// unsupported UDP (including QUIC) is dropped. DNS tries HTTPS to known resolver IPs over the
/// authenticated HTTP/2 CONNECT transport, then falls back to DNS-over-TCP over the same transport.
/// It never opens a direct Internet socket.
/// </summary>
internal sealed class WindowsSocks5UdpDnsRelay : IAsyncDisposable
{
    private const int DnsPort = 53;
    private const int MaxDnsMessageBytes = 4096;
    private const int MaxInflightQueries = 32;
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);
    private const int MaxHttpHeaderBytes = 16 * 1024;

    private static readonly string[][] ResolverPools =
    [
        ["1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"],
        ["8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"],
        ["9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"],
    ];

    private readonly IUpstreamStreamProvider _upstream;
    private readonly IPAddress _clientAddress;
    private readonly int _clientPort;
    private readonly UdpClient _udp;
    private readonly Action<string, string> _log;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _inflight = new(MaxInflightQueries, MaxInflightQueries);
    private readonly ConcurrentDictionary<int, Task> _queries = new();
    private Task? _receiveTask;
    private int _queryId;
    private int _started;
    private int _nonDnsLogged;
    private int _foreignSourceLogged;
    private int _otherDrops;
    private int _disposed;

    public IPEndPoint LocalEndpoint { get; }

    public WindowsSocks5UdpDnsRelay(
        IUpstreamStreamProvider upstream,
        IPEndPoint localEndpoint,
        IPEndPoint associatedClient,
        Action<string, string>? logger = null)
    {
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        ArgumentNullException.ThrowIfNull(localEndpoint);
        ArgumentNullException.ThrowIfNull(associatedClient);
        if (!IPAddress.IsLoopback(localEndpoint.Address) || !IPAddress.IsLoopback(associatedClient.Address))
            throw new InvalidOperationException("SOCKS5 UDP DNS relay is restricted to loopback clients and interfaces.");
        if (!_upstream.IsConnected) throw new InvalidOperationException("The HTTP/2 upstream is not connected.");

        _clientAddress = NormalizeAddress(associatedClient.Address);
        _clientPort = associatedClient.Port;
        _log = logger ?? ((_, _) => { });
        _udp = new UdpClient(new IPEndPoint(localEndpoint.Address, 0));
        _udp.Client.ReceiveBufferSize = 512 * 1024;
        LocalEndpoint = (IPEndPoint)_udp.Client.LocalEndPoint!;
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The UDP DNS relay has already been started.");
        _receiveTask = ReceiveLoopAsync(_lifetime.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var datagram = await _udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (!NormalizeAddress(datagram.RemoteEndPoint.Address).Equals(_clientAddress) ||
                    (_clientPort != 0 && datagram.RemoteEndPoint.Port != _clientPort))
                {
                    if (Interlocked.Exchange(ref _foreignSourceLogged, 1) == 0)
                        _log("WARN", "Dropped a SOCKS5 UDP datagram from an IP outside its TCP association.");
                    continue;
                }
                if (!_inflight.Wait(0))
                {
                    Interlocked.Increment(ref _otherDrops);
                    continue;
                }

                var id = Interlocked.Increment(ref _queryId);
                var task = RelayTrackedAsync(id, datagram, cancellationToken);
                _queries[id] = task;
                _ = task.ContinueWith(
                    _ => _queries.TryRemove(id, out var ignored),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (SocketException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _log("WARN", $"SOCKS5 UDP receive loop stopped ({ex.GetType().Name}).");
        }
    }

    private async Task RelayTrackedAsync(int id, UdpReceiveResult datagram, CancellationToken serverToken)
    {
        try
        {
            using var queryLifetime = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
            queryLifetime.CancelAfter(QueryTimeout);
            await RelayAsync(datagram, queryLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (serverToken.IsCancellationRequested) { }
        catch (OperationCanceledException) { Interlocked.Increment(ref _otherDrops); }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _otherDrops);
            _log("INFO", $"SOCKS5 DNS query {id} failed ({ex.GetType().Name}); no direct-network fallback was attempted.");
        }
        finally { _inflight.Release(); }
    }

    private async Task RelayAsync(UdpReceiveResult datagram, CancellationToken cancellationToken)
    {
        var request = ParseDatagram(datagram.Buffer);
        if (request is null)
        {
            Interlocked.Increment(ref _otherDrops);
            return;
        }
        if (request.Value.Port != DnsPort)
        {
            if (Interlocked.Exchange(ref _nonDnsLogged, 1) == 0)
                _log("INFO", $"Dropped non-DNS UDP to port {request.Value.Port}; general UDP/QUIC is not supported by the current HTTP/2 CONNECT server.");
            return;
        }
        if (request.Value.Payload.Length is < 12 or > MaxDnsMessageBytes)
        {
            Interlocked.Increment(ref _otherDrops);
            return;
        }
        if (WindowsSocks5ProxyServer.IsLocalDestination(request.Value.Host))
        {
            Interlocked.Increment(ref _otherDrops);
            return;
        }
        if (!_upstream.IsConnected) return;

        var response = await ResolveAsync(request.Value.Host, request.Value.Payload, cancellationToken).ConfigureAwait(false);
        if (response is null || response.Length is < 12 or > MaxDnsMessageBytes) return;
        if (!DnsMessageRules.IsResponseToQuery(request.Value.Payload, response))
        {
            Interlocked.Increment(ref _otherDrops);
            return;
        }

        var result = new byte[request.Value.Header.Length + response.Length];
        request.Value.Header.CopyTo(result, 0);
        response.CopyTo(result, request.Value.Header.Length);
        try { await _udp.SendAsync(result, datagram.RemoteEndPoint, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _log("INFO", $"Could not return DNS response ({ex.GetType().Name})."); }
    }

    private async Task<byte[]?> ResolveAsync(string resolverHost, byte[] query, CancellationToken cancellationToken)
    {
        foreach (var candidate in GetDohCandidates(resolverHost).Take(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Keep a bounded budget for DoH so a failed provider can still fall back to DNS-over-TCP.
                using var dohTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                dohTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                var answer = await QueryDohAsync(candidate, query, dohTimeout.Token).ConfigureAwait(false);
                if (answer is { Length: >= 12 and <= MaxDnsMessageBytes } &&
                    DnsMessageRules.IsResponseToQuery(query, answer))
                    return answer;
                if (answer is not null)
                    _log("DEBUG", $"DNS-over-HTTPS via {candidate} returned a response that did not match the query; falling back to DNS-over-TCP.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _log("DEBUG", $"DNS-over-HTTPS via {candidate} failed ({ex.GetType().Name}); falling back to DNS-over-TCP through the authenticated upstream.");
            }
        }

        var tcpAnswer = await QueryDnsOverTcpAsync(resolverHost, query, cancellationToken).ConfigureAwait(false);
        if (tcpAnswer is not { Length: >= 12 and <= MaxDnsMessageBytes } ||
            !DnsMessageRules.IsResponseToQuery(query, tcpAnswer))
        {
            if (tcpAnswer is not null)
                _log("INFO", "DNS-over-TCP returned a response that did not match the query; the response was dropped.");
            return null;
        }
        return tcpAnswer;
    }

    private async Task<byte[]?> QueryDohAsync(string resolverAddress, byte[] query, CancellationToken cancellationToken)
    {
        await using var tunnel = await _upstream.OpenConnectAsync(resolverAddress, 443, cancellationToken).ConfigureAwait(false);
        using var bridge = new SplitDuplexStream(tunnel.Input, tunnel.Output);
        using var tls = new SslStream(bridge, leaveInnerStreamOpen: true);
        using var tlsTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        tlsTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = resolverAddress,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.Online,
            ApplicationProtocols = [SslApplicationProtocol.Http11],
        }, tlsTimeout.Token).ConfigureAwait(false);

        var hostHeader = IPAddress.TryParse(resolverAddress, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{ip}]" : resolverAddress;
        var headers = Encoding.ASCII.GetBytes(
            $"POST /dns-query HTTP/1.1\r\nHost: {hostHeader}\r\nAccept: application/dns-message\r\n" +
            $"Content-Type: application/dns-message\r\nContent-Length: {query.Length}\r\nConnection: close\r\n\r\n");
        await tls.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
        await tls.WriteAsync(query, cancellationToken).ConfigureAwait(false);
        await tls.FlushAsync(cancellationToken).ConfigureAwait(false);
        using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        responseTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        return await ReadDohResponseAsync(tls, responseTimeout.Token).ConfigureAwait(false);
    }

    private async Task<byte[]?> QueryDnsOverTcpAsync(string resolverHost, byte[] query, CancellationToken cancellationToken)
    {
        await using var tunnel = await _upstream.OpenConnectAsync(resolverHost, DnsPort, cancellationToken).ConfigureAwait(false);
        var prefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(prefix, checked((ushort)query.Length));
        await tunnel.Output.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await tunnel.Output.WriteAsync(query, cancellationToken).ConfigureAwait(false);
        await tunnel.Output.FlushAsync(cancellationToken).ConfigureAwait(false);

        var responsePrefix = new byte[2];
        await ReadExactlyAsync(tunnel.Input, responsePrefix, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt16BigEndian(responsePrefix);
        if (length is < 12 or > MaxDnsMessageBytes) return null;
        var response = new byte[length];
        await ReadExactlyAsync(tunnel.Input, response, cancellationToken).ConfigureAwait(false);
        return response;
    }

    private static IReadOnlyList<string> GetDohCandidates(string resolverHost)
    {
        if (!IPAddress.TryParse(resolverHost, out var parsed)) return Array.Empty<string>();
        var normalized = NormalizeAddress(parsed).ToString();
        foreach (var pool in ResolverPools)
        {
            if (pool.Any(candidate => IPAddress.TryParse(candidate, out var address) && NormalizeAddress(address).ToString() == normalized))
                return pool.ToArray();
        }
        return Array.Empty<string>();
    }

    private static async Task<byte[]?> ReadDohResponseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxHttpHeaderBytes + MaxDnsMessageBytes];
        var total = 0;
        var headerEnd = -1;
        while (headerEnd < 0 && total < MaxHttpHeaderBytes)
        {
            var readLimit = Math.Min(2048, MaxHttpHeaderBytes - total);
            var read = await stream.ReadAsync(buffer.AsMemory(total, readLimit), cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;
            total += read;
            headerEnd = FindHeaderEnd(buffer.AsSpan(0, total));
        }
        if (headerEnd < 0 || headerEnd > MaxHttpHeaderBytes) return null;

        if (!DohHttpResponseRules.TryParseResponseHeaders(buffer.AsSpan(0, headerEnd), out var contentLength) ||
            contentLength > MaxDnsMessageBytes)
            return null;

        var bodyStart = headerEnd + 4;
        var expectedEnd = bodyStart + contentLength;
        while (total < expectedEnd)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, expectedEnd - total), cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;
            total += read;
        }
        return buffer.AsSpan(bodyStart, contentLength).ToArray();
    }

    private static int FindHeaderEnd(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i <= bytes.Length - 4; i++)
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n') return i;
        return -1;
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("DNS-over-TCP response closed before its declared message length.");
            offset += read;
        }
    }

    private static Request? ParseDatagram(byte[] datagram)
    {
        if (datagram.Length < 10 || datagram[0] != 0 || datagram[1] != 0 || datagram[2] != 0) return null;
        var startOfAddress = 4;
        string host;
        int portOffset;
        switch (datagram[3])
        {
            case 0x01:
                if (datagram.Length < startOfAddress + 4 + 2) return null;
                host = new IPAddress(datagram.AsSpan(startOfAddress, 4)).ToString();
                portOffset = startOfAddress + 4;
                break;
            case 0x03:
            {
                if (datagram.Length < startOfAddress + 1) return null;
                var length = datagram[startOfAddress];
                if (length == 0 || datagram.Length < startOfAddress + 1 + length + 2) return null;
                var raw = Encoding.ASCII.GetString(datagram, startOfAddress + 1, length);
                try { host = new IdnMapping().GetAscii(raw.TrimEnd('.')).ToLowerInvariant(); }
                catch (ArgumentException) { return null; }
                if (!SettingsStore.IsValidHostname(host)) return null;
                portOffset = startOfAddress + 1 + length;
                break;
            }
            case 0x04:
                if (datagram.Length < startOfAddress + 16 + 2) return null;
                host = new IPAddress(datagram.AsSpan(startOfAddress, 16)).ToString();
                portOffset = startOfAddress + 16;
                break;
            default:
                return null;
        }
        var port = BinaryPrimitives.ReadUInt16BigEndian(datagram.AsSpan(portOffset, 2));
        var payloadOffset = portOffset + 2;
        var header = datagram.AsSpan(0, payloadOffset).ToArray();
        var payload = datagram.AsSpan(payloadOffset).ToArray();
        return new Request(host, port, header, payload);
    }

    private static IPAddress NormalizeAddress(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private readonly record struct Request(string Host, int Port, byte[] Header, byte[] Payload);

    private sealed class SplitDuplexStream(Stream input, Stream output) : Stream
    {
        public override bool CanRead => input.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => output.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => output.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => input.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => input.ReadAsync(buffer, offset, count, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => output.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => output.WriteAsync(buffer, cancellationToken);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => output.WriteAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _udp.Dispose();
        if (_receiveTask is not null)
            try { await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
        var active = _queries.Values.ToArray();
        if (active.Length > 0)
            try { await Task.WhenAll(active).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
        _lifetime.Dispose();
        _log("INFO", $"SOCKS5 DNS relay stopped; {_otherDrops} query/datagram drops recorded. No direct network fallback was used.");
    }
}
