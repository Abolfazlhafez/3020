using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FoxyVPN.Windows.Models;

namespace FoxyVPN.Windows.Services;

public sealed class SettingsStore
{
    private readonly object _sync = new();
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; private set; }

    public SettingsStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoxyVPN");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.bin");
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var encrypted = File.ReadAllBytes(_path);
            var plain = ProtectedData.Unprotect(encrypted, Encoding.UTF8.GetBytes("FoxyVPN.Windows.Settings.v1"), DataProtectionScope.CurrentUser);
            try
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(plain, JsonOptions) ?? new AppSettings();
                Normalize(loaded);
                return loaded;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch
        {
            // Corrupt settings are not allowed to prevent startup. A backup is preserved for diagnosis.
            try { File.Move(_path, _path + ".corrupt-" + DateTimeOffset.Now.ToString("yyyyMMddHHmmss"), true); } catch { }
            return new AppSettings();
        }
    }

    public void Update(Action<AppSettings> update)
    {
        lock (_sync)
        {
            update(Current);
            Normalize(Current);
            SaveAtomically(Current);
        }
    }

    public void Save()
    {
        lock (_sync)
        {
            Normalize(Current);
            SaveAtomically(Current);
        }
    }

    private void SaveAtomically(AppSettings settings)
    {
        var temp = _path + ".tmp";
        var plain = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        byte[]? encrypted = null;
        try
        {
            encrypted = ProtectedData.Protect(plain, Encoding.UTF8.GetBytes("FoxyVPN.Windows.Settings.v1"), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temp, encrypted);
            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    private static void Normalize(AppSettings value)
    {
        value.ThemeMode = value.ThemeMode is "Light" or "Dark" ? value.ThemeMode : "System";
        value.ServerRefreshIntervalMinutes = value.ServerRefreshIntervalMinutes is 5 or 15 or 30 ? value.ServerRefreshIntervalMinutes : 15;
        value.FavoriteServerKeys ??= new List<string>();
        value.FavoriteServerKeys = value.FavoriteServerKeys.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(200).ToList();
        value.SocksPort = Math.Clamp(value.SocksPort, 1, 65535);
        value.UpstreamProxyPort = Math.Clamp(value.UpstreamProxyPort, 1, 65535);
        // This build has no SOCKS authentication for LAN clients; always constrain the local proxy to loopback.
        value.SocksBindAddress = "127.0.0.1";
        value.DohProvider = value.DohProvider is "Automatic" or "Cloudflare" or "Google" or "Quad9" or "Off" ? value.DohProvider : "Automatic";
        value.UpstreamProxyType = value.UpstreamProxyType is "HTTP" or "SOCKS5" ? value.UpstreamProxyType : "SOCKS5";
        value.CustomDnsServer = IsIpAddress(value.CustomDnsServer) ? value.CustomDnsServer.Trim() : "1.1.1.1";
        value.CustomEdgeAddress = IsIpAddress(value.CustomEdgeAddress) || IsValidHostname(value.CustomEdgeAddress) ? value.CustomEdgeAddress.Trim().TrimEnd('.').ToLowerInvariant() : "";
        value.UpstreamProxyHost = value.UpstreamProxyHost?.Trim() ?? "";
        value.UpstreamProxyUsername ??= "";
        value.UpstreamProxyPassword ??= "";
        value.SelectedCountryCode = string.IsNullOrWhiteSpace(value.SelectedCountryCode) ? "REC" : value.SelectedCountryCode.Trim().ToUpperInvariant();
        value.SelectedCityCode ??= "";
        value.SelectedLocationDisplay = string.IsNullOrWhiteSpace(value.SelectedLocationDisplay) ? "Recommended location" : value.SelectedLocationDisplay.Trim();
        value.SelectedServerHost ??= "";
        value.SelectedServerPort = value.SelectedServerPort is >= 1 and <= 65535 ? value.SelectedServerPort : 0;
    }

    public static bool IsIpAddress(string? value) => IPAddress.TryParse(value?.Trim(), out _);

    public static bool IsValidHostname(string? value)
    {
        var host = value?.Trim().TrimEnd('.') ?? "";
        if (host.Length is < 1 or > 253 || host.Contains(':') || host.All(c => char.IsDigit(c) || c == '.')) return false;
        var labels = host.Split('.');
        return labels.Length >= 2 && labels.All(label => label.Length is > 0 and <= 63 &&
            !label.StartsWith('-') && !label.EndsWith('-') &&
            label.All(c => c <= 127 && (char.IsLetterOrDigit(c) || c == '-')));
    }
}
