using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class ProxyPassRenewalScheduleTests
{
    [Fact]
    public void UnknownExpiryUsesSafeFallbackInterval()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), ProxyPassRenewalSchedule.NextDelay(null, 1_800_000_000));
    }

    [Fact]
    public void KnownExpirySchedulesAroundHalfLife()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), ProxyPassRenewalSchedule.NextDelay(1_800_000_600, 1_800_000_000));
    }

    [Theory]
    [InlineData(1_800_000_001, 1_800_000_000, 15)]
    [InlineData(1_900_000_000, 1_800_000_000, 1800)]
    public void DelayIsClamped(long expiry, long now, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ProxyPassRenewalSchedule.NextDelay(expiry, now));
    }
}
