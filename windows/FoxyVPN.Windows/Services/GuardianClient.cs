using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using FoxyVPN.Windows.Models;

namespace FoxyVPN.Windows.Services;

public sealed record ProxyPass(string Token, long? ExpiresAtEpochSeconds, long? QuotaMax, long? QuotaRemaining, long? QuotaReset);

public sealed class GuardianClient : IDisposable
{
    public const string DefaultEndpoint = "https://vpn.mozilla.org";
    private readonly HttpClient _http;
    private readonly CookieContainer _cookieContainer;
    private readonly AppLog _log;

    public GuardianClient(AppLog log)
    {
        _log = log;
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler { CookieContainer = _cookieContainer, UseCookies = true, AutomaticDecompression = DecompressionMethods.All };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MozillaVPN/2.35.0 (sys:windows; iap:true)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public int ImportInteractiveChallengeCookies(Uri origin, IReadOnlyList<InteractiveChallengeCookie> cookies) =>
        InteractiveChallengeCookieBridge.Import(_cookieContainer, origin, cookies);

    public async Task<Entitlement> FetchUserInfoAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, $"{DefaultEndpoint}/api/v1/fpn/status", accessToken, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, text, "fetch account status");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var remaining = ParseHeaderLong(response, "X-Quota-Remaining");
        var max = ParseNullableLong(root, "maxBytes");
        var entitlement = new Entitlement(
            GetBoolean(root, "subscribed"),
            GetString(root, "uid"),
            max,
            GetBoolean(root, "limited_bandwidth"),
            remaining);
        if (entitlement.LimitedBandwidth && !entitlement.QuotaRemaining.HasValue)
        {
            try
            {
                var pass = await FetchProxyPassAsync(accessToken, cancellationToken);
                entitlement = entitlement with { QuotaRemaining = pass.QuotaRemaining ?? entitlement.QuotaRemaining, MaxBytes = pass.QuotaMax ?? entitlement.MaxBytes };
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not GuardianChallengeRequiredException)
            {
                _log.Warn("Guardian", $"Account status was retrieved, but quota headers could not be loaded: {ex.Message}");
            }
        }
        _log.Info("Guardian", "Retrieved Guardian entitlement status.");
        return entitlement;
    }

    public async Task<ProxyPass> FetchProxyPassAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, $"{DefaultEndpoint}/api/v1/fpn/token", accessToken, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode is 401 or 403) throw new UnauthorizedAccessException("Guardian rejected the Firefox Account access token.");
        if ((int)response.StatusCode == 429) throw new InvalidOperationException("The account's available VPN traffic quota has been exceeded.");
        EnsureSuccess(response, text, "fetch proxy pass");
        using var doc = JsonDocument.Parse(text);
        var token = GetString(doc.RootElement, "token");
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidDataException("Guardian response did not include a proxy token.");
        var expiry = ParseNullableLong(doc.RootElement, "expires_at") ?? FirefoxAuthService.JwtExpiryEpochSeconds(token);
        var pass = new ProxyPass(token, expiry,
            ParseHeaderLong(response, "X-Quota-Limit"),
            ParseHeaderLong(response, "X-Quota-Remaining"),
            ParseHeaderLong(response, "X-Quota-Reset"));
        _log.Info("Guardian", "Received a proxy pass from Guardian (token value not logged).");
        return pass;
    }

    public async Task<Entitlement> ActivateAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Post, $"{DefaultEndpoint}/api/v1/fpn/activate", accessToken, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, text, "activate Guardian entitlement");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        return new Entitlement(GetBoolean(root, "subscribed"), GetString(root, "uid"), ParseNullableLong(root, "maxBytes"), GetBoolean(root, "limited_bandwidth"));
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string url, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        if (method == HttpMethod.Post)
        {
            request.Content = new ByteArrayContent(Array.Empty<byte>());
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode == 406)
        {
            response.Dispose();
            throw new GuardianChallengeRequiredException("Firefox VPN Guardian requires an interactive browser verification. Complete the service page in the verification window; FoxyVPN will retry the request at most once.", new Uri(new Uri(url).GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute));
        }
        return response;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body, string operation)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Could not {operation}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForError(body)}", null, response.StatusCode);
    }

    private static string TrimForError(string value) => string.IsNullOrWhiteSpace(value) ? "" : value[..Math.Min(value.Length, 400)];
    private static string GetString(JsonElement obj, string name) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool GetBoolean(JsonElement obj, string name) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static long? ParseNullableLong(JsonElement obj, string name) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n >= 0 ? n : null;
    private static long? ParseHeaderLong(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) && long.TryParse(values.FirstOrDefault(), out var value) ? value : null;

    public void Dispose() => _http.Dispose();
}

public sealed class GuardianChallengeRequiredException(string message, Uri challengeOrigin) : Exception(message)
{
    public Uri ChallengeOrigin { get; } = challengeOrigin;
}
