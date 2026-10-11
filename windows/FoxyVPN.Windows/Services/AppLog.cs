using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;

namespace FoxyVPN.Windows.Services;

public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Tag, string Message)
{
    public string Display => $"{Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {Level,-5}  [{Tag}]  {Message}";
}

public sealed class AppLog
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly ObservableCollection<LogEntry> _entries = new();
    public ReadOnlyObservableCollection<LogEntry> Entries { get; }
    public event EventHandler? Changed;

    public AppLog()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoxyVPN", "logs");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "foxyvpn.log");
        Entries = new ReadOnlyObservableCollection<LogEntry>(_entries);
        LoadRecent();
    }

    public void Info(string tag, string message) => Write("INFO", tag, message);
    public void Warn(string tag, string message) => Write("WARN", tag, message);
    public void Error(string tag, string message) => Write("ERROR", tag, message);

    private void Write(string level, string tag, string message)
    {
        var entry = new LogEntry(DateTimeOffset.UtcNow, level, tag, message);
        lock (_sync)
        {
            _entries.Add(entry);
            while (_entries.Count > 2000) _entries.RemoveAt(0);
            try
            {
                File.AppendAllText(_path, $"{entry.Timestamp:O}\t{level}\t{Sanitize(tag)}\t{Sanitize(message)}{Environment.NewLine}");
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > 4 * 1024 * 1024)
                {
                    var old = _path + ".1";
                    File.Delete(old);
                    File.Move(_path, old);
                }
            }
            catch { /* Logging must not crash the app. */ }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ClearView()
    {
        lock (_sync) _entries.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void LoadRecent()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var lines = File.ReadLines(_path).TakeLast(2000);
            foreach (var line in lines)
            {
                var parts = line.Split('\t', 4);
                if (parts.Length == 4 && DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                    _entries.Add(new LogEntry(timestamp, parts[1], parts[2], parts[3]));
            }
        }
        catch { }
    }

    private static string Sanitize(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
}
