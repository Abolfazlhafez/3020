using System.Text.Json.Serialization;

namespace FoxyVPN.Windows.Models;

public sealed record VpnProtocol(
    [property: JsonPropertyName("name")] string Name = "",
    [property: JsonPropertyName("host")] string Host = "",
    [property: JsonPropertyName("port")] int Port = 0,
    [property: JsonPropertyName("scheme")] string Scheme = "",
    [property: JsonPropertyName("templateString")] string TemplateString = "");

public sealed record VpnServerNode(
    [property: JsonPropertyName("hostname")] string Hostname = "",
    [property: JsonPropertyName("port")] int Port = 0,
    [property: JsonPropertyName("quarantined")] bool Quarantined = false,
    [property: JsonPropertyName("protocols")] List<VpnProtocol>? Protocols = null);

public sealed record VpnCity(
    [property: JsonPropertyName("name")] string Name = "",
    [property: JsonPropertyName("code")] string Code = "",
    [property: JsonPropertyName("servers")] List<VpnServerNode>? Servers = null);

public sealed record VpnCountry(
    [property: JsonPropertyName("name")] string Name = "",
    [property: JsonPropertyName("code")] string Code = "",
    [property: JsonPropertyName("cities")] List<VpnCity>? Cities = null);

public sealed record ServerOption(string CountryName, string CountryCode, string CityName, string CityCode, string Host, int Port)
{
    public string DisplayName => $"{CountryName} · {CityName}";
    public string Endpoint => $"{Host}:{Port}";
    public string Key => $"{CountryCode.Trim().ToUpperInvariant()}|{CityCode.Trim().ToUpperInvariant()}|{Host.Trim().ToLowerInvariant()}|{Port}";
    public override string ToString() => $"{DisplayName} ({Endpoint})";
}

public sealed record RuntimeAuth(string AccessToken, string? RefreshToken, long ExpiresAtEpochSeconds);

public sealed record Entitlement(bool Subscribed, string Uid, long? MaxBytes, bool LimitedBandwidth, long? QuotaRemaining = null)
{
    public double? UsageRatio => MaxBytes is > 0 && QuotaRemaining.HasValue
        ? Math.Clamp(1d - ((double)QuotaRemaining.Value / MaxBytes.Value), 0d, 1d)
        : null;
}

public enum WindowsConnectionState { Disconnected, Connecting, Connected }
