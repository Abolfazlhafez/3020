using System.Globalization;
using System.IO;
using System.Text;

namespace FoxyVPN.Windows.Services;

/// <summary>Pure RFC-oriented validation helpers used by the HTTP/2 CONNECT transport.</summary>
internal static class Http2ProtocolRules
{
    public static int ParseGoAwayLastStreamId(int previousLastStreamId, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
            throw new IOException("HTTP/2 protocol error: malformed GOAWAY.");

        var lastStreamId = (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(payload[..4]) & 0x7FFFFFFF);
        // RFC 9113 permits repeated GOAWAY frames to narrow the range of streams that might have
        // been processed, but never to increase the last-stream identifier.
        if (lastStreamId > previousLastStreamId)
            throw new IOException("HTTP/2 protocol error: GOAWAY last-stream identifier increased.");
        return lastStreamId;
    }

    /// <summary>True when an unknown stream ID cannot identify a client-initiated stream already opened.</summary>
    public static bool IsIdleClientStream(int streamId, long nextClientStreamId) =>
        streamId <= 0 || (streamId & 1) == 0 || (long)streamId >= nextClientStreamId;

    /// <summary>Identifies an HTTP/2 informational response that precedes the final CONNECT response.</summary>
    public static bool IsInformationalResponseStatus(int status) => status >= 100 && status < 200 && status != 101;

    public static int ParseResponseStatus(string? value)
    {
        if (value is not { Length: 3 } || !value.All(char.IsAsciiDigit) ||
            !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var status) || status is < 100 or > 599 || status == 101)
            return 0;
        return status;
    }

    public static void ValidateResponseHeaderName(byte[] name, ref bool sawRegularHeader)
    {
        if (name.Length == 0)
            throw new IOException("HTTP/2 protocol error: an empty response header name was received.");

        var pseudo = name[0] == (byte)':';
        if (pseudo)
        {
            if (sawRegularHeader)
                throw new IOException("HTTP/2 protocol error: a pseudo-header followed a regular response header.");
            if (!name.AsSpan().SequenceEqual(":status"u8))
                throw new IOException("HTTP/2 protocol error: unexpected response pseudo-header.");
        }
        else
        {
            sawRegularHeader = true;
        }

        var start = pseudo ? 1 : 0;
        if (start == name.Length)
            throw new IOException("HTTP/2 protocol error: invalid response pseudo-header.");
        for (var i = start; i < name.Length; i++)
        {
            var ch = name[i];
            if (ch is >= (byte)'A' and <= (byte)'Z' || !IsHeaderToken(ch))
                throw new IOException("HTTP/2 protocol error: response header name is not a lowercase ASCII token.");
        }
    }

    public static void ValidateResponseHeaderValue(byte[] value)
    {
        if (value.Any(b => (b < 0x20 && b != 0x09) || b == 0x7F))
            throw new IOException("HTTP/2 protocol error: response header value contains a prohibited control byte.");
    }

    private static bool IsHeaderToken(byte value)
    {
        if (value is >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9') return true;
        return value is (byte)'!' or (byte)'#' or (byte)'$' or (byte)'%' or (byte)'&' or (byte)'\'' or
            (byte)'*' or (byte)'+' or (byte)'-' or (byte)'.' or (byte)'^' or (byte)'_' or
            (byte)'`' or (byte)'|' or (byte)'~';
    }
}
