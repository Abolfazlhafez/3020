namespace FoxyVPN.Windows.Services;

public enum WindowsTunnelState { Disconnected, Starting, Connected, Stopping, Faulted }

/// <summary>Runtime contract to be implemented by the Windows data plane once all transport layers are ready.</summary>
public interface IWindowsTunnelRuntime : IAsyncDisposable
{
    bool IsAvailable { get; }
    bool IsConnected { get; }
    /// <summary>True when cleanup could not prove system routes/firewall are back in a safe state.</summary>
    bool IsSafetyHold => false;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Serializes connect/disconnect operations and reports Connected only after the runtime confirms it.
/// The system runtime is registered, but native/Windows end-to-end acceptance is still required before production use.
/// </summary>
public sealed class WindowsTunnelEngine : IAsyncDisposable
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly IWindowsTunnelRuntime? _runtime;
    private readonly object _disposeSync = new();
    private WindowsTunnelState _state = WindowsTunnelState.Disconnected;
    private bool _disposed;
    private int _disposeStarted;
    private Task? _disposeTask;

    public WindowsTunnelEngine(IWindowsTunnelRuntime? runtime = null) => _runtime = runtime;

    public WindowsTunnelState State
    {
        get
        {
            // A crashed native worker/transport must not leave the UI reporting a healthy connection.
            // Keep the raw state serialized internally; expose Faulted until explicit disconnect recovers it.
            if (_runtime?.IsSafetyHold == true) return WindowsTunnelState.Faulted;
            return _state == WindowsTunnelState.Connected && _runtime?.IsConnected != true
                ? WindowsTunnelState.Faulted
                : _state;
        }
    }
    public bool IsSupported => _runtime?.IsAvailable == true;
    public bool IsConnected => _state == WindowsTunnelState.Connected && _runtime?.IsConnected == true && _runtime.IsSafetyHold != true;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsConnected) return;
            if (_runtime is null)
            {
                _state = WindowsTunnelState.Disconnected;
                throw new TunnelEngineUnavailableException(
                    "The full-system runtime is not registered. No connection was started and no Connected state was reported.");
            }
            if (_runtime.IsSafetyHold)
            {
                _state = WindowsTunnelState.Faulted;
                throw new InvalidOperationException(
                    "FoxyVPN is in a network safety hold because prior route or firewall cleanup is not verified. Use Disconnect/recovery before attempting another connection.");
            }
            if (!_runtime.IsAvailable)
            {
                _state = WindowsTunnelState.Disconnected;
                throw new TunnelEngineUnavailableException(
                    "The full-system runtime is not currently available. Check the elevated Windows session, the bundled HEV/Wintun native dependencies, the selected server, and the diagnostic report. No connection was started and no Connected state was reported.");
            }

            _state = WindowsTunnelState.Starting;
            try
            {
                await _runtime.StartAsync(cancellationToken).ConfigureAwait(false);
                if (!_runtime.IsConnected)
                    throw new TunnelEngineUnavailableException(
                        "The runtime returned from startup without confirming an active tunnel. FoxyVPN will not report Connected.");
                _state = WindowsTunnelState.Connected;
            }
            catch
            {
                var cleanupVerified = false;
                try
                {
                    await _runtime.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    cleanupVerified = !_runtime.IsConnected && !_runtime.IsSafetyHold;
                }
                catch
                {
                    // Preserve the startup exception, but never turn an uncertain network cleanup into Disconnected.
                }
                _state = cancellationToken.IsCancellationRequested && cleanupVerified
                    ? WindowsTunnelState.Disconnected
                    : WindowsTunnelState.Faulted;
                throw;
            }
        }
        finally { _operationGate.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_runtime is null)
            {
                _state = WindowsTunnelState.Disconnected;
                return;
            }
            // A runtime can be disconnected from data flow while still owning routes/firewall rules.
            // Always allow explicit recovery when it reports a safety hold.
            if (_state == WindowsTunnelState.Disconnected && !_runtime.IsConnected && !_runtime.IsSafetyHold) return;
            _state = WindowsTunnelState.Stopping;
            try
            {
                await _runtime.StopAsync(cancellationToken).ConfigureAwait(false);
                var stillConnected = _runtime.IsConnected;
                var safetyHoldRemains = _runtime.IsSafetyHold;
                _state = stillConnected || safetyHoldRemains ? WindowsTunnelState.Faulted : WindowsTunnelState.Disconnected;
                if (stillConnected || safetyHoldRemains)
                    throw new InvalidOperationException(safetyHoldRemains
                        ? "The runtime remains in a network safety hold after disconnect; system route/firewall cleanup is not verified. Check the recovery journal and diagnostics."
                        : "The Windows tunnel runtime still reports active after disconnect.");
            }
            catch
            {
                _state = WindowsTunnelState.Faulted;
                throw;
            }
        }
        finally { _operationGate.Release(); }
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
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                if (_runtime is not null)
                {
                    try { await _runtime.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    await _runtime.DisposeAsync().ConfigureAwait(false);
                }
                _state = _runtime?.IsSafetyHold == true ? WindowsTunnelState.Faulted : WindowsTunnelState.Disconnected;
            }
            catch
            {
                _state = WindowsTunnelState.Faulted;
                throw;
            }
            finally
            {
                _disposed = true;
            }
        }
        finally
        {
            // Keep the gate alive so operations that passed their first disposed check just before
            // disposal can acquire it, re-check state, and unwind without SemaphoreSlim disposal races.
            _operationGate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed || Volatile.Read(ref _disposeStarted) != 0) throw new ObjectDisposedException(nameof(WindowsTunnelEngine));
    }
}

public sealed class TunnelEngineUnavailableException(string message) : Exception(message) { }
