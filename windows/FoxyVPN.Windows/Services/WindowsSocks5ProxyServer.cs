using System.IO;
using System.Globalization;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FoxyVPN.Windows.Services;

/// <summary>
/// The eventual HTTP/2 CONNECT adapter implements this contract. Input and output are independent
/// because HTTP/2 permits a tunnel stream to be half-closed in one direction while still receiving data.
/// </summary>
public interface IUpstreamStreamProvider
{
    bool IsConnected { get; }
    ValueTask<IUpstreamDuplexStream> OpenConnectAsync(string host, int port, CancellationToken cancellationToken);
}

public interface IUpstreamDuplexStream : IAsyncDisposable
{
    Stream Input { get; }
    Stream Output { get; }
    ValueTask CompleteOutputAsync(CancellationToken cancellationToken);
}

/// <summary>
/// SOCKS5 RFC 1928 CONNECT listener that forwards only through the injected upstream provider.
/// It does not open direct destination sockets. No unauthenticated LAN binding is allowed.
/// UDP ASSOCIATE relays DNS only through the authenticated upstream; other UDP flows are dropped.
/// </summary>
public sealed class WindowsSocks5ProxyServer : IAsyncDisposable
{
    private const int AcceptBacklog = 128;
    private const int MaxConcurrentConnections = 128;
    private const int HandshakeTimeoutSeconds = 10;
    private const int UpstreamOpenTimeoutSeconds = 30;
    private const int RelayBufferBytes = 32 * 1024;
    private static readonly TimeSpan HalfCloseDrainTimeout = TimeSpan.FromMinutes(2);

    private readonly object _lifecycleLock = new();
    private readonly IUpstreamStreamProvider _upstream;
    private readonly Action<string, string> _log;
    private readonly SemaphoreSlim _connectionSlots = new(MaxConcurrentConnections, MaxConcurrentConnections);
    private readonly ConcurrentDictionary<int, TcpClient> _clients = new();
    private readonly ConcurrentDictionary<int, Task> _sessions = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private Task? _acceptLoop;
    private int _connectionId;
    private bool _isRunning;
    private int _disposeStarted;

    public WindowsSocks5ProxyServer(IUpstreamStreamProvider upstream, Action<string, string>? logger = null)
    {
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _log = logger ?? ((_, _) => { });
    }

    public bool IsRunning
    {
        get { lock (_lifecycleLock) return _isRunning; }
    }

    public int BoundPort
    {
        get
        {
            lock (_lifecycleLock)
                return (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;
        }
    }

    public Task StartAsync(string bindAddress, int port, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (!IPAddress.TryParse(bindAddress, out var address) || !IPAddress.IsLoopback(address))
            throw new InvalidOperationException("The unauthenticated SOCKS5 listener may bind only to 127.0.0.1 or ::1. LAN binding is disabled until SOCKS authentication is implemented.");
        if (port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "SOCKS5 port must be between 1 and 65535 (port 0 is reserved for ephemeral test listeners).");
        if (!_upstream.IsConnected)
            throw new InvalidOperationException("The upstream tunnel is not connected. The SOCKS5 listener will not start without a live upstream provider.");

        lock (_lifecycleLock)
        {
            if (_isRunning) throw new InvalidOperationException("The SOCKS5 listener is already running.");
            var listener = new TcpListener(address, port);
            listener.Start(AcceptBacklog);
            var lifetime = new CancellationTokenSource();
            _listener = listener;
            _lifetime = lifetime;
            _isRunning = true;
            _acceptLoop = AcceptLoopAsync(listener, lifetime.Token);
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        TcpListener? listener;
        CancellationTokenSource? lifetime;
        Task? acceptLoop;
        lock (_lifecycleLock)
        {
            listener = _listener;
            lifetime = _lifetime;
            acceptLoop = _acceptLoop;
            _listener = null;
            _lifetime = null;
            _acceptLoop = null;
            _isRunning = false;
        }
        if (lifetime is null) return;

        try { lifetime.Cancel(); } catch (ObjectDisposedException) { }
        try { listener?.Stop(); } catch { }
        foreach (var client in _clients.Values)
        {
            try { client.Dispose(); } catch { }
        }

        if (acceptLoop is not null)
            try { await acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }

        // Closing sockets and the upstream stream in each session should normally make this complete quickly.
        // If a third-party stream ignores cancellation, keep the semaphore alive rather than causing a late Release crash.
        var sessions = _sessions.Values.ToArray();
        if (sessions.Length > 0)
            try { await Task.WhenAll(sessions).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException) { }
            catch { }

        lifetime.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log("WARN", $"SOCKS5 accept failed: {ex.GetType().Name}.");
                try { await Task.Delay(100, cancellationToken).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                client.Dispose();
                break;
            }
            client.NoDelay = true;
            if (!_connectionSlots.Wait(0))
            {
                _log("WARN", "A local SOCKS5 client was rejected because the connection limit was reached.");
                client.Dispose();
                continue;
            }

            var id = Interlocked.Increment(ref _connectionId);
            _clients[id] = client;
            var session = Task.Run(() => HandleTrackedClientAsync(id, client, cancellationToken), CancellationToken.None);
            _sessions[id] = session;
            _ = session.ContinueWith(
                _ => { _sessions.TryRemove(id, out var ignored); },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task HandleTrackedClientAsync(int id, TcpClient client, CancellationToken serverToken)
    {
        try { await HandleClientAsync(client, serverToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (serverToken.IsCancellationRequested) { }
        catch (OperationCanceledException) { _log("WARN", "SOCKS5 handshake or upstream open timed out."); }
        catch (IOException ex) { _log("INFO", $"SOCKS5 flow ended: {ex.GetType().Name}."); }
        catch (SocketException ex) { _log("INFO", $"SOCKS5 socket ended: {ex.SocketErrorCode}."); }
        catch (Exception ex) { _log("WARN", $"SOCKS5 client failed: {ex.GetType().Name}."); }
        finally
        {
            try { client.Dispose(); } catch { }
            _clients.TryRemove(id, out _);
            _connectionSlots.Release();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverToken)
    {
        using var clientLifetime = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(clientLifetime.Token);
        handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(HandshakeTimeoutSeconds));
        var handshakeToken = handshakeTimeout.Token;
        var clientStream = client.GetStream();

        var greeting = new byte[2];
        await ReadExactlyAsync(clientStream, greeting, handshakeToken).ConfigureAwait(false);
        if (greeting[0] != 0x05) return;
        var methodCount = greeting[1];
        if (methodCount == 0)
        {
            await clientStream.WriteAsync(new byte[] { 0x05, 0xFF }, handshakeToken).ConfigureAwait(false);
            return;
        }

        var methods = new byte[methodCount];
        await ReadExactlyAsync(clientStream, methods, handshakeToken).ConfigureAwait(false);
        if (!methods.Contains((byte)0x00))
        {
            await clientStream.WriteAsync(new byte[] { 0x05, 0xFF }, handshakeToken).ConfigureAwait(false);
            return;
        }
        await clientStream.WriteAsync(new byte[] { 0x05, 0x00 }, handshakeToken).ConfigureAwait(false);

        var request = new byte[4];
        await ReadExactlyAsync(clientStream, request, handshakeToken).ConfigureAwait(false);
        if (request[0] != 0x05 || request[2] != 0x00)
        {
            await WriteReplyAsync(clientStream, 0x01, handshakeToken).ConfigureAwait(false);
            return;
        }
        if (request[1] is not (0x01 or 0x03))
        {
            await WriteReplyAsync(clientStream, 0x07, handshakeToken).ConfigureAwait(false);
            return;
        }

        var target = await ReadTargetAsync(clientStream, request[3], handshakeToken).ConfigureAwait(false);
        if (target is null)
        {
            await WriteReplyAsync(clientStream, 0x08, handshakeToken).ConfigureAwait(false);
            return;
        }

        if (request[1] == 0x03)
        {
            // For UDP ASSOCIATE, the request address/port identifies the client's UDP socket,
            // not the DNS destination port. DNS-only policy is enforced per UDP datagram below.
            // Accept an unspecified client address or the same loopback peer that opened this TCP control connection.
            if (client.Client.RemoteEndPoint is not IPEndPoint controlPeer ||
                !IsValidUdpAssociationEndpoint(target.Value.Host, target.Value.Port, controlPeer.Address))
            {
                await WriteReplyAsync(clientStream, 0x02, handshakeToken).ConfigureAwait(false);
                return;
            }
            if (!_upstream.IsConnected)
            {
                await WriteReplyAsync(clientStream, 0x05, handshakeToken).ConfigureAwait(false);
                return;
            }

            WindowsSocks5UdpDnsRelay? dnsRelay = null;
            try
            {
                if (client.Client.LocalEndPoint is not IPEndPoint local || client.Client.RemoteEndPoint is not IPEndPoint remote)
                    throw new IOException("Could not determine the loopback client endpoints for UDP association.");
                dnsRelay = new WindowsSocks5UdpDnsRelay(
                    _upstream,
                    local,
                    new IPEndPoint(remote.Address, target.Value.Port),
                    _log);
                dnsRelay.Start();
                await WriteReplyAsync(clientStream, 0x00, dnsRelay.LocalEndpoint.Address, dnsRelay.LocalEndpoint.Port, serverToken).ConfigureAwait(false);
                // RFC 1928 keeps the UDP association alive for the lifetime of its TCP control connection.
                var keepAlive = new byte[256];
                while (await clientStream.ReadAsync(keepAlive, serverToken).ConfigureAwait(false) != 0) { }
            }
            catch (OperationCanceledException) when (serverToken.IsCancellationRequested) { }
            catch (EndOfStreamException) { }
            catch (IOException ex)
            {
                _log("INFO", $"SOCKS5 UDP association ended ({ex.GetType().Name}).");
            }
            catch (Exception ex)
            {
                _log("WARN", $"SOCKS5 DNS association failed ({ex.GetType().Name}).");
                try { await WriteReplyAsync(clientStream, 0x01, CancellationToken.None).ConfigureAwait(false); } catch { }
            }
            finally
            {
                if (dnsRelay is not null) await dnsRelay.DisposeAsync().ConfigureAwait(false);
            }
            return;
        }

        if (target.Value.Port == 0)
        {
            await WriteReplyAsync(clientStream, 0x08, handshakeToken).ConfigureAwait(false);
            return;
        }
        if (IsLocalDestination(target.Value.Host))
        {
            await WriteReplyAsync(clientStream, 0x02, handshakeToken).ConfigureAwait(false);
            _log("WARN", "A request to a loopback/private/link-local destination was blocked by local SOCKS5 policy.");
            return;
        }
        if (!_upstream.IsConnected)
        {
            await WriteReplyAsync(clientStream, 0x05, handshakeToken).ConfigureAwait(false);
            return;
        }

        IUpstreamDuplexStream? tunnel = null;
        using var openTimeout = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
        openTimeout.CancelAfter(TimeSpan.FromSeconds(UpstreamOpenTimeoutSeconds));
        try
        {
            tunnel = await _upstream.OpenConnectAsync(target.Value.Host, target.Value.Port, openTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!serverToken.IsCancellationRequested)
        {
            await WriteReplyAsync(clientStream, 0x06, CancellationToken.None).ConfigureAwait(false);
            return;
        }
        catch (Exception ex)
        {
            _log("INFO", $"Upstream CONNECT was refused for {target.Value.Host}:{target.Value.Port} ({ex.GetType().Name}).");
            await WriteReplyAsync(clientStream, 0x05, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        if (tunnel is null)
        {
            await WriteReplyAsync(clientStream, 0x01, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        try
        {
            await WriteReplyAsync(clientStream, 0x00, serverToken).ConfigureAwait(false);
            await RelayDuplexAsync(client, clientStream, tunnel, serverToken).ConfigureAwait(false);
        }
        finally
        {
            try { await tunnel.DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }

    private static async Task RelayDuplexAsync(TcpClient client, NetworkStream clientStream, IUpstreamDuplexStream tunnel, CancellationToken serverToken)
    {
        using var relayLifetime = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
        Task clientToUpstream = clientStream.CopyToAsync(tunnel.Output, RelayBufferBytes, relayLifetime.Token);
        Task upstreamToClient = tunnel.Input.CopyToAsync(clientStream, RelayBufferBytes, relayLifetime.Token);
        try
        {
            var first = await Task.WhenAny(clientToUpstream, upstreamToClient).ConfigureAwait(false);
            if (first == clientToUpstream)
            {
                await clientToUpstream.ConfigureAwait(false);
                await tunnel.CompleteOutputAsync(serverToken).ConfigureAwait(false);
                await upstreamToClient.WaitAsync(HalfCloseDrainTimeout, serverToken).ConfigureAwait(false);
            }
            else
            {
                await upstreamToClient.ConfigureAwait(false);
                try { client.Client.Shutdown(SocketShutdown.Send); } catch (SocketException) { }
                catch (ObjectDisposedException) { }
                await clientToUpstream.WaitAsync(HalfCloseDrainTimeout, serverToken).ConfigureAwait(false);
                await tunnel.CompleteOutputAsync(serverToken).ConfigureAwait(false);
            }
        }
        finally
        {
            relayLifetime.Cancel();
            try { await tunnel.Output.DisposeAsync().ConfigureAwait(false); } catch { }
            try { await tunnel.Input.DisposeAsync().ConfigureAwait(false); } catch { }
            // Observe both tasks after closing the upstream stream so no unobserved copy task survives the client.
            try { await Task.WhenAll(clientToUpstream, upstreamToClient).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch { }
        }
    }

    private static async Task<(string Host, int Port)?> ReadTargetAsync(Stream stream, byte addressType, CancellationToken cancellationToken)
    {
        string host;
        switch (addressType)
        {
            case 0x01:
            {
                var bytes = new byte[4];
                await ReadExactlyAsync(stream, bytes, cancellationToken).ConfigureAwait(false);
                host = new IPAddress(bytes).ToString();
                break;
            }
            case 0x03:
            {
                var length = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
                if (length == 0) return null;
                var bytes = new byte[length];
                await ReadExactlyAsync(stream, bytes, cancellationToken).ConfigureAwait(false);
                if (bytes.Any(b => b < 0x21 || b > 0x7E)) return null;
                var raw = Encoding.ASCII.GetString(bytes).TrimEnd('.');
                try { host = new IdnMapping().GetAscii(raw).ToLowerInvariant(); }
                catch (ArgumentException) { return null; }
                if (!SettingsStore.IsValidHostname(host)) return null;
                break;
            }
            case 0x04:
            {
                var bytes = new byte[16];
                await ReadExactlyAsync(stream, bytes, cancellationToken).ConfigureAwait(false);
                host = new IPAddress(bytes).ToString();
                break;
            }
            default:
                return null;
        }
        var portBytes = new byte[2];
        await ReadExactlyAsync(stream, portBytes, cancellationToken).ConfigureAwait(false);
        var port = (portBytes[0] << 8) | portBytes[1];
        return (host, port);
    }

    private static bool IsValidUdpAssociationEndpoint(string requestedHost, int requestedPort, IPAddress controlPeer)
    {
        if (requestedPort is < 0 or > 65535) return false;
        if (!IPAddress.TryParse(requestedHost, out var requestedAddress)) return false;
        requestedAddress = NormalizeAddress(requestedAddress);
        controlPeer = NormalizeAddress(controlPeer);
        return requestedAddress.Equals(IPAddress.Any) || requestedAddress.Equals(IPAddress.IPv6Any) ||
               requestedAddress.Equals(controlPeer);
    }

    private static IPAddress NormalizeAddress(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    internal static bool IsLocalDestination(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("home.arpa", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase)) return true;

        if (!IPAddress.TryParse(host, out var address)) return false;
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.IsIPv6LinkLocal || address.IsIPv6Multicast) return true;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC) return true; // IPv6 unique-local (fc00::/7)
            if (address.IsIPv4MappedToIPv6)
                return IsLocalDestination(address.MapToIPv4().ToString());
            return false;
        }
        if (bytes.Length != 4) return false;
        return bytes[0] == 0 || bytes[0] == 10 || bytes[0] == 127 ||
               (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) || // shared address space / fake-DNS range
               (bytes[0] == 169 && bytes[1] == 254) ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 0) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 198 && bytes[1] is 18 or 19) || // benchmarking / synthetic mapping
               (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||
               (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) || bytes[0] >= 224;
    }

    private static async Task<int> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var one = new byte[1];
        await ReadExactlyAsync(stream, one, cancellationToken).ConfigureAwait(false);
        return one[0];
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("SOCKS5 client closed during a protocol message.");
            offset += read;
        }
    }

    private static Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken) =>
        ReadExactlyAsync(stream, buffer.AsMemory(), cancellationToken);

    private static ValueTask WriteReplyAsync(Stream stream, byte replyCode, CancellationToken cancellationToken)
    {
        byte[] reply = [0x05, replyCode, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        return stream.WriteAsync(reply, cancellationToken);
    }

    private static ValueTask WriteReplyAsync(Stream stream, byte replyCode, IPAddress bindAddress, int bindPort, CancellationToken cancellationToken)
    {
        var addressBytes = bindAddress.GetAddressBytes();
        var addressType = bindAddress.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x04 : (byte)0x01;
        var reply = new byte[4 + addressBytes.Length + 2];
        reply[0] = 0x05;
        reply[1] = replyCode;
        reply[2] = 0x00;
        reply[3] = addressType;
        addressBytes.CopyTo(reply, 4);
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(4 + addressBytes.Length, 2), checked((ushort)bindPort));
        return stream.WriteAsync(reply, cancellationToken);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeStarted) != 0) throw new ObjectDisposedException(nameof(WindowsSocks5ProxyServer));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        await StopAsync().ConfigureAwait(false);
        // The semaphore is intentionally left undisposed if any non-cooperative third-party stream survives StopAsync's timeout.
        if (_sessions.IsEmpty) _connectionSlots.Dispose();
    }
}
