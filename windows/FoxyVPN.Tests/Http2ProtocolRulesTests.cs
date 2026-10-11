using System.Buffers.Binary;
using System.IO;
using System.Text;
using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class Http2ProtocolRulesTests
{
    [Fact]
    public void GoAwayLastStreamMayNarrowButMustNotIncrease()
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), 9);
        Assert.Equal(9, Http2ProtocolRules.ParseGoAwayLastStreamId(int.MaxValue, payload));

        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), 5);
        Assert.Equal(5, Http2ProtocolRules.ParseGoAwayLastStreamId(9, payload));

        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), 7);
        Assert.Throws<IOException>(() => Http2ProtocolRules.ParseGoAwayLastStreamId(5, payload));
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(2, 5, true)]
    [InlineData(5, 5, true)]
    [InlineData(1, 3, false)]
    [InlineData(3, 5, false)]
    public void IdentifiesIdleClientStreamIds(int streamId, long nextStreamId, bool expected)
    {
        Assert.Equal(expected, Http2ProtocolRules.IsIdleClientStream(streamId, nextStreamId));
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(103, true)]
    [InlineData(199, true)]
    [InlineData(101, false)]
    [InlineData(200, false)]
    [InlineData(299, false)]
    [InlineData(0, false)]
    public void IdentifiesInformationalResponsesThatDoNotCompleteConnect(int status, bool expected)
    {
        Assert.Equal(expected, Http2ProtocolRules.IsInformationalResponseStatus(status));
    }

    [Theory]
    [InlineData("200", 200)]
    [InlineData("407", 407)]
    [InlineData("599", 599)]
    [InlineData("101", 0)]
    [InlineData("600", 0)]
    [InlineData("999", 0)]
    [InlineData("0200", 0)]
    [InlineData("2O0", 0)]
    [InlineData("099", 0)]
    [InlineData(null, 0)]
    public void ParsesOnlyThreeDigitHttpStatus(string? value, int expected)
    {
        Assert.Equal(expected, Http2ProtocolRules.ParseResponseStatus(value));
    }

    [Fact]
    public void ValidatesClassicConnectResponseHeaderNamesAndOrdering()
    {
        var sawRegular = false;
        Http2ProtocolRules.ValidateResponseHeaderName(Encoding.ASCII.GetBytes(":status"), ref sawRegular);
        Http2ProtocolRules.ValidateResponseHeaderName(Encoding.ASCII.GetBytes("content-type"), ref sawRegular);
        Assert.True(sawRegular);
        Assert.Throws<IOException>(() => Http2ProtocolRules.ValidateResponseHeaderName(Encoding.ASCII.GetBytes(":status"), ref sawRegular));
    }

    [Theory]
    [InlineData("")]
    [InlineData(":scheme")]
    [InlineData("Content-Type")]
    [InlineData("bad name")]
    [InlineData("bad/name")]
    public void RejectsInvalidOrUnexpectedHttp2HeaderNames(string name)
    {
        var sawRegular = false;
        Assert.Throws<IOException>(() => Http2ProtocolRules.ValidateResponseHeaderName(Encoding.ASCII.GetBytes(name), ref sawRegular));
    }

    [Fact]
    public void RejectsControlBytesInHttp2HeaderValues()
    {
        Assert.Throws<IOException>(() => Http2ProtocolRules.ValidateResponseHeaderValue([0x41, 0x00, 0x42]));
        Assert.Throws<IOException>(() => Http2ProtocolRules.ValidateResponseHeaderValue([0x41, 0x0A]));
        Assert.Throws<IOException>(() => Http2ProtocolRules.ValidateResponseHeaderValue([0x41, 0x0D]));
        Http2ProtocolRules.ValidateResponseHeaderValue(Encoding.UTF8.GetBytes("ok\tvalue"));
    }
}
