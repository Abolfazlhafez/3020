using System.Net;
using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class InteractiveChallengeCookieBridgeTests
{
    [Theory]
    [InlineData("https://api.accounts.firefox.com/")]
    [InlineData("https://accounts.firefox.com/")]
    [InlineData("https://vpn.mozilla.org/")]
    public void ApprovedServiceOriginsAreAccepted(string value)
    {
        Assert.True(InteractiveChallengeCookieBridge.IsAllowedOrigin(new Uri(value)));
    }

    [Theory]
    [InlineData("http://vpn.mozilla.org/")]
    [InlineData("https://vpn.mozilla.org.evil.example/")]
    [InlineData("https://example.com/")]
    [InlineData("https://api.accounts.firefox.com:8443/")]
    [InlineData("https://api.accounts.firefox.com/custom/path")]
    public void UnapprovedOriginsAreRejected(string value)
    {
        Assert.False(InteractiveChallengeCookieBridge.IsAllowedOrigin(new Uri(value)));
    }

    [Fact]
    public void ImportCopiesOnlySecureMatchingServiceCookies()
    {
        var origin = new Uri("https://api.accounts.firefox.com/");
        var jar = new CookieContainer();
        var cookies = new[]
        {
            new InteractiveChallengeCookie("challenge", "ok-value", "api.accounts.firefox.com", "/", true, true, true, null),
            new InteractiveChallengeCookie("parent", "parent-value", ".firefox.com", "/", true, true, true, null),
            new InteractiveChallengeCookie("insecure", "not-imported", "api.accounts.firefox.com", "/", true, false, true, null),
            new InteractiveChallengeCookie("foreign", "not-imported", ".evil.example", "/", true, true, true, null)
        };

        var count = InteractiveChallengeCookieBridge.Import(jar, origin, cookies);
        var imported = jar.GetCookies(origin);

        Assert.Equal(2, count);
        Assert.Equal("ok-value", imported["challenge"]?.Value);
        Assert.Equal("parent-value", imported["parent"]?.Value);
        Assert.Null(imported["insecure"]);
        Assert.Null(imported["foreign"]);
    }

    [Fact]
    public void MalformedCookieSnapshotsAreIgnoredWithoutImportingAnything()
    {
        var origin = new Uri("https://api.accounts.firefox.com/");
        var jar = new CookieContainer();
        InteractiveChallengeCookie[] cookies =
        [
            null!,
            new InteractiveChallengeCookie("null-value", null!, "api.accounts.firefox.com", "/", true, true, true, null),
            new InteractiveChallengeCookie("null-path", "value", "api.accounts.firefox.com", null!, true, true, true, null),
            new InteractiveChallengeCookie("control", "bad\r\nvalue", "api.accounts.firefox.com", "/", true, true, true, null)
        ];

        var count = InteractiveChallengeCookieBridge.Import(jar, origin, cookies);

        Assert.Equal(0, count);
        Assert.Empty(jar.GetCookies(origin).Cast<Cookie>());
    }

    [Fact]
    public void HttpOriginThrowsBeforeImport()
    {
        Assert.Throws<InvalidOperationException>(() => InteractiveChallengeCookieBridge.Import(
            new CookieContainer(), new Uri("http://api.accounts.firefox.com/"), Array.Empty<InteractiveChallengeCookie>()));
    }
}
