using System.IO;
using System.Buffers.Binary;
using System.Text;
using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class DnsMessageRulesTests
{
    [Fact]
    public void AcceptsMatchingQuestionAndTransactionId()
    {
        var query = CreateMessage(isResponse: false, "example.com", recordType: 1);
        var response = CreateMessage(isResponse: true, "EXAMPLE.com", recordType: 1);

        Assert.True(DnsMessageRules.IsResponseToQuery(query, response));
    }

    [Fact]
    public void RejectsResponseWithDifferentTransactionId()
    {
        var query = CreateMessage(isResponse: false, "example.com", recordType: 1, id: 0x1234);
        var response = CreateMessage(isResponse: true, "example.com", recordType: 1, id: 0x5678);

        Assert.False(DnsMessageRules.IsResponseToQuery(query, response));
    }

    [Fact]
    public void RejectsMessageThatIsNotMarkedAsAResponse()
    {
        var query = CreateMessage(isResponse: false, "example.com", recordType: 1);
        var notAResponse = CreateMessage(isResponse: false, "example.com", recordType: 1);

        Assert.False(DnsMessageRules.IsResponseToQuery(query, notAResponse));
    }

    [Fact]
    public void RejectsDifferentOpcode()
    {
        var query = CreateMessage(isResponse: false, "example.com", recordType: 1, opcode: 0);
        var response = CreateMessage(isResponse: true, "example.com", recordType: 1, opcode: 1);

        Assert.False(DnsMessageRules.IsResponseToQuery(query, response));
    }

    [Fact]
    public void RejectsDifferentQuestionNameTypeOrClass()
    {
        var query = CreateMessage(isResponse: false, "example.com", recordType: 1, recordClass: 1);
        Assert.False(DnsMessageRules.IsResponseToQuery(query, CreateMessage(true, "other.example", 1, 1)));
        Assert.False(DnsMessageRules.IsResponseToQuery(query, CreateMessage(true, "example.com", 28, 1)));
        Assert.False(DnsMessageRules.IsResponseToQuery(query, CreateMessage(true, "example.com", 1, 3)));
    }

    [Fact]
    public void RejectsTruncatedOrMalformedQuestionNames()
    {
        var query = CreateMessage(isResponse: false, "example.com", recordType: 1);
        var truncatedResponse = CreateMessage(isResponse: true, "example.com", recordType: 1)[..^1];
        Assert.False(DnsMessageRules.IsResponseToQuery(query, truncatedResponse));

        var malformedQuery = new byte[18];
        BinaryPrimitives.WriteUInt16BigEndian(malformedQuery.AsSpan(0, 2), 0x1234);
        BinaryPrimitives.WriteUInt16BigEndian(malformedQuery.AsSpan(4, 2), 1);
        malformedQuery[12] = 0xC0;
        malformedQuery[13] = 0x0C; // Self-referential compression pointer.
        BinaryPrimitives.WriteUInt16BigEndian(malformedQuery.AsSpan(14, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(malformedQuery.AsSpan(16, 2), 1);
        Assert.False(DnsMessageRules.IsResponseToQuery(malformedQuery, query));
    }

    private static byte[] CreateMessage(
        bool isResponse,
        string host,
        ushort recordType,
        ushort recordClass = 1,
        ushort id = 0x1234,
        ushort opcode = 0)
    {
        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header[..2], id);
        var flags = (ushort)((opcode & 0x0F) << 11);
        flags |= isResponse ? (ushort)0x8180 : (ushort)0x0100;
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(2, 2), flags);
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(4, 2), 1);
        stream.Write(header);

        foreach (var label in host.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is < 1 or > 63) throw new ArgumentException("Invalid test DNS label.", nameof(host));
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }
        stream.WriteByte(0);
        Span<byte> typeAndClass = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(typeAndClass[..2], recordType);
        BinaryPrimitives.WriteUInt16BigEndian(typeAndClass.Slice(2, 2), recordClass);
        stream.Write(typeAndClass);
        return stream.ToArray();
    }
}
