using System.IO;

namespace FoxyVPN.Windows.Services;

/// <summary>Fetches and briefly caches Guardian proxy passes without ever logging token contents.</summary>
public sealed class GuardianProxyPassProvider : IDisposable
{
    private static readonly TimeSpan FallbackCacheLifetime = TimeSpan.FromMinutes(4);
    private readonly GuardianClient _guardian;
    private readonly FirefoxAuthService _auth;
    private readonly AppLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProxyPass? _cachedPass;
    private long _fallbackExpiryEpochSeconds;
    private long _cachedExpiryEpochSeconds;
    private int _disposed;

    public long? CachedExpiryEpochSeconds
    {
        get
        {
            var expiry = Interlocked.Read(ref _cachedExpiryEpochSeconds);
            return expiry > 0 ? expiry : null;
        }
    }

    public GuardianProxyPassProvider(GuardianClient guardian, FirefoxAuthService auth, AppLog log)
    {
        _guardian = guardian ?? throw new ArgumentNullException(nameof(guardian));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) =>
        GetTokenCoreAsync(forceRefresh: false, cancellationToken);

    /// <summary>
    /// Force a fresh Guardian proxy pass for subsequent HTTP/2 CONNECT streams. Existing CONNECT streams
    /// remain alive; token values are never logged or persisted by this provider.
    /// </summary>
    public Task<string> ForceRefreshAsync(CancellationToken cancellationToken = default) =>
        GetTokenCoreAsync(forceRefresh: true, cancellationToken);

    private async Task<string> GetTokenCoreAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var cachedExpiry = _cachedPass?.ExpiresAtEpochSeconds ?? _fallbackExpiryEpochSeconds;
            if (!forceRefresh && _cachedPass is { Token.Length: > 0 } cached && cachedExpiry > now + 45)
                return cached.Token;

            var accessToken = await _auth.GetFreshAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new UnauthorizedAccessException("Sign in to Firefox Account again before opening an HTTP/2 tunnel.");

            var pass = await _guardian.FetchProxyPassAsync(accessToken, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pass.Token) || pass.Token.Any(char.IsControl) || pass.Token.Any(ch => ch > 0x7F))
                throw new InvalidDataException("Guardian returned a proxy pass that is not a valid HTTP header token.");
            if (pass.ExpiresAtEpochSeconds is { } expires && expires <= now + 15)
                throw new InvalidDataException("Guardian returned a proxy pass that is already expired or too close to expiry.");

            _cachedPass = pass;
            _fallbackExpiryEpochSeconds = pass.ExpiresAtEpochSeconds ?? now + (long)FallbackCacheLifetime.TotalSeconds;
            Interlocked.Exchange(ref _cachedExpiryEpochSeconds, _fallbackExpiryEpochSeconds);
            _log.Info("Guardian", forceRefresh
                ? "Guardian proxy pass refreshed for future HTTP/2 CONNECT streams; token contents were not logged."
                : "A fresh proxy pass is cached for HTTP/2 CONNECT requests; token contents were not logged.");
            return pass.Token;
        }
        finally { _gate.Release(); }
    }

    public void Invalidate()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _gate.Wait();
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            _cachedPass = null;
            _fallbackExpiryEpochSeconds = 0;
            Interlocked.Exchange(ref _cachedExpiryEpochSeconds, 0);
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // Do not dispose the semaphore: a caller that observed the object just before disposal may
        // already be queued on WaitAsync. Such callers acquire the gate, observe disposed, and release it.
        _gate.Wait();
        try
        {
            _cachedPass = null;
            _fallbackExpiryEpochSeconds = 0;
            Interlocked.Exchange(ref _cachedExpiryEpochSeconds, 0);
        }
        finally { _gate.Release(); }
    }
}
