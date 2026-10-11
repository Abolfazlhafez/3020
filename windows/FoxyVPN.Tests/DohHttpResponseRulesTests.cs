using System.Text;
using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class DohHttpResponseRulesTests
{
    [Fact]
    public void AcceptsOneContentLengthAndDnsMessageContentType()
    {
        var headers = Header("Content-Type: application/dns-message", "Content-Length: 24");
        Assert.True(DohHttpResponseRules.TryParseResponseHeaders(headers, out var length));
        Assert.Equal(24, length);
    }

    [Fact]
    public void RejectsDuplicateContentLengthEvenWhenValuesMatch()
    {
        var headers = Header("Content-Type: application/dns-message", "Content-Length: 24", "Content-Length: 24");
        Assert.False(DohHttpResponseRules.TryParseResponseHeaders(headers, out _));
    }

    [Fact]
    public void RejectsDuplicateOrWrongContentType()
    {
        Assert.False(DohHttpResponseRules.TryParseResponseHeaders(Header("Content-Type: application/dns-message", "Content-Type: text/plain", "Content-Length: 24"), out _));
        Assert.False(DohHttpResponseRules.TryParseResponseHeaders(Header("Content-Type: text/plain", "Content-Length: 24"), out _));
    }

    [Fact]
    public void RejectsTransferEncodingAndMalformedHeaderLines()
    {
        Assert.False(DohHttpResponseRules.TryParseResponseHeaders(Header("Content-Type: application/dns-message", "Content-Length: 24", "Transfer-Encoding: chunked"), out _));
        Assert.False(DohHttpResponseRules.TryParseResponseHeaders(Header("Content-Type: application/dns-message", "Content-Length: 24", "broken-header"), out _));
        Assert.False(DohHttpResponseRules.TryParseResponseHeaders(Header("Content-Type: application/dns-message", " Content-Length: 24"), out _));
    }

    [Fact]
    public void RejectsInvalidLengthAndNonSuccessStatus()
    {
        Assert.False(DohHttpResponseRules.TryParseResponseHeaders(Header("Content-Type: application/dns-message", "Content-Length: 0"), out _));
        Assert.False(DohHttpResponseRules.TryParseResponseHeaders(Header("Content-Type: application/dns-message", "Content-Length: 4097"), out _));
        Assert.False(DohHttpResponseRules.TryParseResponseHeaders(Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nContent-Type: application/dns-message\r\nContent-Length: 24"), out _));
    }


    [Fact]
    public void RejectsBareLineFeedAndBareCarriageReturn()
    {
        var bareLf = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/dns-message\nContent-Length: 24");
        var bareCr = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/dns-message\rContent-Length: 24");
        Assert.False(DohHttpResponseRules.TryParseResponseHeaders(bareLf, out _));
        Assert.False(DohHttpResponseRules.TryParseResponseHeaders(bareCr, out _));
    }

    private static byte[] Header(params string[] fields) =>
        Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n" + string.Join("\r\n", fields));
}
