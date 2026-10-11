namespace FoxyVPN.Windows.Services;

/// <summary>Pure timing policy for proactive Guardian proxy-pass renewal.</summary>
public static class ProxyPassRenewalSchedule
{
    public static TimeSpan NextDelay(long? expiresAtEpochSeconds, long nowEpochSeconds)
    {
        if (expiresAtEpochSeconds is not { } expiry) return TimeSpan.FromMinutes(2);
        // Refresh around half-life, but avoid rapid polling and never wait indefinitely for a stale expiry.
        var seconds = Math.Clamp((expiry - nowEpochSeconds) / 2, 15, 30 * 60);
        return TimeSpan.FromSeconds(seconds);
    }
}
