using System.Net.Http;
using System.Text.Json;
using FoxyVPN.Windows.Models;

namespace FoxyVPN.Windows.Services;

public sealed class ServerCatalogService : IDisposable
{
    public const string RemoteSettingsUrl = "https://firefox.settings.services.mozilla.com/v1/buckets/main/collections/vpn-serverlist/records";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly AppLog _log;

    public ServerCatalogService(AppLog log)
    {
        _log = log;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MozillaVPN/2.35.0 (sys:windows; iap:true)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<IReadOnlyList<ServerOption>> FetchOptionsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(RemoteSettingsUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var options = ParseOptions(json);
        _log.Info("ServerCatalog", $"Fetched {options.Count} connectable server endpoints from Mozilla Remote Settings.");
        return options;
    }

    public static IReadOnlyList<ServerOption> ParseOptions(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!TryGetProperty(root, "data", out var records) || records.ValueKind != JsonValueKind.Array)
            return Array.Empty<ServerOption>();

        var result = new List<ServerOption>();
        foreach (var record in records.EnumerateArray())
        {
            var countryElement = TryGetProperty(record, "country", out var nestedCountry) ? nestedCountry : record;
            var countryName = GetString(countryElement, "name");
            var countryCode = GetString(countryElement, "code");
            if (string.IsNullOrWhiteSpace(countryCode) || countryName.Equals("CatchAll Anycast", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!TryGetProperty(countryElement, "cities", out var cities) || cities.ValueKind != JsonValueKind.Array) continue;

            foreach (var city in cities.EnumerateArray())
            {
                var cityName = GetString(city, "name");
                var cityCode = GetString(city, "code");
                if (!TryGetProperty(city, "servers", out var servers) || servers.ValueKind != JsonValueKind.Array) continue;
                foreach (var server in servers.EnumerateArray())
                {
                    if (GetBoolean(server, "quarantined")) continue;
                    var host = GetString(server, "hostname");
                    var port = GetInt32(server, "port");
                    var hasProtocols = TryGetProperty(server, "protocols", out var protocols) && protocols.ValueKind == JsonValueKind.Array;
                    if (hasProtocols && protocols.GetArrayLength() > 0)
                    {
                        var connect = protocols.EnumerateArray().FirstOrDefault(p => GetString(p, "name").Equals("connect", StringComparison.Ordinal));
                        if (connect.ValueKind == JsonValueKind.Undefined) continue;
                        var protocolHost = GetString(connect, "host");
                        var protocolPort = GetInt32(connect, "port");
                        if (!string.IsNullOrWhiteSpace(protocolHost)) host = protocolHost;
                        if (protocolPort > 0) port = protocolPort;
                    }
                    if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) continue;
                    result.Add(new ServerOption(countryName, countryCode.ToUpperInvariant(), cityName, cityCode, host, port));
                }
            }
        }
        return result
            .DistinctBy(x => (x.CountryCode, x.CityCode, x.Host, x.Port))
            .OrderBy(x => x.CountryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.CityName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Host, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }

    private static string GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static int GetInt32(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static bool GetBoolean(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.True;

    public void Dispose() => _http.Dispose();
}
