using System.IO;
using Xunit;
using System.Text;
using FoxyVPN.Windows.Services;

namespace FoxyVPN.Tests;

public sealed class Http2ConnectHeaderEncoderTests
{
    [Fact]
    public void EncodesClassicConnectWithoutSchemeOrPathAndNeverIndexesBearer()
    {
        var block = Http2ConnectHeaderEncoder.Encode("example.org:443", "unit-test-token");
        var decoder = new hpack.Decoder(8192, 4096);
        var listener = new Listener();
        using (var reader = new BinaryReader(new MemoryStream(block), Encoding.UTF8, leaveOpen: false))
        {
            decoder.Decode(reader, listener);
            decoder.EndHeaderBlock();
        }

        Assert.Equal("CONNECT", listener.Headers[":method"]);
        Assert.Equal("example.org:443", listener.Headers[":authority"]);
        Assert.Equal("Bearer unit-test-token", listener.Headers["proxy-authorization"]);
        Assert.False(listener.Headers.ContainsKey(":scheme"));
        Assert.False(listener.Headers.ContainsKey(":path"));
        Assert.Contains("proxy-authorization", listener.SensitiveHeaders);
    }

    [Theory]
    [InlineData("example.org:443\r\nX-Evil: yes", "token")]
    [InlineData("example.org:443", "token\r\nX-Evil: yes")]
    [InlineData("example.org:443", "токен")]
    public void RejectsHeaderInjectionAndNonAsciiBearerTokens(string authority, string token)
    {
        Assert.Throws<ArgumentException>(() => Http2ConnectHeaderEncoder.Encode(authority, token));
    }

    private sealed class Listener : hpack.IHeaderListener
    {
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> SensitiveHeaders { get; } = new();

        public void AddHeader(byte[] name, byte[] value, bool sensitive)
        {
            var decodedName = Encoding.UTF8.GetString(name);
            Headers[decodedName] = Encoding.UTF8.GetString(value);
            if (sensitive) SensitiveHeaders.Add(decodedName);
        }
    }
}
