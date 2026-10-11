using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class ServerCatalogTests
{
    [Fact]
    public void ParseOptions_FlattensCountriesAndUsesConnectProtocolEndpoint()
    {
        const string response = """
        {
          "data": [
            { "country": { "name": "Exampleland", "code": "EX", "cities": [
              { "name": "North", "code": "NTH", "servers": [
                { "hostname": "fallback.example", "port": 443, "quarantined": false,
                  "protocols": [ { "name": "connect", "host": "edge.example", "port": 8443, "scheme": "https" } ] }
              ] },
              { "name": "South", "code": "STH", "servers": [
                { "hostname": "offline.example", "port": 443, "quarantined": true, "protocols": [] }
              ] }
            ] } },
            { "country": { "name": "CatchAll Anycast", "code": "ZZ", "cities": [
              { "name": "Hidden", "code": "HID", "servers": [ { "hostname": "hidden.example", "port": 443, "protocols": [] } ] }
            ] } }
          ]
        }
        """;

        var options = ServerCatalogService.ParseOptions(response);

        var option = Assert.Single(options);
        Assert.Equal("Exampleland", option.CountryName);
        Assert.Equal("EX", option.CountryCode);
        Assert.Equal("North", option.CityName);
        Assert.Equal("edge.example", option.Host);
        Assert.Equal(8443, option.Port);
    }

    [Fact]
    public void ParseOptions_ReturnsEmptyWhenRemoteSettingsResponseHasNoDataArray()
    {
        Assert.Empty(ServerCatalogService.ParseOptions("{\"data\":null}"));
    }
}
