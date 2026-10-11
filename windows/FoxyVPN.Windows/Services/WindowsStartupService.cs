using System.IO;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;

namespace FoxyVPN.Windows.Services;

/// <summary>Manages per-user Windows sign-in startup without requiring administrator rights.</summary>
public static class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FoxyVPN";

    public static void Apply(bool enabled, bool startMinimized)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows could not open the current user's startup registry key.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("Windows could not determine the FoxyVPN executable path.");

        var args = startMinimized ? " --minimized" : string.Empty;
        // Framework-dependent development launches may be hosted by dotnet.exe.
        // In that case also pass the app assembly so the startup entry is executable.
        var fileName = Path.GetFileNameWithoutExtension(processPath);
        string command;
        if (fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssembly = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssembly) || !File.Exists(entryAssembly))
                throw new InvalidOperationException("Cannot configure auto-start from this development host. Publish the Windows app and retry.");
            command = $"\"{processPath}\" \"{entryAssembly}\"{args}";
        }
        else
        {
            command = $"\"{processPath}\"{args}";
        }

        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch { return false; }
    }
}
