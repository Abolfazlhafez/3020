using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace FoxyVPN.Windows.Services;

public sealed record HevNativeCoreStatus(bool Ready, bool Running, string Detail, int? InterfaceIndex = null);

/// <summary>
/// Thin, serialized binding to the official HEV tun2socks C API.
/// It starts/stops HEV and its Wintun adapter, but intentionally does not install system routes.
/// Do not register it as the full system-VPN runtime until the proxy/HEV chain, route/DNS lifecycle, endpoint bypass, and leak/rollback tests are verified.
/// </summary>
public sealed class WindowsHevNativeCore : IAsyncDisposable
{
    private static readonly TimeSpan AdapterStartTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly string _applicationDirectory;
    private readonly string _configDirectory;
    private readonly Action<string, string> _log;
    private IntPtr _library;
    private HevMainFromFile? _mainFromFile;
    private HevQuit? _quit;
    private Task<int>? _mainTask;
    private string? _configPath;
    private bool _disposed;
    private bool _running;
    private int? _interfaceIndex;
    private int? _interfaceIndexV6;
    private string? _interfaceId;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HevMainFromFile(IntPtr configPathUtf8, int tunFileDescriptor);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void HevQuit();

    public WindowsHevNativeCore(string? applicationDirectory = null, string? configDirectory = null, Action<string, string>? logger = null)
    {
        _applicationDirectory = Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory);
        _configDirectory = Path.GetFullPath(configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FoxyVPN", "runtime"));
        _log = logger ?? ((_, _) => { });
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return !_disposed && _running && _mainTask is { IsCompleted: false } && _interfaceIndex is > 0 && _interfaceIndexV6 is > 0 && !string.IsNullOrWhiteSpace(_interfaceId);
        }
    }

    public int? InterfaceIndex { get { lock (_gate) return _interfaceIndex; } }
    public int? InterfaceIndexV6 { get { lock (_gate) return _interfaceIndexV6; } }
    public string? InterfaceId { get { lock (_gate) return _interfaceId; } }

    public HevNativeCoreStatus Inspect()
    {
        if (!OperatingSystem.IsWindows()) return new(false, false, "The HEV Windows runtime is only available on Windows.");
        if (!IsAdministrator()) return new(false, false, "Starting Wintun and configuring a system VPN requires an elevated Windows process. No native code was loaded.");
        var libraryPath = FindNativeLibraryPath();
        if (libraryPath is null) return new(false, false, "The HEV native library is missing beside the application. Build the pinned Windows native dependency before using full-system VPN mode.");
        var wintunPath = Path.Combine(_applicationDirectory, "wintun.dll");
        if (!File.Exists(wintunPath)) return new(false, false, "The signed Wintun runtime is missing. No native code was loaded.");
        return new(true, IsRunning, $"HEV native library and Wintun DLL are present. Interface creation and packet forwarding have not been started by this preflight. {Path.GetFileName(libraryPath)}");
    }

    /// <summary>
    /// Starts HEV against the loopback SOCKS5 upstream. The caller must keep that upstream alive.
    /// This does not alter routes and must not be surfaced as system-wide VPN Connected.
    /// </summary>
    public async Task StartCoreAsync(int socksPort, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StartCoreLockedAsync(socksPort, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task StartCoreLockedAsync(int socksPort, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_mainTask is { IsCompleted: false })
                throw new InvalidOperationException("The HEV native core is already running.");
        }

        // Reap a previously exited native worker before loading another copy of its process-global library.
        await StopCoreLockedAsync(CancellationToken.None).ConfigureAwait(false);

        var status = Inspect();
        if (!status.Ready) throw new TunnelEngineUnavailableException(status.Detail);
        var libraryPath = FindNativeLibraryPath()!;
        var configPath = await WindowsHevConfigWriter.WriteAsync(_configDirectory, socksPort, cancellationToken).ConfigureAwait(false);
        lock (_gate) _configPath = configPath;

        IntPtr library = IntPtr.Zero;
        try
        {
            library = NativeLibrary.Load(libraryPath);
            var mainPointer = NativeLibrary.GetExport(library, "hev_socks5_tunnel_main_from_file");
            var quitPointer = NativeLibrary.GetExport(library, "hev_socks5_tunnel_quit");
            var main = Marshal.GetDelegateForFunctionPointer<HevMainFromFile>(mainPointer);
            var quit = Marshal.GetDelegateForFunctionPointer<HevQuit>(quitPointer);

            lock (_gate)
            {
                ThrowIfDisposed();
                _library = library;
                _mainFromFile = main;
                _quit = quit;
                library = IntPtr.Zero; // ownership transferred to this instance
                _mainTask = Task.Run(() => InvokeMain(main, configPath), CancellationToken.None);
            }

            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < AdapterStartTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task<int>? task;
                lock (_gate) task = _mainTask;
                if (task is null) throw new InvalidOperationException("The HEV native task was not started.");
                if (task.IsCompleted)
                {
                    var exitCode = await task.ConfigureAwait(false);
                    throw new InvalidOperationException($"The HEV native engine exited before its Wintun interface became ready (code {exitCode}). Check native runtime dependencies and the tunnel log.");
                }

                var networkInterface = FindTunnelInterface();
                if (networkInterface is not null)
                {
                    var properties = networkInterface.GetIPProperties();
                    var index = properties.GetIPv4Properties()?.Index;
                    var indexV6 = properties.GetIPv6Properties()?.Index;
                    // Full-system mode routes both address families. Do not activate if Windows does not
                    // expose both family-specific interface indices; otherwise IPv6 could bypass the tunnel.
                    if (index is > 0 && indexV6 is > 0)
                    {
                        lock (_gate)
                        {
                            _interfaceIndex = index;
                            _interfaceIndexV6 = indexV6;
                            _interfaceId = networkInterface.Id;
                            _running = true;
                        }
                        _log("INFO", $"HEV native tun2socks core is running; Wintun IPv4/IPv6 interface indices are {index}/{indexV6}. System routes have not been changed.");
                        return;
                    }
                }
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException("The HEV native core did not expose its configured Wintun interface within 20 seconds.");
        }
        catch
        {
            try { await StopCoreLockedAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception stopError) { _log("ERROR", $"HEV cleanup after startup failure was incomplete ({stopError.GetType().Name}). The native library remains loaded if its worker is still active."); }
            throw;
        }
        finally
        {
            if (library != IntPtr.Zero) NativeLibrary.Free(library);
        }
    }

    public async Task StopCoreAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task StopCoreLockedAsync(CancellationToken cancellationToken)
    {
        Task<int>? task;
        HevQuit? quit;
        bool hadState;
        lock (_gate)
        {
            task = _mainTask;
            quit = _quit;
            hadState = task is not null || _configPath is not null || _library != IntPtr.Zero;
        }
        if (!hadState) return;

        if (task is { IsCompleted: false })
        {
            try { quit?.Invoke(); }
            catch (Exception ex) { _log("WARN", $"HEV stop request failed: {ex.GetType().Name}."); }
            try
            {
                await task.WaitAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Do not unload native code while its native worker can still execute inside it.
                throw new TimeoutException("HEV did not stop within 10 seconds. Its native library remains loaded to avoid invalid native callbacks.");
            }
            catch (OperationCanceledException)
            {
                // A caller-cancelled stop must not free native code while the worker is still active.
                throw;
            }
            catch (Exception ex)
            {
                // The task faulted/exited; continue cleanup and free the loaded module now that it is safe.
                _log("WARN", $"HEV worker exited with {ex.GetType().Name}; continuing native cleanup.");
            }
        }
        else if (task is not null)
        {
            // Observe any native-task exception so shutdown doesn't produce an unobserved task fault.
            try { await task.ConfigureAwait(false); }
            catch (Exception ex) { _log("WARN", $"HEV worker had already exited with {ex.GetType().Name}; its native library will now be released."); }
        }

        string? configPath;
        lock (_gate)
        {
            _mainTask = null;
            _mainFromFile = null;
            _quit = null;
            _running = false;
            _interfaceIndex = null;
            _interfaceIndexV6 = null;
            _interfaceId = null;
            configPath = _configPath;
            _configPath = null;
            CleanupLoadedLibraryIfStopped();
        }

        if (configPath is not null)
        {
            try { File.Delete(configPath); }
            catch (Exception ex) { _log("WARN", $"Could not remove temporary HEV config ({ex.GetType().Name})."); }
        }
        _log("INFO", "HEV native tun2socks core stopped. Route cleanup remains owned by the separate system-tunnel lifecycle.");
    }

    private static int InvokeMain(HevMainFromFile main, string configPath)
    {
        var utf8Path = Marshal.StringToCoTaskMemUTF8(configPath);
        try
        {
            // A negative descriptor tells HEV to create and own the Wintun adapter from the YAML config.
            return main(utf8Path, -1);
        }
        finally { Marshal.FreeCoTaskMem(utf8Path); }
    }

    private NetworkInterface? FindTunnelInterface()
    {
        var guid = WindowsHevConfigWriter.AdapterGuid;
        var expectedAddress = IPAddress.Parse(WindowsHevConfigWriter.TunnelIpv4);
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(item =>
            {
                if (item.Id.Contains(guid, StringComparison.OrdinalIgnoreCase)) return true;
                try { return item.GetIPProperties().UnicastAddresses.Any(address => address.Address.Equals(expectedAddress)); }
                catch { return false; }
            });
        }
        catch (NetworkInformationException) { return null; }
    }

    private string? FindNativeLibraryPath()
    {
        var candidates = new[]
        {
            Path.Combine(_applicationDirectory, "hev-socks5-tunnel.dll"),
            Path.Combine(_applicationDirectory, "libhev-socks5-tunnel.dll"),
            Path.Combine(_applicationDirectory, "libhev-socks5-tunnel.so"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private void CleanupLoadedLibraryIfStopped()
    {
        if (_mainTask is { IsCompleted: false }) return;
        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
        }
        _mainFromFile = null;
        _quit = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsHevNativeCore));
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
            }
            await StopCoreLockedAsync(CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                _disposed = true;
                CleanupLoadedLibraryIfStopped();
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }
}
