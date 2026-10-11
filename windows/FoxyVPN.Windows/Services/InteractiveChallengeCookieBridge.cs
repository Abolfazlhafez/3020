using System.IO;
using System.Net;

namespace FoxyVPN.Windows.Services;

/// <summary>A short-lived in-memory cookie copied from the explicit, user-mediated challenge window.</summary>
public sealed record InteractiveChallengeCookie(
    string Name,
    string Value,
    string Domain,
    string Path,
    bool IsHttpOnly,
    bool IsSecure,
    bool IsSession,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Enforces a small allow-list before transferring cookies from WebView2 to one of the API clients.
/// This is a user-mediated browser challenge, not a challenge solver or anti-bot bypass.
/// </summary>
public static class InteractiveChallengeCookieBridge
{
    private static readonly string[] AllowedHosts =
    [
        "api.accounts.firefox.com",
        "accounts.firefox.com",
        "vpn.mozilla.org"
    ];

    public static bool IsAllowedOrigin(Uri origin) =>
        origin is not null && origin.IsAbsoluteUri && origin.IsDefaultPort &&
        origin.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        origin.UserInfo.Length == 0 && origin.AbsolutePath == "/" &&
        origin.Query.Length == 0 && origin.Fragment.Length == 0 &&
        AllowedHosts.Contains(origin.Host, StringComparer.OrdinalIgnoreCase);

    public static int Import(CookieContainer target, Uri origin, IReadOnlyList<InteractiveChallengeCookie> snapshots)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(snapshots);
        if (!IsAllowedOrigin(origin)) throw new InvalidOperationException("Challenge cookie transfer was refused because the origin is not an approved HTTPS FoxyVPN service.");
        if (snapshots.Count > 128) throw new InvalidDataException("The challenge returned an unexpected number of cookies; none were imported.");

        var imported = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var source in snapshots)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Name) || source.Name.Length > 256 ||
                source.Value is null || source.Value.Length > 8192 ||
                source.Name.Any(char.IsControl) || source.Value.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(source.Path) || source.Path.Length > 2048 ||
                !source.Path.StartsWith("/", StringComparison.Ordinal) || source.Path.Any(char.IsControl))
                continue;
            if (!source.IsSecure) continue; // The challenge origin is HTTPS; never import non-secure service cookies.
            if (source.ExpiresAt is { } expires && expires <= now) continue;

            var rawDomain = (source.Domain ?? string.Empty).Trim();
            var normalizedDomain = rawDomain.TrimStart('.').TrimEnd('.');
            if (normalizedDomain.Length == 0 || !IsDomainAllowedForOrigin(origin.Host, normalizedDomain)) continue;

            try
            {
                var cookie = new Cookie(source.Name, source.Value, source.Path)
                {
                    Secure = true,
                    HttpOnly = source.IsHttpOnly
                };
                // Keep host-only cookies host-only. A leading dot from WebView2 is preserved for parent-domain cookies.
                if (rawDomain.StartsWith(".", StringComparison.Ordinal) ||
                    !normalizedDomain.Equals(origin.Host, StringComparison.OrdinalIgnoreCase))
                    cookie.Domain = "." + normalizedDomain;
                if (!source.IsSession && source.ExpiresAt is { } expiry)
                    cookie.Expires = expiry.UtcDateTime;

                target.Add(origin, cookie);
                imported++;
            }
            catch (CookieException) { /* Invalid cookie: ignore it, never widen its scope to recover. */ }
            catch (ArgumentException) { /* Invalid cookie: ignore it, never widen its scope to recover. */ }
        }
        return imported;
    }

    private static bool IsDomainAllowedForOrigin(string originHost, string cookieDomain)
    {
        if (!(originHost.Equals(cookieDomain, StringComparison.OrdinalIgnoreCase) ||
              originHost.EndsWith("." + cookieDomain, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Restrict parent-domain cookies to the two known service domains; exact approved hosts are also valid.
        return AllowedHosts.Contains(cookieDomain, StringComparer.OrdinalIgnoreCase) ||
               cookieDomain.Equals("firefox.com", StringComparison.OrdinalIgnoreCase) ||
               cookieDomain.Equals("mozilla.org", StringComparison.OrdinalIgnoreCase) ||
               cookieDomain.Equals("accounts.firefox.com", StringComparison.OrdinalIgnoreCase);
    }
}
