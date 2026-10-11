using System.IO;
using System.Text;
using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

/// <summary>
/// Protocol-level tests for the upstream-proxy negotiation. They use an isolated scripted stream;
/// they do not contact public endpoints or log credentials.
/// </summary>
public sealed class UpstreamProxyHandshakeTests
{
    [Theory]
    [InlineData("SOCKS5", "alice", "", false)]
    [InlineData("SOCKS5", "", "secret", false)]
    [InlineData("SOCKS5", "alice", "secret", true)]
    [InlineData("HTTP", "", "secret", false)]
    [InlineData("HTTP", "alice", "", true)]
    public void UpstreamCredentialsAreValidatedBeforeConnecting(string type, string username, string password, bool valid)
    {
        var options = new WindowsUpstreamProxyOptions(type, "proxy.example.test", 8080, username, password);
        if (valid)
            WindowsHttp2ConnectSession.ValidateUpstreamProxyCredentials(options);
        else
            Assert.Throws<ArgumentException>(() => WindowsHttp2ConnectSession.ValidateUpstreamProxyCredentials(options));
    }

    [Fact]
    public void Socks5CredentialsOverRfcLimitAreRejectedBeforeConnecting()
    {
        var options = new WindowsUpstreamProxyOptions("SOCKS5", "proxy.example.test", 1080, new string('u', 256), new string('p', 256));
        Assert.Throws<ArgumentException>(() => WindowsHttp2ConnectSession.ValidateUpstreamProxyCredentials(options));
    }

    [Fact]
    public async Task HttpConnectProxySendsBasicAuthAndAcceptsOnlySuccessfulConnect()
    {
        var stream = new ScriptedStream(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\nProxy-Agent: test\r\n\r\n"));
        var options = new WindowsUpstreamProxyOptions("HTTP", "proxy.example.test", 8080, "user", "pass");

        await WindowsHttp2ConnectSession.EstablishUpstreamProxyTunnelAsync(stream, "edge.example.test", 443, options, CancellationToken.None);

        var request = Encoding.ASCII.GetString(stream.WrittenBytes);
        Assert.Contains("CONNECT edge.example.test:443 HTTP/1.1\r\n", request, StringComparison.Ordinal);
        Assert.Contains("Host: edge.example.test:443\r\n", request, StringComparison.Ordinal);
        Assert.Contains("Proxy-Authorization: Basic dXNlcjpwYXNz\r\n", request, StringComparison.Ordinal);
        Assert.DoesNotContain("pass", request, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n")]
    [InlineData("HTTP/1.1 403 Forbidden\r\n\r\n")]
    [InlineData("HTTP/1.1 2000 Not Really Success\r\n\r\n")]
    [InlineData("NOTHTTP 200 Nope\r\n\r\n")]
    public async Task HttpConnectProxyRejectsNon200OrMalformedResponses(string response)
    {
        var stream = new ScriptedStream(Encoding.ASCII.GetBytes(response));
        var options = new WindowsUpstreamProxyOptions("HTTP", "proxy.example.test", 8080);

        await Assert.ThrowsAsync<IOException>(() => WindowsHttp2ConnectSession.EstablishUpstreamProxyTunnelAsync(
            stream, "edge.example.test", 443, options, CancellationToken.None));
    }

    [Fact]
    public async Task HttpConnectProxyUsesBracketedIpv6Authority()
    {
        var stream = new ScriptedStream(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n\r\n"));
        var options = new WindowsUpstreamProxyOptions("HTTP", "proxy.example.test", 8080);

        await WindowsHttp2ConnectSession.EstablishUpstreamProxyTunnelAsync(stream, "2001:db8::55", 8443, options, CancellationToken.None);

        Assert.Contains("CONNECT [2001:db8::55]:8443 HTTP/1.1\r\n", Encoding.ASCII.GetString(stream.WrittenBytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Socks5NoAuthenticationNegotiatesConnectAndConsumesBoundAddress()
    {
        // greeting choice: no-auth; CONNECT success with IPv4 bind address 127.0.0.1:53000.
        byte[] serverReply = [0x05, 0x00, 0x05, 0x00, 0x00, 0x01, 127, 0, 0, 1, 0xCF, 0x08];
        var stream = new ScriptedStream(serverReply);
        var options = new WindowsUpstreamProxyOptions("SOCKS5", "proxy.example.test", 1080);

        await WindowsHttp2ConnectSession.EstablishUpstreamProxyTunnelAsync(stream, "edge.example.test", 443, options, CancellationToken.None);

        var written = stream.WrittenBytes;
        Assert.Equal(new byte[] { 0x05, 0x01, 0x00 }, written[..3]);
        Assert.Equal((byte)0x05, written[3]);
        Assert.Equal((byte)0x01, written[4]); // CONNECT
        Assert.Equal((byte)0x00, written[5]); // reserved
        Assert.Equal((byte)0x03, written[6]); // domain name
        Assert.Equal((byte)"edge.example.test".Length, written[7]);
        Assert.Equal(Encoding.ASCII.GetBytes("edge.example.test"), written[8..(8 + "edge.example.test".Length)]);
        Assert.Equal(new byte[] { 0x01, 0xBB }, written[^2..]);
    }

    [Fact]
    public async Task Socks5UsernamePasswordAuthenticationIsRequiredWhenConfigured()
    {
        // method 2 selected, RFC 1929 auth success, then CONNECT success with IPv6 bind address.
        byte[] serverReply = [0x05, 0x02, 0x01, 0x00, 0x05, 0x00, 0x00, 0x04,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xCF, 0x08];
        var stream = new ScriptedStream(serverReply);
        var options = new WindowsUpstreamProxyOptions("SOCKS5", "proxy.example.test", 1080, "alice", "s3cret");

        await WindowsHttp2ConnectSession.EstablishUpstreamProxyTunnelAsync(stream, "edge.example.test", 443, options, CancellationToken.None);

        var written = stream.WrittenBytes;
        Assert.Equal(new byte[] { 0x05, 0x01, 0x02 }, written[..3]);
        var authOffset = 3; // greeting is the first packet written, followed by RFC 1929 auth packet
        Assert.Equal((byte)0x01, written[authOffset]);
        Assert.Equal((byte)5, written[authOffset + 1]);
        Assert.Equal(Encoding.UTF8.GetBytes("alice"), written[(authOffset + 2)..(authOffset + 7)]);
        Assert.Equal((byte)6, written[authOffset + 7]);
        Assert.Equal(Encoding.UTF8.GetBytes("s3cret"), written[(authOffset + 8)..(authOffset + 14)]);
    }

    [Fact]
    public async Task Socks5RejectsEmptyPasswordForUsernamePasswordAuthentication()
    {
        var stream = new ScriptedStream([0x05, 0x02]);
        var options = new WindowsUpstreamProxyOptions("SOCKS5", "proxy.example.test", 1080, "alice", "");

        await Assert.ThrowsAsync<InvalidOperationException>(() => WindowsHttp2ConnectSession.EstablishUpstreamProxyTunnelAsync(
            stream, "edge.example.test", 443, options, CancellationToken.None));
        Assert.Equal(new byte[] { 0x05, 0x01, 0x02 }, stream.WrittenBytes);
    }

    [Fact]
    public async Task Socks5RejectsUnexpectedAuthenticationMethod()
    {
        var stream = new ScriptedStream([0x05, 0x02]); // configured no-auth; proxy insists on user/pass
        var options = new WindowsUpstreamProxyOptions("SOCKS5", "proxy.example.test", 1080);

        await Assert.ThrowsAsync<IOException>(() => WindowsHttp2ConnectSession.EstablishUpstreamProxyTunnelAsync(
            stream, "edge.example.test", 443, options, CancellationToken.None));
    }

    [Fact]
    public async Task Socks5RejectsAnUnsuccessfulConnectReply()
    {
        var stream = new ScriptedStream([0x05, 0x00, 0x05, 0x05, 0x00, 0x01, 127, 0, 0, 1, 0x00, 0x00]);
        var options = new WindowsUpstreamProxyOptions("SOCKS5", "proxy.example.test", 1080);

        await Assert.ThrowsAsync<IOException>(() => WindowsHttp2ConnectSession.EstablishUpstreamProxyTunnelAsync(
            stream, "edge.example.test", 443, options, CancellationToken.None));
    }

    private sealed class ScriptedStream(byte[] response) : Stream
    {
        private readonly byte[] _response = response;
        private readonly MemoryStream _written = new();
        private int _readOffset;

        public byte[] WrittenBytes => _written.ToArray();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _written.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _written.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count)
        {
            var amount = Math.Min(count, _response.Length - _readOffset);
            if (amount <= 0) return 0;
            Array.Copy(_response, _readOffset, buffer, offset, amount);
            _readOffset += amount;
            return amount;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var amount = Math.Min(buffer.Length, _response.Length - _readOffset);
            if (amount <= 0) return ValueTask.FromResult(0);
            _response.AsMemory(_readOffset, amount).CopyTo(buffer);
            _readOffset += amount;
            return ValueTask.FromResult(amount);
        }
        public override void Write(byte[] buffer, int offset, int count) => _written.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _written.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
