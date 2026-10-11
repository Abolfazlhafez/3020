using System.IO;
using FoxyVPN.Windows.Models;

namespace FoxyVPN.Windows.Services;

public interface IWindowsTunRuntimeProbe
{
    TunRuntimeStatus Inspect(string applicationDirectory);
}

/// <summary>
/// Non-invasive preflight for the pinned HEV native library and signed Wintun DLL.
/// It never loads native code or changes routes; files being present is not a connectivity test.
/// </summary>
public sealed class WindowsTunRuntimeProbe : IWindowsTunRuntimeProbe
{
    public TunRuntimeStatus Inspect(string applicationDirectory)
    {
        if (!OperatingSystem.IsWindows())
            return new TunRuntimeStatus(false, "The HEV/Wintun runtime is available only on Windows.");
        if (string.IsNullOrWhiteSpace(applicationDirectory))
            return new TunRuntimeStatus(false, "The application directory could not be determined.");

        string normalizedDirectory;
        try { normalizedDirectory = Path.GetFullPath(applicationDirectory); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new TunRuntimeStatus(false, "The application directory is invalid.");
        }

        var hevCandidates = new[]
        {
            Path.Combine(normalizedDirectory, "hev-socks5-tunnel.dll"),
            Path.Combine(normalizedDirectory, "libhev-socks5-tunnel.dll"),
            Path.Combine(normalizedDirectory, "libhev-socks5-tunnel.so"),
        };
        var hev = hevCandidates.FirstOrDefault(File.Exists);
        var wintun = Path.Combine(normalizedDirectory, "wintun.dll");
        if (hev is null)
            return new TunRuntimeStatus(false,
                "The pinned HEV tun2socks native library is not beside the application. The Windows native build/bundle step must supply it before system-tunnel mode can be considered.");
        if (!File.Exists(wintun))
            return new TunRuntimeStatus(false,
                "The HEV native library exists but the official signed wintun.dll runtime is missing. No native library was loaded.");

        var hevInfo = new FileInfo(hev);
        var wintunInfo = new FileInfo(wintun);
        return new TunRuntimeStatus(true,
            $"HEV native library ({hevInfo.Length:N0} bytes) and wintun.dll ({wintunInfo.Length:N0} bytes) are present. Export compatibility, driver startup, UDP/DNS relay, system routes and end-to-end forwarding are not proven by this file check.");
    }
}
