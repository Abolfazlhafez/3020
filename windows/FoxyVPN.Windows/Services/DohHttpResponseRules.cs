using System.Globalization;
using System.Text;

namespace FoxyVPN.Windows.Services;

/// <summary>Strict bounded parser for the HTTP/1.1 headers used by the DoH fallback transport.</summary>
internal static class DohHttpResponseRules
{
    public static bool TryParseResponseHeaders(ReadOnlySpan<byte> headerBytes, out int contentLength)
    {
        contentLength = 0;
        if (headerBytes.IsEmpty || headerBytes.Length > 16 * 1024) return false;

        // Header syntax is ASCII. Reject non-ASCII and control bytes before decoding so they cannot
        // be silently replaced or normalized by Encoding.ASCII.
        for (var i = 0; i < headerBytes.Length; i++)
        {
            var value = headerBytes[i];
            if (value > 0x7E || (value < 0x20 && value != (byte)'\r' && value != (byte)'\n' && value != (byte)'\t'))
                return false;
            // HTTP field lines must use CRLF pairs; reject bare CR/LF to avoid parser disagreement.
            if (value == (byte)'\r' && (i + 1 >= headerBytes.Length || headerBytes[i + 1] != (byte)'\n')) return false;
            if (value == (byte)'\n' && (i == 0 || headerBytes[i - 1] != (byte)'\r')) return false;
        }

        var text = Encoding.ASCII.GetString(headerBytes);
        var lines = text.Split("\r\n", StringSplitOptions.None);
        if (lines.Length < 2 || lines[0].Length == 0) return false;
        var status = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (status.Length < 2 || status[0] is not ("HTTP/1.0" or "HTTP/1.1") || status[1] != "200") return false;

        var sawContentLength = false;
        var sawContentType = false;
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0 || line[0] is ' ' or '\t') return false; // no empty fields or obsolete folding
            var separator = line.IndexOf(':');
            if (separator <= 0) return false;
            var name = line[..separator];
            if (!name.All(IsTokenCharacter)) return false;
            var value = line[(separator + 1)..].Trim(' ', '\t');
            if (value.Any(ch => (ch < 0x20 && ch != '\t') || ch == 0x7F)) return false;

            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                // Reject all duplicates, even identical values, rather than risk parser disagreement.
                if (sawContentLength || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 12 or > 4096)
                    return false;
                contentLength = parsed;
                sawContentLength = true;
            }
            else if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (sawContentType || !value.Split(';', 2)[0].Trim().Equals("application/dns-message", StringComparison.OrdinalIgnoreCase))
                    return false;
                sawContentType = true;
            }
            else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                // Transfer-Encoding is not supported by this bounded parser; reject rather than decode ambiguously.
                return false;
            }
        }

        return sawContentLength && sawContentType;
    }

    private static bool IsTokenCharacter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or
        '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
}
