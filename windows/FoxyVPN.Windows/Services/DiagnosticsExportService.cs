using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace FoxyVPN.Windows.Services;

public sealed class DiagnosticsExportService
{
    public void Export(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("A destination path is required.", nameof(destinationPath));
        var settings = App.Services.Settings.Current;
        var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var proxyPort = App.Services.ProxyOnlyPort?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "not active";
        var report = new StringBuilder()
            .AppendLine("FoxyVPN Windows diagnostic report")
            .AppendLine($"Generated UTC: {DateTimeOffset.UtcNow:O}")
            .AppendLine($"App version: {assemblyVersion}")
            .AppendLine($"OS: {Environment.OSVersion}")
            .AppendLine($"OS 64-bit: {Environment.Is64BitOperatingSystem}")
            .AppendLine($"Process architecture: {(Environment.Is64BitProcess ? "x64" : "x86")}")
            .AppendLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}")
            .AppendLine($"Firefox session present: {App.Services.Auth.HasSession}")
            .AppendLine($"Windows tunnel engine supported (preflight): {App.Services.Tunnel.IsSupported}")
            .AppendLine($"Windows system tunnel currently connected: {App.Services.Tunnel.IsConnected}")
            .AppendLine($"Local proxy-only session active: {App.Services.IsProxyOnlySessionActive}")
            .AppendLine($"Theme: {settings.ThemeMode}")
            .AppendLine($"Start with Windows (saved setting): {settings.StartWithWindows}")
            .AppendLine($"Start minimized: {settings.StartMinimized}")
            .AppendLine($"Auto-refresh server catalog: {settings.AutoRefreshServers}")
            .AppendLine($"Refresh interval minutes: {settings.ServerRefreshIntervalMinutes}")
            .AppendLine($"Selected country/city codes: {settings.SelectedCountryCode}/{settings.SelectedCityCode}")
            .AppendLine($"Favorite location count: {settings.FavoriteServerKeys?.Count ?? 0}")
            .AppendLine($"Local SOCKS5 proxy port when active: {proxyPort}")
            .AppendLine($"Proxy-only mode configured: {settings.ProxyOnlyMode}")
            .AppendLine()
            .AppendLine("Privacy note: tokens, passwords, account IDs, and saved endpoint hostnames are omitted from this report.")
            .AppendLine("The included application log may contain server hostnames and error details. Review it before sharing.")
            .ToString();

        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoxyVPN", "logs");
        var currentLog = Path.Combine(logDir, "foxyvpn.log");
        var previousLog = currentLog + ".1";

        using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
        var reportEntry = archive.CreateEntry("diagnostics.txt", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(reportEntry.Open(), new UTF8Encoding(false))) writer.Write(report);
        AddLogIfPresent(archive, currentLog, "foxyvpn.log");
        AddLogIfPresent(archive, previousLog, "foxyvpn.previous.log");
    }

    private static void AddLogIfPresent(ZipArchive archive, string source, string entryName)
    {
        if (!File.Exists(source)) return;
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var output = entry.Open();
        using var input = File.OpenRead(source);
        input.CopyTo(output);
    }
}
