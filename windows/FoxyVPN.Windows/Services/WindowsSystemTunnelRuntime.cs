using System.IO;
using FoxyVPN.Windows.Models;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Principal;
using Microsoft.Win32;

namespace FoxyVPN.Windows.Services;

/// <summary>
/// Coordinates the full system tunnel in dependency order: authenticated HTTP/2 edge -> localhost SOCKS5
/// -> HEV/Wintun -> transactional routes and DNS policy. On shutdown, routes/DNS are rolled back before
/// the transport is stopped. This runtime does not claim success until all layers are active.
/// </summary>
public sealed class WindowsSystemTunnelRuntime : IWindowsTunnelRuntime
{
    private readonly SettingsStore _settings;
    private readonly FirefoxAuthService _auth;
    private readonly GuardianProxyPassProvider _proxyPasses;
    private readonly WindowsHevNativeCore _hev;
    private readonly Action<string, string> _log;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _disposeSync = new();
    private readonly CancellationTokenSource _environmentLifetime = new();
    private readonly object _environmentDebounceSync = new();
    private CancellationTokenSource? _environmentDebounce;
    private CancellationTokenSource? _proxyPassRenewalLifetime;
    private Task? _proxyPassRenewalTask;
    private WindowsProxyTunnelSession? _transport;
    private WindowsSystemRouteTransaction? _routes;
    private bool _connected;
    private bool _disposed;
    private bool _safetyHold;
    private int _disposeStarted;
    private Task? _disposeTask;

    public WindowsSystemTunnelRuntime(
        SettingsStore settings,
        FirefoxAuthService auth,
        GuardianProxyPassProvider proxyPasses,
        WindowsHevNativeCore hev,
        Action<string, string>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _proxyPasses = proxyPasses ?? throw new ArgumentNullException(nameof(proxyPasses));
        _hev = hev ?? throw new ArgumentNullException(nameof(hev));
        _log = logger ?? ((_, _) => { });
        if (OperatingSystem.IsWindows())
        {
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
    }

    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows() || _settings.Current.ProxyOnlyMode) return false;
            return _hev.Inspect().Ready;
        }
    }

    public bool IsConnected
    {
        get
        {
            return Volatile.Read(ref _connected) &&
                   _transport?.IsConnected == true &&
                   _hev.IsRunning &&
                   _routes?.IsActive == true;
        }
    }

    public bool IsSafetyHold => Volatile.Read(ref _safetyHold);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsConnected) return;
            if (_routes is not null || _safetyHold)
                throw new InvalidOperationException("A previous FoxyVPN tunnel still owns system routes or is in safety hold. Choose Disconnect/recovery first; a new connection will not be layered over uncertain routes.");
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The full FoxyVPN tunnel is Windows-only.");
            if (_settings.Current.ProxyOnlyMode)
                throw new InvalidOperationException("Proxy-only mode is enabled in Settings. Disable it before starting the system-wide VPN.");
            if (!_auth.HasSession)
                throw new UnauthorizedAccessException("Sign in to Firefox Account before connecting FoxyVPN.");

            var settings = _settings.Current;
            if (string.IsNullOrWhiteSpace(settings.SelectedServerHost) || settings.SelectedServerPort is < 1 or > 65535)
                throw new InvalidOperationException("Choose and save a live VPN location before connecting.");
            if (!IsCurrentProcessAdministrator())
                throw new UnauthorizedAccessException("Full-system VPN needs administrator privileges to create Wintun and install system routes. Close FoxyVPN and choose Run as administrator. Proxy-only mode does not require elevation.");

            var nativeStatus = _hev.Inspect();
            if (!nativeStatus.Ready) throw new TunnelEngineUnavailableException(nativeStatus.Detail);

            // If a previous process exited abnormally, remove only the routes and NRPT rules recorded in its journal.
            // Recovery runs before opening a new control socket, and refuses to continue if cleanup cannot be proven.
            await WindowsSystemRouteTransaction.RecoverStaleJournalAsync(_log, cancellationToken).ConfigureAwait(false);

            var server = new ServerOption(
                settings.SelectedLocationDisplay,
                settings.SelectedCountryCode,
                string.Empty,
                settings.SelectedCityCode,
                settings.SelectedServerHost,
                settings.SelectedServerPort);

            try
            {
                _transport = await WindowsProxyTunnelSession.StartAsync(
                    server,
                    settings,
                    _proxyPasses,
                    _log,
                    cancellationToken,
                    forSystemTunnel: true).ConfigureAwait(false);

                var localEndpoint = _transport.UpstreamLocalEndPoint
                    ?? throw new IOException("The HTTP/2 session did not expose its physical local endpoint; safe route bypass cannot be established.");
                var remoteEndpoint = _transport.UpstreamRemoteEndPoint
                    ?? throw new IOException("The HTTP/2 session did not expose its remote edge endpoint; safe route bypass cannot be established.");

                await _hev.StartCoreAsync(_transport.LocalProxyPort, cancellationToken).ConfigureAwait(false);
                var tunnelIndexV4 = _hev.InterfaceIndex
                    ?? throw new IOException("HEV reported startup but no Wintun IPv4 interface index could be identified.");
                var tunnelIndexV6 = _hev.InterfaceIndexV6
                    ?? throw new IOException("HEV reported startup but no Wintun IPv6 interface index could be identified. IPv6 fail-closed routing cannot be guaranteed.");
                var tunnelInterfaceId = _hev.InterfaceId
                    ?? throw new IOException("HEV reported startup without the stable Wintun interface identity needed for safe route rollback.");

                _routes = await WindowsSystemRouteTransaction.StartAsync(
                    tunnelIndexV4,
                    tunnelIndexV6,
                    tunnelInterfaceId,
                    remoteEndpoint,
                    localEndpoint,
                    _log,
                    cancellationToken).ConfigureAwait(false);

                if (!_transport.IsConnected || !_hev.IsRunning || !_routes.IsActive)
                    throw new IOException("One of the tunnel layers stopped during final activation. Full VPN connection will not be reported.");

                Volatile.Write(ref _safetyHold, false);
                Volatile.Write(ref _connected, true);
                StartProxyPassRenewal();
                _log("INFO", "Full-system tunnel is active: authenticated HTTP/2 CONNECT, local SOCKS5, HEV/Wintun, IPv4/IPv6 routes and DNS NRPT are running. General UDP is limited by the current TCP-CONNECT-only edge protocol.");
            }
            catch (WindowsRouteTransactionException ex) when (ex.RollbackIncomplete)
            {
                // Do not tear down the data plane while a partial route install may still direct traffic at it.
                Volatile.Write(ref _safetyHold, true);
                Volatile.Write(ref _connected, false);
                _log("ERROR", "Full tunnel entered safety hold because route rollback is incomplete. Keep the HEV and HTTP/2 layers alive and retry cleanup before disconnecting.");
                throw;
            }
            catch
            {
                await StopComponentsAfterCleanRouteFailureAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return;

            // Pause token renewal before removing routes so no new control requests start mid-shutdown.
            // If cleanup fails, retain the forwarding layers and resume renewal when the prior session was healthy.
            var renewalWasActive = _proxyPassRenewalLifetime is not null;
            await StopProxyPassRenewalAsync().ConfigureAwait(false);
            try
            {
                // The DNS rule is removed first; only then are full-tunnel routes removed. If route cleanup fails,
                // keep the transport/core alive instead of removing the forwarding path under active routes.
                if (_routes is not null)
                {
                    await _routes.DisposeAsync().ConfigureAwait(false);
                    _routes = null;
                }
                else if (_safetyHold)
                {
                    await WindowsSystemRouteTransaction.RecoverStaleJournalAsync(_log, cancellationToken, allowCurrentProcessOwner: true).ConfigureAwait(false);
                }
            }
            catch
            {
                if (renewalWasActive && _connected && _transport?.IsConnected == true) StartProxyPassRenewal();
                throw;
            }

            Volatile.Write(ref _connected, false);
            Volatile.Write(ref _safetyHold, false);
            Exception? stopError = null;
            try { await _hev.StopCoreAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { stopError = ex; _log("ERROR", $"HEV stop did not complete ({ex.GetType().Name}); the native module will not be forcibly unloaded while its worker may still be active."); }

            var transport = Interlocked.Exchange(ref _transport, null);
            if (transport is not null)
            {
                try { await transport.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { stopError ??= ex; _log("WARN", $"HTTP/2/SOCKS shutdown reported {ex.GetType().Name}."); }
            }
            if (stopError is not null) throw new InvalidOperationException("The system routes and DNS policy were restored, but one or more VPN components reported a shutdown failure. Check the diagnostic log.", stopError);
            _log("INFO", "System VPN disconnected; DNS policy and the routes owned by this session have been removed.");
        }
        finally { _lifecycle.Release(); }
    }

    private async Task StopComponentsAfterCleanRouteFailureAsync()
    {
        // Only used when route creation reports a clean rollback, so no partial system route should point at HEV.
        await StopProxyPassRenewalAsync().ConfigureAwait(false);
        if (_routes is not null)
        {
            await _routes.DisposeAsync().ConfigureAwait(false);
            _routes = null;
        }
        try { await _hev.StopCoreAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { _log("WARN", $"HEV cleanup after failed startup reported {ex.GetType().Name}; its native worker remains protected from premature library unload."); }
        var transport = Interlocked.Exchange(ref _transport, null);
        if (transport is not null)
        {
            try { await transport.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _log("WARN", $"Transport cleanup after failed startup reported {ex.GetType().Name}."); }
        }
        Volatile.Write(ref _connected, false);
        Volatile.Write(ref _safetyHold, false);
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => QueueEnvironmentHealthCheck("network address changed");

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) =>
        QueueEnvironmentHealthCheck(e.IsAvailable ? "network became available" : "network became unavailable");

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            _log("WARN", "Windows is suspending. The existing route journal and physical-egress firewall guard are retained; health will be reevaluated after resume.");
            return;
        }
        if (e.Mode == PowerModes.Resume) QueueEnvironmentHealthCheck("Windows resumed from sleep/hibernate");
    }

    private void QueueEnvironmentHealthCheck(string reason)
    {
        if (Volatile.Read(ref _disposed) || Volatile.Read(ref _disposeStarted) != 0 || _environmentLifetime.IsCancellationRequested) return;
        CancellationTokenSource debounce;
        lock (_environmentDebounceSync)
        {
            _environmentDebounce?.Cancel();
            _environmentDebounce = CancellationTokenSource.CreateLinkedTokenSource(_environmentLifetime.Token);
            debounce = _environmentDebounce;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(350), debounce.Token).ConfigureAwait(false);
                await EvaluateEnvironmentHealthAsync(reason, debounce.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (debounce.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _log("ERROR", $"Connectivity lifecycle check failed ({ex.GetType().Name}); the route transaction and firewall journal were not automatically removed.");
            }
            finally
            {
                lock (_environmentDebounceSync)
                {
                    if (ReferenceEquals(_environmentDebounce, debounce)) _environmentDebounce = null;
                }
                debounce.Dispose();
            }
        });
    }

    private async Task EvaluateEnvironmentHealthAsync(string reason, CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed || !_connected || _routes is null) return;

            // Expand the persistent egress guard before deciding whether the existing upstream remains usable.
            // Newly connected adapters get no upstream endpoint exception, preventing automatic fail-open on Wi-Fi handover.
            try { await _routes.RefreshPhysicalEgressGuardAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Volatile.Write(ref _connected, false);
                Volatile.Write(ref _safetyHold, true);
                _log("CRITICAL", $"Could not verify Windows Firewall or extend the physical-egress guard after {reason} ({ex.GetType().Name}). The runtime is faulted and routes/journal remain in a safety hold. Restore Windows Firewall, then disconnect/recover explicitly; protection cannot be guaranteed while Firewall is disabled.");
                return;
            }

            var localAddressStillPresent = IsLocalEndpointAssignedToActiveAdapter(_transport?.UpstreamLocalEndPoint);

            if (_transport?.IsConnected == true && _hev.IsRunning && _routes.IsActive && localAddressStillPresent)
            {
                _log("INFO", $"Environment event handled ({reason}); the HTTP/2 transport, HEV worker and route transaction still report healthy. Firewall coverage was refreshed for active adapters.");
                return;
            }

            // Never attempt a transparent rebind here: the active firewall exception is intentionally pinned
            // to the old TCP socket/port. Mark the session faulted and keep routes + kill switch until the user
            // requests an orderly disconnect/recovery, avoiding a reconnect over uncertain route ownership.
            Volatile.Write(ref _connected, false);
            Volatile.Write(ref _safetyHold, true);
            _log("ERROR", $"The VPN lost a healthy transport or its physical local endpoint after {reason}. Full-system status is now faulted. Routes, DNS journal, forwarding components and the physical-egress guard are retained; use Disconnect/recovery before reconnecting.");
        }
        finally { _lifecycle.Release(); }
    }

    private static bool IsLocalEndpointAssignedToActiveAdapter(IPEndPoint? localEndpoint)
    {
        if (localEndpoint is null) return false;
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                .SelectMany(nic =>
                {
                    try { return nic.GetIPProperties().UnicastAddresses.Select(item => item.Address).ToArray(); }
                    catch (NetworkInformationException) { return Array.Empty<IPAddress>(); }
                    catch (PlatformNotSupportedException) { return Array.Empty<IPAddress>(); }
                })
                .Any(address => NormalizeEndpointAddress(address).Equals(NormalizeEndpointAddress(localEndpoint.Address)));
        }
        catch (NetworkInformationException) { return false; }
        catch (PlatformNotSupportedException) { return false; }
    }

    private static IPAddress NormalizeEndpointAddress(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private void StartProxyPassRenewal()
    {
        _proxyPassRenewalLifetime?.Cancel();
        _proxyPassRenewalLifetime?.Dispose();
        var lifetime = new CancellationTokenSource();
        _proxyPassRenewalLifetime = lifetime;
        _proxyPassRenewalTask = Task.Run(() => ProxyPassRenewalLoopAsync(lifetime.Token));
    }

    private async Task ProxyPassRenewalLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var expiry = _proxyPasses.CachedExpiryEpochSeconds;
                await Task.Delay(ProxyPassRenewalSchedule.NextDelay(expiry, now), cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested || !_connected || _transport?.IsConnected != true) return;
                await _proxyPasses.ForceRefreshAsync(cancellationToken).ConfigureAwait(false);
                // Each future CONNECT asks the shared provider for the latest cached pass; current streams stay open.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (GuardianChallengeRequiredException)
            {
                _log("WARN", "Guardian requires user-mediated browser verification before another proxy pass can be issued. Automatic renewal paused; existing CONNECT streams remain untouched. Complete verification in Account and reconnect if new streams fail.");
                return;
            }
            catch (FirefoxChallengeRequiredException)
            {
                _log("WARN", "Firefox Accounts requires user-mediated browser verification to refresh credentials. Automatic proxy-pass renewal paused; existing CONNECT streams remain untouched.");
                return;
            }
            catch (Exception ex)
            {
                _log("WARN", $"Guardian proxy-pass renewal failed ({ex.GetType().Name}); existing HTTP/2 streams remain untouched and renewal will retry.");
                try { await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            }
        }
    }

    private async Task StopProxyPassRenewalAsync()
    {
        var lifetime = _proxyPassRenewalLifetime;
        var task = _proxyPassRenewalTask;
        _proxyPassRenewalLifetime = null;
        _proxyPassRenewalTask = null;
        if (lifetime is null) return;
        lifetime.Cancel();
        if (task is not null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log("WARN", $"Proxy-pass renewal worker stopped with {ex.GetType().Name}."); }
        }
        lifetime.Dispose();
    }

    private void StopEnvironmentMonitoring()
    {
        if (OperatingSystem.IsWindows())
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        _environmentLifetime.Cancel();
        lock (_environmentDebounceSync) _environmentDebounce?.Cancel();
    }

    public static bool IsCurrentProcessAdministrator()
    {
        try { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) || Volatile.Read(ref _disposeStarted) != 0)
            throw new ObjectDisposedException(nameof(WindowsSystemTunnelRuntime));
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            if (_disposeTask is null)
            {
                Interlocked.Exchange(ref _disposeStarted, 1);
                _disposeTask = DisposeCoreAsync();
            }
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        StopEnvironmentMonitoring();
        await _lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StopProxyPassRenewalAsync().ConfigureAwait(false);
            try { await StopAsyncCoreForDisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _log("ERROR", $"Shutdown could not fully clean the system tunnel ({ex.GetType().Name}). Its route recovery journal is retained for the next elevated launch."); }
            Volatile.Write(ref _disposed, true);
        }
        finally
        {
            _lifecycle.Release();
            // Do not dispose these synchronization primitives here. Network-change callbacks or public
            // operations may already have passed a pre-disposal check and be unwinding after cancellation.
            // The runtime is no longer reachable after disposal; keeping these small managed gates alive
            // avoids racing SemaphoreSlim/CTS.Dispose against late continuations.
        }
    }

    private async Task StopAsyncCoreForDisposeAsync()
    {
        if (_routes is not null)
        {
            await _routes.DisposeAsync().ConfigureAwait(false);
            _routes = null;
        }
        else if (_safetyHold)
        {
            await WindowsSystemRouteTransaction.RecoverStaleJournalAsync(_log, CancellationToken.None, allowCurrentProcessOwner: true).ConfigureAwait(false);
        }
        Volatile.Write(ref _connected, false);
        Volatile.Write(ref _safetyHold, false);
        try { await _hev.StopCoreAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception ex) { _log("ERROR", $"HEV shutdown did not finish cleanly ({ex.GetType().Name})."); }
        var transport = Interlocked.Exchange(ref _transport, null);
        if (transport is not null) await transport.DisposeAsync().ConfigureAwait(false);
        await _hev.DisposeAsync().ConfigureAwait(false);
    }
}
