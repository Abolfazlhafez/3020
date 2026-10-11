using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using FoxyVPN.Windows.Services;
using System.Windows;
using System.Windows.Controls;

namespace FoxyVPN.Windows.Views;

public partial class LogsView : UserControl, IRefreshableView
{
    public LogsView()
    {
        InitializeComponent();
        LogList.ItemsSource = App.Services.Log.Entries;
        App.Services.Log.Changed += Log_Changed;
    }

    public Task RefreshAsync()
    {
        if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        return Task.CompletedTask;
    }

    private void Log_Changed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        });
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        App.Services.Log.ClearView();
        StatusText.Text = "The in-memory view was cleared; existing on-disk log history remains preserved.";
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoxyVPN", "logs");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export FoxyVPN diagnostic bundle",
            Filter = "ZIP archive (*.zip)|*.zip",
            FileName = $"FoxyVPN-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            new DiagnosticsExportService().Export(dialog.FileName);
            StatusText.Text = "Diagnostic bundle exported. Review the included log files for server hostnames before sharing.";
            App.Services.Log.Info("Diagnostics", "Diagnostic ZIP bundle exported by the user.");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not export diagnostics: {ex.Message}";
            App.Services.Log.Error("Diagnostics", $"Diagnostic export failed: {ex.Message}");
        }
    }
}
