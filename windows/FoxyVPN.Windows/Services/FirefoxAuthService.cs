using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FoxyVPN.Windows.Models;

namespace FoxyVPN.Windows.Services;

public sealed class FirefoxAuthService : IDisposable
{
    private const string AuthServer = "https://api.accounts.firefox.com/v1";
    // Public Firefox Accounts client ID used by the original FoxyVPN Android client; this is not a secret.
    private const string ClientId = "5882386c6d801776";
    private const string Scope = "profile https://identity.mozilla.com/apps/vpn";
    private const string ProtocolVersion = "identity.mozilla.com/picl/v1/";
    private const int DefaultTokenTtlSeconds = 86400;
    private readonly HttpClient _http;
    private readonly CookieContainer _cookieContainer;
    private readonly TokenStore _tokens = new();
    private readonly AppLog _log;
    private string? _pendingSessionToken;

    public FirefoxAuthService(AppLog log)
    {
        _log = log;
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler { CookieContainer = _cookieContainer, UseCookies = true, AutomaticDecompression = DecompressionMethods.All };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MozillaVPN/2.35.0 (sys:windows; iap:true)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public RuntimeAuth? CurrentAuth => _tokens.Load();

    public int ImportInteractiveChallengeCookies(Uri origin, IReadOnlyList<InteractiveChallengeCookie> cookies) =>
        InteractiveChallengeCookieBridge.Import(_cookieContainer, origin, cookies);
    public bool HasSession => CurrentAuth is not null;
    public bool HasValidAccessToken => CurrentAuth is { } auth && auth.ExpiresAtEpochSeconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;

    /// <returns>true when email two-factor verification is required.</returns>
    public async Task<bool> StartLoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Enter your Firefox Account email.", nameof(email));
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Enter your password.", nameof(password));
        var body = new Dictionary<string, object?>
        {
            ["email"] = email.Trim(),
            ["authPW"] = DeriveAuthPassword(email.Trim(), password),
            ["verificationMethod"] = "email-2fa"
        };
        JsonDocument result;
        try
        {
            result = await FxaRequestAsync("POST", "/account/login", null, body, cancellationToken);
        }
        catch (FirefoxApiException ex) when (ex.Errno == 107)
        {
            body.Remove("verificationMethod");
            result = await FxaRequestAsync("POST", "/account/login", null, body, cancellationToken);
        }
        using (result)
        {
            _pendingSessionToken = GetString(result.RootElement, "sessionToken");
            if (string.IsNullOrWhiteSpace(_pendingSessionToken)) throw new InvalidOperationException("Firefox Accounts did not return a session token.");
            var verified = GetBoolean(result.RootElement, "verified");
            if (!verified) return true;
            await CompleteLoginAsync(_pendingSessionToken, cancellationToken);
            return false;
        }
    }

    public async Task SubmitTwoFactorCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_pendingSessionToken)) throw new InvalidOperationException("Sign in again before entering a verification code.");
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Enter the verification code.", nameof(code));
        using var _ = await FxaRequestAsync("POST", "/session/verify_code", _pendingSessionToken,
            new Dictionary<string, object?> { ["code"] = code.Trim() }, cancellationToken);
        await CompleteLoginAsync(_pendingSessionToken, cancellationToken);
    }

    private async Task CompleteLoginAsync(string sessionToken, CancellationToken cancellationToken)
    {
        using var data = await FxaRequestAsync("POST", "/oauth/token", sessionToken, new Dictionary<string, object?>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "fxa-credentials",
            ["scope"] = Scope,
            ["access_type"] = "offline"
        }, cancellationToken);
        var access = GetString(data.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(access)) throw new InvalidOperationException("Firefox Accounts returned an empty access token.");
        var refresh = GetString(data.RootElement, "refresh_token");
        var expires = GetInt32(data.RootElement, "expires_in");
        _tokens.Save(new RuntimeAuth(access, string.IsNullOrWhiteSpace(refresh) ? null : refresh,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (expires > 0 ? expires : DefaultTokenTtlSeconds)));
        _pendingSessionToken = null;
        _log.Info("FirefoxAuth", "Firefox Account sign-in completed; tokens stored with Windows DPAPI.");
    }

    public async Task<string?> GetFreshAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var current = _tokens.Load();
        if (current is null) return null;
        if (!forceRefresh && current.ExpiresAtEpochSeconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60) return current.AccessToken;
        if (string.IsNullOrWhiteSpace(current.RefreshToken)) return null;
        try
        {
            using var data = await FxaRequestAsync("POST", "/oauth/token", null, new Dictionary<string, object?>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = current.RefreshToken,
                ["scope"] = Scope
            }, cancellationToken);
            var access = GetString(data.RootElement, "access_token");
            if (string.IsNullOrWhiteSpace(access)) throw new InvalidOperationException("Firefox Accounts returned no access token for refresh.");
            var refresh = GetString(data.RootElement, "refresh_token");
            var expires = GetInt32(data.RootElement, "expires_in");
            var renewed = new RuntimeAuth(access, string.IsNullOrWhiteSpace(refresh) ? current.RefreshToken : refresh,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (expires > 0 ? expires : DefaultTokenTtlSeconds));
            _tokens.Save(renewed);
            return renewed.AccessToken;
        }
        catch (FirefoxApiException ex) when (ex.StatusCode is 400 or 401 or 403)
        {
            _tokens.Clear();
            _log.Warn("FirefoxAuth", "Firefox Accounts rejected the saved refresh token; local session cleared.");
            return null;
        }
    }

    public void SignOut()
    {
        _pendingSessionToken = null;
        _tokens.Clear();
        _log.Info("FirefoxAuth", "Signed out locally; encrypted local tokens removed.");
    }

    public static long? JwtExpiryEpochSeconds(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            var expiry = GetInt64(document.RootElement, "exp");
            return expiry > 0 ? expiry : null;
        }
        catch { return null; }
    }

    private async Task<JsonDocument> FxaRequestAsync(string method, string path, string? sessionToken,
        object body, CancellationToken cancellationToken)
    {
        var uri = new Uri(new Uri(AuthServer), path);
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body);
        string? authorization = null;
        if (sessionToken is not null)
        {
            var (id, key) = DeriveHawkCredentials(sessionToken);
            authorization = BuildHawkHeader(method, uri, id, key, bodyBytes);
            CryptographicOperations.ZeroMemory(key);
        }

        using var request = new HttpRequestMessage(new HttpMethod(method), uri)
        {
            Content = new ByteArrayContent(bodyBytes)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        if (authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode == 406)
            throw new FirefoxChallengeRequiredException("Firefox Accounts requires an interactive browser verification. Complete the service page in the verification window; FoxyVPN will retry the request at most once.", new Uri(uri.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute));
        if (!response.IsSuccessStatusCode)
        {
            int? errno = null;
            string message = string.IsNullOrWhiteSpace(text) ? $"HTTP {(int)response.StatusCode}" : text;
            try
            {
                using var error = JsonDocument.Parse(text);
                errno = TryGetProperty(error.RootElement, "errno", out var errnoValue) && errnoValue.TryGetInt32(out var parsedErrno) ? parsedErrno : null;
                message = GetString(error.RootElement, "message") is { Length: > 0 } m ? m : message;
            }
            catch { }
            throw new FirefoxApiException(message, errno, (int)response.StatusCode);
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static string DeriveAuthPassword(string email, string password)
    {
        var salt = Encoding.UTF8.GetBytes($"{ProtocolVersion}quickStretch:{email}");
        var quickStretch = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 1000, HashAlgorithmName.SHA256, 32);
        var result = Hkdf(quickStretch, $"{ProtocolVersion}authPW", 32);
        CryptographicOperations.ZeroMemory(quickStretch);
        return Convert.ToHexString(result).ToLowerInvariant();
    }

    private static (string Id, byte[] Key) DeriveHawkCredentials(string sessionToken)
    {
        var sessionBytes = Convert.FromHexString(sessionToken);
        var expanded = Hkdf(sessionBytes, $"{ProtocolVersion}sessionToken", 64);
        var id = Convert.ToHexString(expanded.AsSpan(0, 32)).ToLowerInvariant();
        var key = expanded.AsSpan(32, 32).ToArray();
        CryptographicOperations.ZeroMemory(sessionBytes);
        CryptographicOperations.ZeroMemory(expanded);
        return (id, key);
    }

    private static string BuildHawkHeader(string method, Uri uri, string id, byte[] hmacKey, byte[] body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var nonceBytes = RandomNumberGenerator.GetBytes(6);
        var nonce = Convert.ToBase64String(nonceBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payloadHash = "";
        if (body.Length > 0)
        {
            using var sha = SHA256.Create();
            var prefix = Encoding.UTF8.GetBytes("hawk.1.payload\napplication/json\n");
            var suffix = Encoding.UTF8.GetBytes("\n");
            var payload = new byte[prefix.Length + body.Length + suffix.Length];
            Buffer.BlockCopy(prefix, 0, payload, 0, prefix.Length);
            Buffer.BlockCopy(body, 0, payload, prefix.Length, body.Length);
            Buffer.BlockCopy(suffix, 0, payload, prefix.Length + body.Length, suffix.Length);
            payloadHash = Convert.ToBase64String(sha.ComputeHash(payload));
            CryptographicOperations.ZeroMemory(payload);
        }
        var path = uri.PathAndQuery;
        var normalized = $"hawk.1.header\n{timestamp}\n{nonce}\n{method.ToUpperInvariant()}\n{path}\n{uri.Host}\n{uri.Port}\n{payloadHash}\n\n";
        using var hmac = new HMACSHA256(hmacKey);
        var mac = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized)));
        var header = $"Hawk id=\"{id}\", ts=\"{timestamp}\", nonce=\"{nonce}\", mac=\"{mac}\"";
        if (payloadHash.Length > 0) header += $", hash=\"{payloadHash}\"";
        return header;
    }

    private static byte[] Hkdf(byte[] inputKeyMaterial, string info, int length)
    {
        var salt = new byte[32];
        byte[] pseudoRandomKey;
        using (var extract = new HMACSHA256(salt)) pseudoRandomKey = extract.ComputeHash(inputKeyMaterial);
        var infoBytes = Encoding.UTF8.GetBytes(info);
        var result = new byte[length];
        var previousBlock = Array.Empty<byte>();
        var generated = 0;
        byte counter = 1;
        while (generated < length)
        {
            var input = new byte[previousBlock.Length + infoBytes.Length + 1];
            Buffer.BlockCopy(previousBlock, 0, input, 0, previousBlock.Length);
            Buffer.BlockCopy(infoBytes, 0, input, previousBlock.Length, infoBytes.Length);
            input[^1] = counter++;
            using var expand = new HMACSHA256(pseudoRandomKey);
            var block = expand.ComputeHash(input);
            var copy = Math.Min(block.Length, length - generated);
            Buffer.BlockCopy(block, 0, result, generated, copy);
            generated += copy;
            if (previousBlock.Length > 0) CryptographicOperations.ZeroMemory(previousBlock);
            previousBlock = block;
            CryptographicOperations.ZeroMemory(input);
        }
        CryptographicOperations.ZeroMemory(pseudoRandomKey);
        CryptographicOperations.ZeroMemory(salt);
        return result;
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement result)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out result)) return true;
        result = default;
        return false;
    }

    private static string GetString(JsonElement obj, string name) =>
        TryGetProperty(obj, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool GetBoolean(JsonElement obj, string name) =>
        TryGetProperty(obj, name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int GetInt32(JsonElement obj, string name) =>
        TryGetProperty(obj, name, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static long GetInt64(JsonElement obj, string name) =>
        TryGetProperty(obj, name, out var value) && value.TryGetInt64(out var result) ? result : 0L;

    public void Dispose() => _http.Dispose();
}

public sealed class FirefoxApiException(string message, int? errno, int statusCode) : Exception(message)
{
    public int? Errno { get; } = errno;
    public int StatusCode { get; } = statusCode;
}

public sealed class FirefoxChallengeRequiredException(string message, Uri challengeOrigin) : Exception(message)
{
    public Uri ChallengeOrigin { get; } = challengeOrigin;
}
