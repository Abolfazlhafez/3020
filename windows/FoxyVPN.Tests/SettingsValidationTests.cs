using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class SettingsValidationTests
{
    [Theory]
    [InlineData("vpn.example.com", true)]
    [InlineData("edge-1.mozilla.net", true)]
    [InlineData("localhost", false)]
    [InlineData("-bad.example", false)]
    [InlineData("bad-.example", false)]
    [InlineData("bad host.example", false)]
    [InlineData("", false)]
    public void HostnameValidation_MatchesExpectedRules(string host, bool valid) =>
        Assert.Equal(valid, SettingsStore.IsValidHostname(host));

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("2001:db8::1", true)]
    [InlineData("not-an-ip", false)]
    [InlineData("1.1.1.999", false)]
    public void IpValidation_UsesNumericAddressParser(string host, bool valid) =>
        Assert.Equal(valid, SettingsStore.IsIpAddress(host));
}
