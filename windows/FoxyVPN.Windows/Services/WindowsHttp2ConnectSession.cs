using System.IO;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;

namespace FoxyVPN.Windows.Services;

public sealed record WindowsUpstreamProxyOptions(string Type, string Host, int Port, string Username = "", string Password = "");

/// <summary>
/// Direct-TLS HTTP/2 CONNECT transport matching the Android client contract.
/// The TCP destination may be an edge IP, but TLS validation/SNI always use tlsServerHost.
/// Optional HTTP CONNECT or SOCKS5 upstream proxy chaining is completed before TLS; the tunnel itself
/// still carries destination TCP streams and does not turn unsupported edge UDP into a supported protocol.
/// </summary>
public sealed class WindowsHttp2ConnectSession : IUpstreamStreamProvider, IAsyncDisposable
{
    private const int DefaultWindowSize = 65_535;
    private const int DefaultFrameSize = 16_384;
    private const int MaximumInboundFrameSize = 16_384; // no larger inbound frame limit is advertised
    private const int MaximumHeaderBlockBytes = 64 * 1024;
    private static readonly byte[] ClientPreface = Encoding.ASCII.GetBytes("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n");

    private readonly TcpClient _tcp;
    private readonly SslStream _tls;
    private readonly Func<CancellationToken, Task<string>> _proxyPassProvider;
    private readonly Action<string, string> _log;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    // Stream IDs must be allocated and their HEADERS frames emitted in the same order.
    // Token refresh may be concurrent, but HTTP/2 stream initiation may not overtake.
    private readonly SemaphoreSlim _openStreamGate = new(1, 1);
    private readonly object _flowLock = new();
    private readonly object _pingLock = new();
    private readonly ConcurrentDictionary<int, H2StreamState> _streams = new();
    private readonly hpack.Decoder _headerDecoder = new(64 * 1024, 0);
    private Task? _readerTask;
    private Task? _keepAliveTask;
    private readonly TaskCompletionSource<bool> _peerSettingsReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private byte[]? _pendingPingPayload;
    private TaskCompletionSource<bool>? _pendingPingAck;
    private long _pingSequence;
    private TaskCompletionSource<bool> _flowChanged = NewSignal();
    private long _connectionSendWindow = DefaultWindowSize;
    private long _connectionReceiveWindow = DefaultWindowSize;
    private long _peerInitialStreamWindow = DefaultWindowSize;
    private int _peerMaxFrameSize = DefaultFrameSize;
    private int _peerMaxConcurrentStreams = 128;
    private long _nextStreamId = 1;
    private int _peerGoAwayLastStreamId = int.MaxValue;
    private volatile bool _goAwayReceived;
    private volatile bool _newStreamsDisabled;
    private bool _connected;
    private volatile bool _disposed;
    private int _disposeStarted;
    private int _transportAbortStarted;
    private int _continuationStreamId;
    private byte _continuationFlags;
    private MemoryStream? _headerBlock;

    private WindowsHttp2ConnectSession(
        TcpClient tcp,
        SslStream tls,
        Func<CancellationToken, Task<string>> proxyPassProvider,
        Action<string, string>? logger)
    {
        _tcp = tcp;
        _tls = tls;
        _proxyPassProvider = proxyPassProvider;
        _log = logger ?? ((_, _) => { });
    }

    public bool IsConnected => Volatile.Read(ref _connected) && !_disposed && !_lifetime.IsCancellationRequested;

    /// <summary>Resolved transport endpoint for a future route-bypass transaction; reading it changes no routes.</summary>
    public IPEndPoint? RemoteEndPoint
    {
        get
        {
            try { return _tcp.Client.RemoteEndPoint as IPEndPoint; }
            catch (ObjectDisposedException) { return null; }
            catch (SocketException) { return null; }
        }
    }

    /// <summary>Physical source endpoint selected before TUN routes are installed; used to identify the uplink for route bypass.</summary>
    public IPEndPoint? LocalEndPoint
    {
        get
        {
            try { return _tcp.Client.LocalEndPoint as IPEndPoint; }
            catch (ObjectDisposedException) { return null; }
            catch (SocketException) { return null; }
        }
    }

    /// <summary>Connects TCP to connectHost, but validates the TLS peer as tlsServerHost.</summary>
    public static async Task<WindowsHttp2ConnectSession> ConnectAsync(
        string connectHost,
        string tlsServerHost,
        int port,
        Func<CancellationToken, Task<string>> proxyPassProvider,
        Action<string, string>? logger = null,
        CancellationToken cancellationToken = default,
        WindowsUpstreamProxyOptions? upstreamProxy = null)
    {
        if (!IsValidHost(connectHost)) throw new ArgumentException("A valid TCP connect hostname/IP is required.", nameof(connectHost));
        if (!IsValidHost(tlsServerHost)) throw new ArgumentException("A valid TLS server hostname/IP is required.", nameof(tlsServerHost));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        ArgumentNullException.ThrowIfNull(proxyPassProvider);
        if (upstreamProxy is not null)
        {
            if (upstreamProxy.Type is not ("SOCKS5" or "HTTP")) throw new ArgumentException("The upstream proxy type must be SOCKS5 or HTTP.", nameof(upstreamProxy));
            if (!IsValidHost(upstreamProxy.Host)) throw new ArgumentException("The upstream proxy host is invalid.", nameof(upstreamProxy));
            if (upstreamProxy.Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(upstreamProxy), "The upstream proxy port is invalid.");
            ValidateUpstreamProxyCredentials(upstreamProxy);
        }

        var tcp = new TcpClient { NoDelay = true };
        SslStream? tls = null;
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            if (upstreamProxy is null)
            {
                await tcp.ConnectAsync(connectHost, port, connectTimeout.Token).ConfigureAwait(false);
            }
            else
            {
                await tcp.ConnectAsync(upstreamProxy.Host, upstreamProxy.Port, connectTimeout.Token).ConfigureAwait(false);
                await EstablishUpstreamProxyTunnelAsync(tcp.GetStream(), connectHost, port, upstreamProxy, connectTimeout.Token).ConfigureAwait(false);
            }
            tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            using var tlsTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            tlsTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = tlsServerHost,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http2 },
                CertificateRevocationCheckMode = X509RevocationMode.Online,
            }, tlsTimeout.Token).ConfigureAwait(false);
            if (tls.NegotiatedApplicationProtocol != SslApplicationProtocol.Http2)
                throw new IOException("The upstream TLS server did not negotiate ALPN h2. The VPN transport will not fall back to HTTP/1.1.");

            var session = new WindowsHttp2ConnectSession(tcp, tls, proxyPassProvider, logger);
            try
            {
                await session.StartProtocolAsync(cancellationToken).ConfigureAwait(false);
                return session;
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            if (tls is not null) await tls.DisposeAsync().ConfigureAwait(false);
            tcp.Dispose();
            throw;
        }
    }

    internal static void ValidateUpstreamProxyCredentials(WindowsUpstreamProxyOptions proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        var hasUsername = !string.IsNullOrWhiteSpace(proxy.Username);
        var hasPassword = !string.IsNullOrEmpty(proxy.Password);
        var usernameBytes = Encoding.UTF8.GetByteCount(proxy.Username ?? string.Empty);
        var passwordBytes = Encoding.UTF8.GetByteCount(proxy.Password ?? string.Empty);

        if (proxy.Type == "SOCKS5")
        {
            if (hasUsername != hasPassword)
                throw new ArgumentException("SOCKS5 authentication requires both a non-empty username and a non-empty password.", nameof(proxy));
            if (hasUsername && (usernameBytes is < 1 or > 255 || passwordBytes is < 1 or > 255))
                throw new ArgumentException("SOCKS5 username and password must each contain 1..255 UTF-8 bytes.", nameof(proxy));
            return;
        }

        if (proxy.Type == "HTTP")
        {
            if (!hasUsername && hasPassword)
                throw new ArgumentException("An HTTP upstream proxy password cannot be configured without a username.", nameof(proxy));
            // Bound encoded Basic credentials before opening the TCP connection.
            if (usernameBytes > 1024 || passwordBytes > 1024)
                throw new ArgumentException("HTTP upstream proxy credentials may not exceed 1024 UTF-8 bytes per field.", nameof(proxy));
            return;
        }

        throw new ArgumentException("The upstream proxy type must be SOCKS5 or HTTP.", nameof(proxy));
    }

    internal static async Task EstablishUpstreamProxyTunnelAsync(
        Stream stream,
        string targetHost,
        int targetPort,
        WindowsUpstreamProxyOptions proxy,
        CancellationToken cancellationToken)
    {
        if (proxy.Type == "SOCKS5")
        {
            await EstablishSocks5ProxyTunnelAsync(stream, targetHost, targetPort, proxy, cancellationToken).ConfigureAwait(false);
            return;
        }
        await EstablishHttpConnectProxyTunnelAsync(stream, targetHost, targetPort, proxy, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task EstablishHttpConnectProxyTunnelAsync(
        Stream stream,
        string targetHost,
        int targetPort,
        WindowsUpstreamProxyOptions proxy,
        CancellationToken cancellationToken)
    {
        var authority = FormatAuthority(targetHost, targetPort);
        var builder = new StringBuilder()
            .Append("CONNECT ").Append(authority).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(authority).Append("\r\n")
            .Append("Proxy-Connection: Keep-Alive\r\n");
        if (!string.IsNullOrWhiteSpace(proxy.Username))
        {
            var userPass = Encoding.UTF8.GetBytes(proxy.Username + ":" + proxy.Password);
            try { builder.Append("Proxy-Authorization: Basic ").Append(Convert.ToBase64String(userPass)).Append("\r\n"); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(userPass); }
        }
        builder.Append("\r\n");
        var request = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        const int maxHeaderBytes = 16 * 1024;
        var header = new List<byte>(1024);
        var one = new byte[1];
        while (header.Count < maxHeaderBytes)
        {
            await stream.ReadExactlyAsync(one, cancellationToken).ConfigureAwait(false);
            header.Add(one[0]);
            var count = header.Count;
            if (count >= 4 && header[count - 4] == 13 && header[count - 3] == 10 && header[count - 2] == 13 && header[count - 1] == 10)
            {
                var text = Encoding.ASCII.GetString(header.ToArray());
                var statusLine = text.Split("\r\n", 2, StringSplitOptions.None)[0];
                var status = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (status.Length < 2 || status[0] is not ("HTTP/1.0" or "HTTP/1.1") || status[1] != "200")
                    throw new IOException("The configured HTTP upstream proxy did not accept CONNECT to the FoxyVPN edge.");
                return;
            }
        }
        throw new IOException("The configured HTTP upstream proxy returned headers larger than 16 KiB.");
    }

    internal static async Task EstablishSocks5ProxyTunnelAsync(
        Stream stream,
        string targetHost,
        int targetPort,
        WindowsUpstreamProxyOptions proxy,
        CancellationToken cancellationToken)
    {
        var useCredentials = !string.IsNullOrWhiteSpace(proxy.Username);
        byte[] greeting = useCredentials ? [0x05, 0x01, 0x02] : [0x05, 0x01, 0x00];
        await stream.WriteAsync(greeting, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        var method = new byte[2];
        await stream.ReadExactlyAsync(method, cancellationToken).ConfigureAwait(false);
        if (method[0] != 0x05 || method[1] != (useCredentials ? 0x02 : 0x00))
            throw new IOException("The configured SOCKS5 proxy did not accept the required authentication method.");

        if (useCredentials)
        {
            var username = Encoding.UTF8.GetBytes(proxy.Username);
            var password = Encoding.UTF8.GetBytes(proxy.Password ?? string.Empty);
            byte[]? auth = null;
            try
            {
                // RFC 1929 requires both the username and password to contain 1..255 octets.
                if (username.Length is < 1 or > 255 || password.Length is < 1 or > 255)
                    throw new InvalidOperationException("SOCKS5 username/password must each contain 1..255 UTF-8 bytes.");
                auth = new byte[3 + username.Length + password.Length];
                auth[0] = 0x01;
                auth[1] = checked((byte)username.Length);
                username.CopyTo(auth, 2);
                auth[2 + username.Length] = checked((byte)password.Length);
                password.CopyTo(auth, 3 + username.Length);
                await stream.WriteAsync(auth, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(username);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(password);
                if (auth is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(auth);
            }

            var authReply = new byte[2];
            await stream.ReadExactlyAsync(authReply, cancellationToken).ConfigureAwait(false);
            if (authReply[0] != 0x01 || authReply[1] != 0x00)
                throw new IOException("The configured SOCKS5 proxy rejected username/password authentication.");
        }

        byte[] targetAddress;
        byte addressType;
        if (IPAddress.TryParse(targetHost, out var ip))
        {
            targetAddress = ip.GetAddressBytes();
            addressType = ip.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x04 : (byte)0x01;
        }
        else
        {
            var asciiHost = new IdnMapping().GetAscii(targetHost.TrimEnd('.'));
            targetAddress = Encoding.ASCII.GetBytes(asciiHost);
            if (targetAddress.Length is < 1 or > 255) throw new ArgumentException("The SOCKS5 CONNECT host is too long.", nameof(targetHost));
            addressType = 0x03;
        }

        var requestLength = 4 + (addressType == 0x03 ? 1 : 0) + targetAddress.Length + 2;
        var request = new byte[requestLength];
        request[0] = 0x05;
        request[1] = 0x01;
        request[2] = 0x00;
        request[3] = addressType;
        var offset = 4;
        if (addressType == 0x03) request[offset++] = checked((byte)targetAddress.Length);
        targetAddress.CopyTo(request, offset);
        offset += targetAddress.Length;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(offset, 2), checked((ushort)targetPort));
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var reply = new byte[4];
        await stream.ReadExactlyAsync(reply, cancellationToken).ConfigureAwait(false);
        if (reply[0] != 0x05 || reply[2] != 0x00 || reply[1] != 0x00)
            throw new IOException($"The configured SOCKS5 proxy rejected CONNECT to the FoxyVPN edge (status 0x{reply[1]:X2}).");
        int addressLength = reply[3] switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => await ReadSocksDomainLengthAsync(stream, cancellationToken).ConfigureAwait(false),
            _ => throw new IOException("The SOCKS5 proxy returned an invalid bind address type."),
        };
        var trailing = new byte[addressLength + 2];
        await stream.ReadExactlyAsync(trailing, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadSocksDomainLengthAsync(Stream stream, CancellationToken cancellationToken)
    {
        var value = new byte[1];
        await stream.ReadExactlyAsync(value, cancellationToken).ConfigureAwait(false);
        if (value[0] == 0) throw new IOException("The SOCKS5 proxy returned an empty bind domain.");
        return value[0];
    }

    private async Task StartProtocolAsync(CancellationToken cancellationToken)
    {
        await _tls.WriteAsync(ClientPreface, cancellationToken).ConfigureAwait(false);
        // Limit the upstream's HPACK dynamic table to zero. The request headers below also never index
        // the bearer authorization field, and use only literal/static-table representations.
        var settings = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(settings.AsSpan(0, 2), 0x1); // SETTINGS_HEADER_TABLE_SIZE
        BinaryPrimitives.WriteUInt32BigEndian(settings.AsSpan(2, 4), 0);
        BinaryPrimitives.WriteUInt16BigEndian(settings.AsSpan(6, 2), 0x2); // SETTINGS_ENABLE_PUSH
        BinaryPrimitives.WriteUInt32BigEndian(settings.AsSpan(8, 4), 0);
        await WriteFrameAsync(FrameSettings, 0, 0, settings, cancellationToken).ConfigureAwait(false);
        _readerTask = Task.Run(() => ReaderLoopAsync(_lifetime.Token), CancellationToken.None);
        // Do not report this transport connected until the peer has sent its initial SETTINGS frame.
        await _peerSettingsReceived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        if (_readerTask.IsCompleted || _lifetime.IsCancellationRequested || _disposed)
            throw new IOException("The HTTP/2 upstream closed during its initial SETTINGS exchange.");
        Volatile.Write(ref _connected, true);
        // Close the small race where the reader exits between the preceding check and the connected write.
        if (_readerTask.IsCompleted || _lifetime.IsCancellationRequested || _disposed)
        {
            Volatile.Write(ref _connected, false);
            throw new IOException("The HTTP/2 upstream closed while the session was being initialized.");
        }
        _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(_lifetime.Token), CancellationToken.None);
        _log("INFO", "TLS/ALPN h2 session established and initial peer SETTINGS received.");
    }

    public async ValueTask<IUpstreamDuplexStream> OpenConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (!IsConnected) throw new IOException("The HTTP/2 upstream session is not connected.");
        if (!IsValidHost(host)) throw new ArgumentException("Invalid CONNECT destination hostname/IP.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (_goAwayReceived || _newStreamsDisabled)
            throw new IOException("The upstream session is draining and is no longer accepting new streams.");

        string token;
        using (var tokenTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
        {
            tokenTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            token = await _proxyPassProvider(tokenTimeout.Token).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16 * 1024 || token.Any(char.IsControl) || token.Any(ch => ch > 0x7F))
            throw new InvalidOperationException("Guardian did not provide a valid proxy pass token.");

        var authority = FormatAuthority(host, port);
        using var openTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        openTimeout.CancelAfter(TimeSpan.FromSeconds(20));

        H2StreamState? state = null;
        var headersFrameSent = false;
        // Token acquisition is intentionally outside this gate. Once a stream ID is allocated, however,
        // its HEADERS frame must be sent before a later stream ID is initiated on the same connection.
        await _openStreamGate.WaitAsync(openTimeout.Token).ConfigureAwait(false);
        try
        {
            if (!IsConnected) throw new IOException("The HTTP/2 session closed while opening a stream.");
            if (_goAwayReceived || _newStreamsDisabled)
                throw new IOException("The upstream session is draining and is no longer accepting new streams.");

            var streamId = AllocateStreamId();
            lock (_flowLock)
            {
                if (_disposed || !IsConnected) throw new IOException("The HTTP/2 session closed while opening a stream.");
                if (_goAwayReceived || _newStreamsDisabled) throw new IOException("The upstream session is draining.");
                if (_streams.Count >= _peerMaxConcurrentStreams)
                    throw new IOException($"The upstream concurrent stream limit ({_peerMaxConcurrentStreams}) has been reached.");
                state = new H2StreamState(streamId, _peerInitialStreamWindow) { Authority = authority };
                if (!_streams.TryAdd(streamId, state)) throw new IOException("Could not register the HTTP/2 stream.");
            }

            var streamState = state ?? throw new InvalidOperationException("HTTP/2 stream registration failed unexpectedly.");
            var headers = Http2ConnectHeaderEncoder.Encode(authority, token);
            await WriteFrameAsync(FrameHeaders, FlagEndHeaders, streamState.Id, headers, openTimeout.Token).ConfigureAwait(false);
            headersFrameSent = true;
        }
        catch
        {
            if (state is not null)
            {
                _streams.TryRemove(state.Id, out _);
                state.Inbound.Writer.TryComplete();
                state.ConnectResponse.TrySetCanceled();
                if (headersFrameSent && IsConnected)
                {
                    try { await WriteRstStreamAsync(state.Id, ErrorCancel, CancellationToken.None).ConfigureAwait(false); } catch { }
                }
            }
            throw;
        }
        finally
        {
            _openStreamGate.Release();
        }

        try
        {
            var accepted = await state!.ConnectResponse.Task.WaitAsync(openTimeout.Token).ConfigureAwait(false);
            if (!accepted) throw new IOException($"The upstream rejected HTTP/2 CONNECT to {authority}.");
            return new H2DuplexStream(this, state);
        }
        catch
        {
            _streams.TryRemove(state!.Id, out _);
            state.Inbound.Writer.TryComplete();
            if (headersFrameSent && IsConnected)
            {
                try { await WriteRstStreamAsync(state.Id, ErrorCancel, CancellationToken.None).ConfigureAwait(false); } catch { }
            }
            throw;
        }
    }

    private int AllocateStreamId()
    {
        var allocated = Interlocked.Add(ref _nextStreamId, 2) - 2;
        if (allocated <= 0 || allocated > 0x7FFFFFFF)
        {
            _newStreamsDisabled = true;
            _log("WARN", "HTTP/2 client stream identifiers are exhausted; existing streams may drain but no new streams will be opened.");
            throw new IOException("HTTP/2 stream IDs are exhausted; reconnect the upstream session.");
        }
        return checked((int)allocated);
    }

    private async Task ReaderLoopAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            var firstPeerFrame = true;
            while (!cancellationToken.IsCancellationRequested)
            {
                var header = new byte[9];
                await _tls.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
                var length = (header[0] << 16) | (header[1] << 8) | header[2];
                if (length > MaximumInboundFrameSize) throw new IOException($"HTTP/2 peer sent an oversized frame ({length} bytes).");
                var type = header[3];
                var flags = header[4];
                var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5, 4)) & 0x7FFFFFFF);
                var payload = new byte[length];
                if (length > 0) await _tls.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
                if (firstPeerFrame)
                {
                    if (type != FrameSettings || streamId != 0 || (flags & FlagAck) != 0)
                        throw new IOException("HTTP/2 protocol error: the peer's first frame must be a non-ACK SETTINGS frame on stream 0.");
                    firstPeerFrame = false;
                }

                if (_continuationStreamId != 0 && type != FrameContinuation)
                    throw new IOException("HTTP/2 protocol error: a CONTINUATION sequence was interrupted.");

                switch (type)
                {
                    case FrameData: await ProcessDataFrameAsync(streamId, flags, payload, cancellationToken).ConfigureAwait(false); break;
                    case FrameHeaders: await ProcessHeadersFrameAsync(streamId, flags, payload, cancellationToken).ConfigureAwait(false); break;
                    case FrameContinuation: await ProcessContinuationFrameAsync(streamId, flags, payload, cancellationToken).ConfigureAwait(false); break;
                    case FrameSettings: await ProcessSettingsFrameAsync(streamId, flags, payload, cancellationToken).ConfigureAwait(false); break;
                    case FrameWindowUpdate: ProcessWindowUpdateFrame(streamId, payload); break;
                    case FrameRstStream: ProcessRstStreamFrame(streamId, payload); break;
                    case FrameGoAway: ProcessGoAwayFrame(streamId, payload); break;
                    case FramePing: await ProcessPingFrameAsync(streamId, flags, payload, cancellationToken).ConfigureAwait(false); break;
                    case FramePriority: ValidatePriorityFrame(streamId, payload); break;
                    case FramePushPromise: throw new IOException("HTTP/2 server push is disabled; the upstream sent an unexpected PUSH_PROMISE.");
                    default: /* Unknown frame types are ignored as required by HTTP/2. */ break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested || _disposed) { }
        catch (IOException ex) { failure = ex; }
        catch (Exception ex) { failure = new IOException("The HTTP/2 upstream reader failed.", ex); }
        finally
        {
            if (!_peerSettingsReceived.Task.IsCompleted)
            {
                if (failure is not null) _peerSettingsReceived.TrySetException(failure);
                else _peerSettingsReceived.TrySetCanceled();
            }
            Volatile.Write(ref _connected, false);
            if (failure is not null && !_disposed)
                AbortTransport(failure, "HTTP/2 reader failed");
            else if (failure is not null)
                FailAllStreams(failure);
        }
    }

    private async Task ProcessSettingsFrameAsync(int streamId, byte flags, byte[] payload, CancellationToken cancellationToken)
    {
        if (streamId != 0) throw new IOException("HTTP/2 protocol error: SETTINGS must use stream 0.");
        if ((flags & FlagAck) != 0)
        {
            if (payload.Length != 0) throw new IOException("HTTP/2 protocol error: SETTINGS ACK has a payload.");
            return;
        }
        if (payload.Length % 6 != 0) throw new IOException("HTTP/2 protocol error: malformed SETTINGS payload.");

        lock (_flowLock)
        {
            for (var offset = 0; offset < payload.Length; offset += 6)
            {
                var id = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset, 2));
                var value = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset + 2, 4));
                switch (id)
                {
                    case 0x1: // HEADER_TABLE_SIZE for our encoder; outbound header blocks do not use a dynamic table.
                        break;
                    case 0x2: // SETTINGS_ENABLE_PUSH may be sent by a client, never by a server.
                        throw new IOException("HTTP/2 protocol error: the server must not send SETTINGS_ENABLE_PUSH.");
                    case 0x3:
                        _peerMaxConcurrentStreams = (int)Math.Min(value, (uint)int.MaxValue);
                        break;
                    case 0x4:
                        if (value > 0x7FFFFFFF) throw new IOException("HTTP/2 protocol error: invalid INITIAL_WINDOW_SIZE.");
                        var delta = (long)value - _peerInitialStreamWindow;
                        foreach (var stream in _streams.Values)
                        {
                            var updated = stream.SendWindow + delta;
                            if (updated > 0x7FFFFFFF || updated < -0x7FFFFFFF)
                                throw new IOException("HTTP/2 protocol error: stream flow-control window overflow.");
                            stream.SendWindow = updated;
                        }
                        _peerInitialStreamWindow = value;
                        PulseFlowWaitersLocked();
                        break;
                    case 0x5:
                        if (value is < 16_384 or > 16_777_215) throw new IOException("HTTP/2 protocol error: invalid MAX_FRAME_SIZE.");
                        _peerMaxFrameSize = (int)value;
                        break;
                    default: break;
                }
            }
        }
        await WriteFrameAsync(FrameSettings, FlagAck, 0, Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
        _peerSettingsReceived.TrySetResult(true);
    }

    private void ProcessWindowUpdateFrame(int streamId, byte[] payload)
    {
        if (payload.Length != 4) throw new IOException("HTTP/2 protocol error: WINDOW_UPDATE payload must be 4 bytes.");
        var increment = (long)(BinaryPrimitives.ReadUInt32BigEndian(payload) & 0x7FFFFFFF);
        if (increment == 0) throw new IOException("HTTP/2 protocol error: WINDOW_UPDATE increment is zero.");
        lock (_flowLock)
        {
            if (streamId == 0)
            {
                if (_connectionSendWindow + increment > 0x7FFFFFFF)
                    throw new IOException("HTTP/2 protocol error: connection flow-control window overflow.");
                _connectionSendWindow += increment;
                PulseFlowWaitersLocked();
            }
            else if (_streams.TryGetValue(streamId, out var stream))
            {
                if (stream.SendWindow + increment > 0x7FFFFFFF)
                    throw new IOException("HTTP/2 protocol error: stream flow-control window overflow.");
                stream.SendWindow += increment;
                PulseFlowWaitersLocked();
            }
            else if (Http2ProtocolRules.IsIdleClientStream(streamId, Volatile.Read(ref _nextStreamId)))
            {
                throw new IOException("HTTP/2 protocol error: WINDOW_UPDATE was received for an idle stream.");
            }
            // WINDOW_UPDATE on a closed client stream is ignored.
        }
    }

    private void ProcessRstStreamFrame(int streamId, byte[] payload)
    {
        if (streamId == 0 || payload.Length != 4) throw new IOException("HTTP/2 protocol error: malformed RST_STREAM.");
        if (!_streams.TryGetValue(streamId, out var state))
        {
            // Server-initiated streams are unavailable because PUSH_PROMISE is rejected. A stream ID
            // at or beyond the next client ID has never been opened and is therefore idle.
            if (Http2ProtocolRules.IsIdleClientStream(streamId, Volatile.Read(ref _nextStreamId)))
                throw new IOException("HTTP/2 protocol error: RST_STREAM was received for an idle stream.");
            return; // RST_STREAM on a previously closed client stream can be ignored.
        }
        var code = BinaryPrimitives.ReadUInt32BigEndian(payload);
        var error = new IOException($"The upstream reset HTTP/2 stream {streamId} (error={code}).");
        state.ConnectResponse.TrySetException(error);
        state.Inbound.Writer.TryComplete(error);
        state.RemoteEnded = true;
        state.LocalEnded = true;
        PulseFlowWaiters();
    }

    private void ProcessGoAwayFrame(int streamId, byte[] payload)
    {
        if (streamId != 0 || payload.Length < 8) throw new IOException("HTTP/2 protocol error: malformed GOAWAY.");
        var lastStreamId = Http2ProtocolRules.ParseGoAwayLastStreamId(_peerGoAwayLastStreamId, payload);
        var errorCode = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(4, 4));
        _peerGoAwayLastStreamId = lastStreamId;
        _goAwayReceived = true;
        foreach (var state in _streams.Values.Where(s => s.Id > lastStreamId))
        {
            var error = new IOException($"HTTP/2 GOAWAY (error={errorCode}) means stream {state.Id} was not processed.");
            state.ConnectResponse.TrySetException(error);
            state.Inbound.Writer.TryComplete(error);
            state.RemoteEnded = true;
            state.LocalEnded = true;
        }
        PulseFlowWaiters();
        _log("WARN", $"HTTP/2 GOAWAY received; last stream={lastStreamId}, error={errorCode}.");
    }

    private async Task ProcessPingFrameAsync(int streamId, byte flags, byte[] payload, CancellationToken cancellationToken)
    {
        if (streamId != 0 || payload.Length != 8) throw new IOException("HTTP/2 protocol error: malformed PING.");
        if ((flags & FlagAck) == 0)
        {
            await WriteFrameAsync(FramePing, FlagAck, 0, payload, cancellationToken).ConfigureAwait(false);
            return;
        }
        TaskCompletionSource<bool>? waiter = null;
        lock (_pingLock)
        {
            if (_pendingPingPayload is not null && payload.AsSpan().SequenceEqual(_pendingPingPayload))
                waiter = _pendingPingAck;
        }
        waiter?.TrySetResult(true);
    }

    private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var payload = new byte[8];
                BinaryPrimitives.WriteInt64BigEndian(payload, Interlocked.Increment(ref _pingSequence));
                var ack = NewSignal();
                lock (_pingLock)
                {
                    _pendingPingPayload = payload;
                    _pendingPingAck = ack;
                }
                try
                {
                    await WriteFrameAsync(FramePing, 0, 0, payload, cancellationToken).ConfigureAwait(false);
                    await ack.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException ex)
                {
                    throw new IOException("The HTTP/2 upstream did not acknowledge a keep-alive PING within 10 seconds.", ex);
                }
                finally
                {
                    lock (_pingLock)
                    {
                        if (ReferenceEquals(_pendingPingAck, ack))
                        {
                            _pendingPingPayload = null;
                            _pendingPingAck = null;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AbortTransport(ex, "HTTP/2 keep-alive failed");
        }
    }

    private static void ValidatePriorityFrame(int streamId, byte[] payload)
    {
        if (streamId == 0 || payload.Length != 5)
            throw new IOException("HTTP/2 protocol error: malformed PRIORITY frame.");
    }

    private async Task ProcessHeadersFrameAsync(int streamId, byte flags, byte[] payload, CancellationToken cancellationToken)
    {
        if (streamId == 0) throw new IOException("HTTP/2 protocol error: HEADERS requires a stream ID.");
        if (payload.Length == 0 && (flags & FlagPadded) != 0)
            throw new IOException("HTTP/2 protocol error: padded HEADERS frame is empty.");
        var offset = 0;
        var padLength = 0;
        if ((flags & FlagPadded) != 0) padLength = payload[offset++];
        if ((flags & FlagPriority) != 0)
        {
            if (payload.Length - offset < 5) throw new IOException("HTTP/2 protocol error: truncated HEADERS priority fields.");
            offset += 5;
        }
        if (padLength > payload.Length - offset) throw new IOException("HTTP/2 protocol error: invalid HEADERS padding.");
        var fragmentLength = payload.Length - offset - padLength;
        _headerBlock = new MemoryStream(Math.Min(fragmentLength + 256, MaximumHeaderBlockBytes));
        _headerBlock.Write(payload, offset, fragmentLength);
        _continuationStreamId = streamId;
        _continuationFlags = flags;
        if ((flags & FlagEndHeaders) != 0) await FinishHeaderBlockAsync(cancellationToken).ConfigureAwait(false);
        else if (_headerBlock.Length > MaximumHeaderBlockBytes) throw new IOException("HTTP/2 response header block is too large.");
    }

    private async Task ProcessContinuationFrameAsync(int streamId, byte flags, byte[] payload, CancellationToken cancellationToken)
    {
        if (_continuationStreamId == 0 || streamId != _continuationStreamId)
            throw new IOException("HTTP/2 protocol error: unexpected CONTINUATION stream ID.");
        if (_headerBlock is null) throw new IOException("HTTP/2 protocol error: missing header block buffer.");
        if (_headerBlock.Length + payload.Length > MaximumHeaderBlockBytes)
            throw new IOException("HTTP/2 response header block is too large.");
        _headerBlock.Write(payload, 0, payload.Length);
        if ((flags & FlagEndHeaders) != 0) await FinishHeaderBlockAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task FinishHeaderBlockAsync(CancellationToken cancellationToken)
    {
        var streamId = _continuationStreamId;
        var flags = _continuationFlags;
        var encoded = _headerBlock?.ToArray() ?? Array.Empty<byte>();
        _headerBlock?.Dispose();
        _headerBlock = null;
        _continuationStreamId = 0;
        _continuationFlags = 0;

        var listener = new HeaderCollector();
        using (var reader = new BinaryReader(new MemoryStream(encoded, writable: false), Encoding.UTF8, leaveOpen: false))
        {
            _headerDecoder.Decode(reader, listener);
            _headerDecoder.EndHeaderBlock();
        }
        if (!_streams.TryGetValue(streamId, out var state))
        {
            if (Http2ProtocolRules.IsIdleClientStream(streamId, Volatile.Read(ref _nextStreamId)))
                throw new IOException("HTTP/2 protocol error: HEADERS was received for an idle stream.");
            try { await WriteRstStreamAsync(streamId, ErrorStreamClosed, cancellationToken).ConfigureAwait(false); } catch { }
            return;
        }
        if (state.RemoteEnded)
            throw new IOException("HTTP/2 protocol error: HEADERS arrived after the remote direction ended.");

        var status = Http2ProtocolRules.ParseResponseStatus(listener.Headers.GetValueOrDefault(":status"));
        if (!state.ConnectResponse.Task.IsCompleted)
        {
            if (Http2ProtocolRules.IsInformationalResponseStatus(status))
            {
                // HTTP/2 permits one or more informational HEADERS blocks before the final response.
                // Do not resolve CONNECT yet; its 2xx acceptance/rejection comes from the final block.
                if ((flags & FlagEndStream) != 0)
                    throw new IOException("HTTP/2 protocol error: an informational response ended the stream.");
                return;
            }

            if (status is >= 200 and <= 299)
                state.ConnectResponse.TrySetResult(true);
            else
            {
                var rejection = new IOException($"The VPN upstream rejected CONNECT to {state.Authority ?? "the destination"} with HTTP/2 status {(status == 0 ? "<missing>" : status.ToString(CultureInfo.InvariantCulture))}.");
                state.ConnectResponse.TrySetException(rejection);
                // OpenConnectAsync owns stream cleanup and emits a single RST_STREAM. Sending another
                // reset here could duplicate the frame while that caller handles the rejection.
            }
        }
        if ((flags & FlagEndStream) != 0) MarkRemoteEnded(state);
    }

    private async Task ProcessDataFrameAsync(int streamId, byte flags, byte[] payload, CancellationToken cancellationToken)
    {
        if (streamId == 0) throw new IOException("HTTP/2 protocol error: DATA requires a stream ID.");
        var offset = 0;
        var padLength = 0;
        if ((flags & FlagPadded) != 0)
        {
            if (payload.Length == 0) throw new IOException("HTTP/2 protocol error: padded DATA frame is empty.");
            padLength = payload[offset++];
        }
        if (padLength > payload.Length - offset) throw new IOException("HTTP/2 protocol error: invalid DATA padding.");
        var dataLength = payload.Length - offset - padLength;

        H2StreamState? state = null;
        lock (_flowLock)
        {
            var flowLength = payload.Length;
            if (_connectionReceiveWindow - flowLength < 0)
                throw new IOException("HTTP/2 protocol error: connection receive window was exceeded.");
            _connectionReceiveWindow -= flowLength;
            if (_streams.TryGetValue(streamId, out state))
            {
                if (state.RemoteReceiveWindow - flowLength < 0)
                    throw new IOException("HTTP/2 protocol error: stream receive window was exceeded.");
                state.RemoteReceiveWindow -= flowLength;
            }
        }
        if (state is null)
        {
            if (Http2ProtocolRules.IsIdleClientStream(streamId, Volatile.Read(ref _nextStreamId)))
                throw new IOException("HTTP/2 protocol error: DATA was received for an idle stream.");
            // DATA on an already closed client stream is a stream error. Credit the connection-level
            // bytes so other live streams are not starved, then explicitly close the stale stream.
            if (payload.Length > 0) await CreditConnectionWindowAsync(payload.Length, cancellationToken).ConfigureAwait(false);
            try { await WriteRstStreamAsync(streamId, ErrorStreamClosed, cancellationToken).ConfigureAwait(false); } catch { }
            return;
        }
        if (state.RemoteEnded)
            throw new IOException("HTTP/2 protocol error: DATA arrived after END_STREAM for this stream.");
        if (offset + padLength > 0) await CreditReceiveWindowAsync(streamId, offset + padLength, cancellationToken).ConfigureAwait(false);
        if (!state.ConnectResponse.Task.IsCompletedSuccessfully)
            throw new IOException("HTTP/2 protocol error: DATA arrived before CONNECT was accepted.");
        if (dataLength > 0)
        {
            var data = new byte[dataLength];
            Buffer.BlockCopy(payload, offset, data, 0, dataLength);
            if (!state.Inbound.Writer.TryWrite(data))
                throw new IOException("HTTP/2 inbound stream buffer has already closed.");
        }
        if ((flags & FlagEndStream) != 0) MarkRemoteEnded(state);
    }

    private void MarkRemoteEnded(H2StreamState state)
    {
        if (state.RemoteEnded)
            throw new IOException("HTTP/2 protocol error: remote stream direction ended more than once.");
        state.RemoteEnded = true;
        state.Inbound.Writer.TryComplete();
    }

    private async Task CreditReceiveWindowAsync(int streamId, int bytes, CancellationToken cancellationToken)
    {
        if (bytes <= 0) return;
        await CreditConnectionWindowAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (!_streams.TryGetValue(streamId, out var state)) return;
        lock (_flowLock)
        {
            if (state.RemoteReceiveWindow + bytes > 0x7FFFFFFF)
                throw new IOException("HTTP/2 protocol error: receive stream window overflow.");
            state.RemoteReceiveWindow += bytes;
        }
        await WriteWindowUpdateAsync(streamId, bytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task CreditConnectionWindowAsync(int bytes, CancellationToken cancellationToken)
    {
        if (bytes <= 0) return;
        lock (_flowLock)
        {
            if (_connectionReceiveWindow + bytes > 0x7FFFFFFF)
                throw new IOException("HTTP/2 protocol error: receive connection window overflow.");
            _connectionReceiveWindow += bytes;
        }
        await WriteWindowUpdateAsync(0, bytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteWindowUpdateAsync(int streamId, int bytes, CancellationToken cancellationToken)
    {
        if (bytes <= 0) return;
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)bytes & 0x7FFFFFFF);
        await WriteFrameAsync(FrameWindowUpdate, 0, streamId, payload, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SendDataAsync(H2StreamState state, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < data.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count;
            while (true)
            {
                Task waitTask;
                lock (_flowLock)
                {
                    // HTTP/2 streams are half-duplex by direction: a remote END_STREAM closes only the
                    // upstream-to-client direction. The client may still send bytes until local END_STREAM.
                    if (!IsConnected || state.LocalEnded) throw new IOException("HTTP/2 tunnel stream is closed for writing.");
                    var available = Math.Min(_connectionSendWindow, state.SendWindow);
                    if (available > 0)
                    {
                        count = (int)Math.Min(Math.Min(available, _peerMaxFrameSize), data.Length - offset);
                        _connectionSendWindow -= count;
                        state.SendWindow -= count;
                        break;
                    }
                    waitTask = _flowChanged.Task;
                }
                await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            await WriteFrameAsync(FrameData, 0, state.Id, data.Slice(offset, count), cancellationToken).ConfigureAwait(false);
            offset += count;
        }
    }

    private async Task CompleteOutputAsync(H2StreamState state, CancellationToken cancellationToken)
    {
        lock (_flowLock)
        {
            if (state.LocalEnded) return;
            state.LocalEnded = true;
        }
        await WriteFrameAsync(FrameData, FlagEndStream, state.Id, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteRstStreamAsync(int streamId, uint errorCode, CancellationToken cancellationToken)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, errorCode);
        await WriteFrameAsync(FrameRstStream, 0, streamId, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteFrameAsync(byte type, byte flags, int streamId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length > 0xFFFFFF) throw new ArgumentOutOfRangeException(nameof(payload));
        if (streamId < 0 || streamId > 0x7FFFFFFF) throw new ArgumentOutOfRangeException(nameof(streamId));
        cancellationToken.ThrowIfCancellationRequested();
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) throw new IOException("HTTP/2 session is closed.");
            if (_lifetime.IsCancellationRequested) throw new IOException("HTTP/2 session is shutting down.");

            var header = new byte[9];
            header[0] = (byte)(payload.Length >> 16);
            header[1] = (byte)(payload.Length >> 8);
            header[2] = (byte)payload.Length;
            header[3] = type;
            header[4] = flags;
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(5, 4), (uint)streamId & 0x7FFFFFFF);
            try
            {
                // Once the first byte of a frame is emitted, a per-request cancellation token must not
                // interrupt the header/payload pair and corrupt the shared HTTP/2 connection. Shutdown
                // still cancels writes via the session lifetime token; a bounded frame timeout aborts
                // the whole session if the transport stalls after a frame has started.
                using var frameWriteTimeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                frameWriteTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                await _tls.WriteAsync(header, frameWriteTimeout.Token).ConfigureAwait(false);
                if (!payload.IsEmpty) await _tls.WriteAsync(payload, frameWriteTimeout.Token).ConfigureAwait(false);
                await _tls.FlushAsync(frameWriteTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!_disposed && !_lifetime.IsCancellationRequested)
                    AbortTransport(ex, "HTTP/2 frame write failed; the connection cannot be reused");
                throw;
            }
        }
        finally { _writeGate.Release(); }
    }

    private void AbortTransport(Exception error, string reason)
    {
        Volatile.Write(ref _connected, false);
        if (Interlocked.Exchange(ref _transportAbortStarted, 1) != 0) return;
        if (!_disposed) _log("WARN", $"{reason}: {error.GetType().Name}. The HTTP/2 session is being closed.");
        try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
        FailAllStreams(error);
        try { _tls.Dispose(); } catch { }
        try { _tcp.Dispose(); } catch { }
    }

    private void FailAllStreams(Exception error)
    {
        foreach (var stream in _streams.Values)
        {
            stream.ConnectResponse.TrySetException(error);
            stream.Inbound.Writer.TryComplete(error);
        }
        lock (_flowLock) PulseFlowWaitersLocked();
    }

    private void PulseFlowWaiters()
    {
        lock (_flowLock) PulseFlowWaitersLocked();
    }

    private void PulseFlowWaitersLocked()
    {
        var previous = _flowChanged;
        _flowChanged = NewSignal();
        previous.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool IsValidHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace)) return false;
        if (IPAddress.TryParse(value, out _)) return true;
        if (value.Contains(':') || value.Contains('/') || value.Contains('\\')) return false;
        try { return Uri.CheckHostName(new IdnMapping().GetAscii(value.TrimEnd('.'))) == UriHostNameType.Dns; }
        catch (ArgumentException) { return false; }
    }

    private static string FormatAuthority(string host, int port)
    {
        if (IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
            return $"[{address}]:{port}";
        if (!IPAddress.TryParse(host, out _)) host = new IdnMapping().GetAscii(host.TrimEnd('.')).ToLowerInvariant();
        return $"{host}:{port}";
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        _disposed = true;
        _lifetime.Cancel();
        Volatile.Write(ref _connected, false);
        try { _tls.Dispose(); } catch { }
        try { _tcp.Dispose(); } catch { }
        FailAllStreams(new IOException("The HTTP/2 upstream session has been closed."));
        if (_readerTask is not null)
        {
            try { await _readerTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch { }
        }
        if (_keepAliveTask is not null)
        {
            try { await _keepAliveTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch { }
        }
        _headerBlock?.Dispose();
        _lifetime.Dispose();
        // The write semaphore is intentionally not disposed: a caller racing shutdown can still unwind
        // from WaitAsync/WriteFrameAsync and must be able to release it without a late disposal exception.
    }

    private sealed class H2StreamState(int id, long sendWindow)
    {
        public int Id { get; } = id;
        public string? Authority { get; set; }
        public TaskCompletionSource<bool> ConnectResponse { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Channel<byte[]> Inbound { get; } = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = false });
        public long SendWindow = sendWindow;
        public long RemoteReceiveWindow = DefaultWindowSize;
        public volatile bool RemoteEnded;
        public volatile bool LocalEnded;
    }

    private sealed class H2DuplexStream : IUpstreamDuplexStream
    {
        private readonly WindowsHttp2ConnectSession _owner;
        private readonly H2StreamState _state;
        private readonly H2InputStream _input;
        private readonly H2OutputStream _output;
        private int _disposed;

        public H2DuplexStream(WindowsHttp2ConnectSession owner, H2StreamState state)
        {
            _owner = owner;
            _state = state;
            _input = new H2InputStream(owner, state);
            _output = new H2OutputStream(owner, state);
        }

        public Stream Input => _input;
        public Stream Output => _output;
        public ValueTask CompleteOutputAsync(CancellationToken cancellationToken) => _owner.CompleteOutputAsync(_state, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (!_state.RemoteEnded && _owner.IsConnected)
            {
                try { await _owner.WriteRstStreamAsync(_state.Id, ErrorCancel, CancellationToken.None).ConfigureAwait(false); } catch { }
            }
            _owner._streams.TryRemove(_state.Id, out _);
            _state.Inbound.Writer.TryComplete();
            _owner.PulseFlowWaiters();
            await _input.DisposeAsync().ConfigureAwait(false);
            await _output.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class H2InputStream(WindowsHttp2ConnectSession owner, H2StreamState state) : Stream
    {
        private byte[]? _current;
        private int _currentOffset;
        private bool _disposed;
        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (buffer.IsEmpty) return 0;
            while (_current is null || _currentOffset >= _current.Length)
            {
                _current = null;
                _currentOffset = 0;
                try
                {
                    _current = await state.Inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException ex)
                {
                    if (ex.InnerException is not null) throw new IOException("HTTP/2 response stream failed.", ex.InnerException);
                    return 0;
                }
            }
            var count = Math.Min(buffer.Length, _current.Length - _currentOffset);
            _current.AsMemory(_currentOffset, count).CopyTo(buffer);
            _currentOffset += count;
            await owner.CreditReceiveWindowAsync(state.Id, count, cancellationToken).ConfigureAwait(false);
            return count;
        }

        protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
        public override ValueTask DisposeAsync() { _disposed = true; GC.SuppressFinalize(this); return ValueTask.CompletedTask; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class H2OutputStream(WindowsHttp2ConnectSession owner, H2StreamState state) : Stream
    {
        private bool _disposed;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed && !state.LocalEnded;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return owner.SendDataAsync(state, buffer, cancellationToken);
        }
        protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
        public override ValueTask DisposeAsync() { _disposed = true; GC.SuppressFinalize(this); return ValueTask.CompletedTask; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class HeaderCollector : hpack.IHeaderListener
    {
        private const int MaximumFields = 128;
        private const int MaximumDecodedBytes = 64 * 1024;
        private bool _sawRegularHeader;
        private int _decodedBytes;
        private int _fieldCount;
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddHeader(byte[] name, byte[] value, bool sensitive)
        {
            if (++_fieldCount > MaximumFields || (long)_decodedBytes + name.Length + value.Length > MaximumDecodedBytes)
                throw new IOException("HTTP/2 response headers exceed the decoded size/count limit.");
            _decodedBytes += name.Length + value.Length;
            Http2ProtocolRules.ValidateResponseHeaderName(name, ref _sawRegularHeader);
            Http2ProtocolRules.ValidateResponseHeaderValue(value);

            var headerName = Encoding.ASCII.GetString(name);
            var headerValue = Encoding.UTF8.GetString(value);
            if (headerName.Equals(":status", StringComparison.Ordinal))
            {
                if (!Headers.TryAdd(headerName, headerValue))
                    throw new IOException("HTTP/2 response included duplicate :status headers.");
            }
            else
            {
                // Duplicate regular fields are legal in HTTP/2. They are not consulted for CONNECT
                // authorization/status decisions, so retain the first value without losing framing.
                Headers.TryAdd(headerName, headerValue);
            }
        }
    }

    private const byte FrameData = 0;
    private const byte FrameHeaders = 1;
    private const byte FramePriority = 2;
    private const byte FrameRstStream = 3;
    private const byte FrameSettings = 4;
    private const byte FramePushPromise = 5;
    private const byte FramePing = 6;
    private const byte FrameGoAway = 7;
    private const byte FrameWindowUpdate = 8;
    private const byte FrameContinuation = 9;
    private const byte FlagEndStream = 0x1;
    private const byte FlagAck = 0x1;
    private const byte FlagEndHeaders = 0x4;
    private const byte FlagPadded = 0x8;
    private const byte FlagPriority = 0x20;
    private const uint ErrorStreamClosed = 0x5;
    private const uint ErrorCancel = 0x8;
}

/// <summary>Small RFC 7541 encoder specialized for the classic CONNECT headers FoxyVPN requires.</summary>
public static class Http2ConnectHeaderEncoder
{
    /// <summary>
    /// Builds exactly :method=CONNECT, :authority, and never-indexed proxy-authorization.
    /// It deliberately omits :scheme and :path because this is classic HTTP/2 CONNECT.
    /// </summary>
    public static byte[] Encode(string authority, string bearerToken)
    {
        if (string.IsNullOrWhiteSpace(authority) || authority.Length > 4096 || authority.Any(char.IsControl) || authority.Any(char.IsWhiteSpace) || authority.Any(ch => ch > 0x7F))
            throw new ArgumentException("Invalid CONNECT authority.", nameof(authority));
        if (string.IsNullOrWhiteSpace(bearerToken) || bearerToken.Length > 16 * 1024 || bearerToken.Any(char.IsControl) || bearerToken.Any(ch => ch > 0x7F))
            throw new ArgumentException("Invalid proxy authorization token.", nameof(bearerToken));

        using var output = new MemoryStream();
        // Literal without indexing, indexed static name :method (index 2), value CONNECT.
        WriteInteger(output, 0x00, 4, 2);
        WriteString(output, Encoding.ASCII.GetBytes("CONNECT"));
        // Literal without indexing, indexed static name :authority (index 1), value host:port.
        WriteInteger(output, 0x00, 4, 1);
        WriteString(output, Encoding.ASCII.GetBytes(authority));
        // Literal never indexed (0001 prefix) with a literal field name. This prevents the Bearer token
        // from entering a dynamic compression table and leaking across requests/streams.
        WriteInteger(output, 0x10, 4, 0);
        WriteString(output, Encoding.ASCII.GetBytes("proxy-authorization"));
        WriteString(output, Encoding.ASCII.GetBytes("Bearer " + bearerToken));
        if (output.Length > 16 * 1024) throw new ArgumentException("CONNECT header block exceeds the size limit.");
        return output.ToArray();
    }

    private static void WriteInteger(Stream stream, byte prefixBits, int prefixSize, int value)
    {
        var maxPrefix = (1 << prefixSize) - 1;
        if (value < maxPrefix)
        {
            stream.WriteByte((byte)(prefixBits | value));
            return;
        }
        stream.WriteByte((byte)(prefixBits | maxPrefix));
        value -= maxPrefix;
        while (value >= 128)
        {
            stream.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    private static void WriteString(Stream stream, byte[] value)
    {
        WriteInteger(stream, 0, 7, value.Length); // non-Huffman literal string
        stream.Write(value, 0, value.Length);
    }
}
