using System.Buffers.Binary;
using System.IO;

namespace FoxyVPN.Windows.Services;

/// <summary>Bounded validation for DNS responses received through an authenticated upstream.</summary>
internal static class DnsMessageRules
{
    private const int HeaderLength = 12;
    private const ushort QuestionRecordTypeClassLength = 4;
    private const ushort QueryResponseFlag = 0x8000;
    private const ushort OpcodeMask = 0x7800;

    /// <summary>
    /// Returns true only when the DNS message is a response to the supplied query, including a
    /// matching transaction ID, opcode, question count and semantic question tuples. Name
    /// compression is decoded with strict bounds before question tuples are compared.
    /// </summary>
    public static bool IsResponseToQuery(ReadOnlySpan<byte> query, ReadOnlySpan<byte> response)
    {
        if (query.Length < HeaderLength || response.Length < HeaderLength) return false;

        var queryFlags = BinaryPrimitives.ReadUInt16BigEndian(query.Slice(2, 2));
        var responseFlags = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(2, 2));
        if ((queryFlags & QueryResponseFlag) != 0 || (responseFlags & QueryResponseFlag) == 0) return false;
        if ((queryFlags & OpcodeMask) != (responseFlags & OpcodeMask)) return false;
        if (!query[..2].SequenceEqual(response[..2])) return false;

        var queryCount = BinaryPrimitives.ReadUInt16BigEndian(query.Slice(4, 2));
        var responseCount = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(4, 2));
        if (queryCount == 0 || queryCount != responseCount) return false;

        var queryOffset = HeaderLength;
        var responseOffset = HeaderLength;
        for (var i = 0; i < queryCount; i++)
        {
            if (!TryReadQuestion(query, ref queryOffset, out var queryName, out var queryType, out var queryClass) ||
                !TryReadQuestion(response, ref responseOffset, out var responseName, out var responseType, out var responseClass))
                return false;

            if (queryType != responseType || queryClass != responseClass || !queryName.AsSpan().SequenceEqual(responseName))
                return false;
        }
        return true;
    }

    private static bool TryReadQuestion(
        ReadOnlySpan<byte> message,
        ref int offset,
        out byte[] canonicalName,
        out ushort recordType,
        out ushort recordClass)
    {
        canonicalName = Array.Empty<byte>();
        recordType = 0;
        recordClass = 0;
        if (!TryReadName(message, ref offset, out canonicalName) || offset < 0 || offset > message.Length - QuestionRecordTypeClassLength)
            return false;

        recordType = BinaryPrimitives.ReadUInt16BigEndian(message.Slice(offset, 2));
        recordClass = BinaryPrimitives.ReadUInt16BigEndian(message.Slice(offset + 2, 2));
        offset += QuestionRecordTypeClassLength;
        return true;
    }

    private static bool TryReadName(ReadOnlySpan<byte> message, ref int offset, out byte[] canonicalName)
    {
        canonicalName = Array.Empty<byte>();
        if ((uint)offset >= (uint)message.Length) return false;

        var cursor = offset;
        var offsetAfterEncodedName = -1;
        var expandedLength = 1; // Includes the terminating root label.
        using var name = new MemoryStream(64);

        // A valid compression pointer always points backwards. Requiring strictly decreasing
        // cursors prevents cycles and bounds traversal without a separate visited-set allocation.
        var steps = 0;
        while (steps++ < message.Length)
        {
            if ((uint)cursor >= (uint)message.Length) return false;
            var labelLength = message[cursor];
            if (labelLength == 0)
            {
                name.WriteByte(0);
                if (expandedLength > 255) return false;
                offset = offsetAfterEncodedName >= 0 ? offsetAfterEncodedName : cursor + 1;
                canonicalName = name.ToArray();
                return true;
            }

            if ((labelLength & 0xC0) == 0xC0)
            {
                if (cursor + 1 >= message.Length) return false;
                var pointer = ((labelLength & 0x3F) << 8) | message[cursor + 1];
                if (pointer >= cursor || pointer >= message.Length) return false;
                if (offsetAfterEncodedName < 0) offsetAfterEncodedName = cursor + 2;
                cursor = pointer;
                continue;
            }

            if ((labelLength & 0xC0) != 0 || labelLength > 63) return false;
            var labelStart = cursor + 1;
            if (labelStart + labelLength > message.Length) return false;
            expandedLength += labelLength + 1;
            if (expandedLength > 255) return false;

            name.WriteByte(labelLength);
            for (var i = labelStart; i < labelStart + labelLength; i++)
            {
                var value = message[i];
                if (value is >= (byte)'A' and <= (byte)'Z') value = (byte)(value + ('a' - 'A'));
                name.WriteByte(value);
            }
            cursor = labelStart + labelLength;
        }

        return false;
    }
}
