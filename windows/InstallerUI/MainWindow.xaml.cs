using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FoxyVPN.InstallerUI;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private double _progressPosition;
    private bool _installStarted;
    private bool _installFinished;

    public MainWindow()
    {
        InitializeComponent();
        _progressTimer.Tick += (_, _) =>
        {
            _progressPosition += 5;
            if (_progressPosition > 520) _progressPosition = -110;
            ProgressTranslate.X = _progressPosition;
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // A subtle glass-surface animation; all progress/status text remains truthful.
        var pulse = new DoubleAnimation(0.72, 1.0, TimeSpan.FromSeconds(2.4))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        IndeterminateTrack.BeginAnimation(OpacityProperty, pulse);
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installStarted || _installFinished) return;
        _installStarted = true;
        InstallButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
        StatusTitle.Text = "Installing FoxyVPN";
        StatusDescription.Text = "Please keep this window open while setup runs. Windows may ask for administrator permission.";
        SubtitleText.Text = "Setting up your safer internet.";
        StepOneIcon.Text = "✓";
        StepOneIcon.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
        StepTwoIcon.Text = "◌";
        StepTwoIcon.Foreground = System.Windows.Media.Brushes.White;
        IndeterminateTrack.Visibility = Visibility.Visible;
        _progressTimer.Start();

        string? setupPath = null;
        try
        {
            await Task.Run(() =>
            {
                using Stream resource = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("FoxyVPN.Setup.exe")
                    ?? throw new InvalidOperationException("The embedded FoxyVPN setup package was not found.");
                setupPath = Path.Combine(Path.GetTempPath(), $"FoxyVPN-Setup-{Guid.NewGuid():N}.exe");
                using var output = File.Create(setupPath);
                resource.CopyTo(output);
            });

            var logPath = Path.Combine(Path.GetTempPath(), "FoxyVPN-Setup.log");
            var startInfo = new ProcessStartInfo
            {
                FileName = setupPath!,
                Arguments = $"/LOG=\"{logPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetTempPath()
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows could not start the setup process.");
            await process.WaitForExitAsync();

            _progressTimer.Stop();
            IndeterminateTrack.Visibility = Visibility.Collapsed;
            _installFinished = process.ExitCode == 0;
            CloseButton.IsEnabled = true;
            ExitButton.Content = _installFinished ? "Finish" : "Close";

            if (process.ExitCode == 0)
            {
                StatusTitle.Text = "You're all set";
                StatusDescription.Text = "FoxyVPN was installed successfully. You can now close this installer and launch FoxyVPN.";
                SubtitleText.Text = "Welcome to a safer internet.";
                StepTwoIcon.Text = "✓";
                StepTwoIcon.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
                StepThreeIcon.Text = "✓";
                StepThreeIcon.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
                ProgressTrack.Width = 10000;
                InstallButton.Content = "Installed";
            }
            else
            {
                StatusTitle.Text = "Setup did not finish";
                StatusDescription.Text = $"The Windows setup process exited with code {process.ExitCode}. Please try again or review the setup log in your temporary folder.";
                StepTwoIcon.Text = "!";
                StepTwoIcon.Foreground = System.Windows.Media.Brushes.Orange;
                InstallButton.IsEnabled = true;
                InstallButton.Content = "Try again";
                _installStarted = false;
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _progressTimer.Stop();
            IndeterminateTrack.Visibility = Visibility.Collapsed;
            StatusTitle.Text = "Installation cancelled";
            StatusDescription.Text = "Administrator permission was declined. FoxyVPN has not been installed.";
            StepTwoIcon.Text = "–";
            InstallButton.IsEnabled = true;
            InstallButton.Content = "Try again";
            CloseButton.IsEnabled = true;
            _installStarted = false;
            if (setupPath is not null) TryDelete(setupPath);
        }
        catch (Exception ex)
        {
            _progressTimer.Stop();
            IndeterminateTrack.Visibility = Visibility.Collapsed;
            StatusTitle.Text = "Could not start setup";
            StatusDescription.Text = ex.Message;
            InstallButton.IsEnabled = true;
            InstallButton.Content = "Try again";
            CloseButton.IsEnabled = true;
            _installStarted = false;
            if (setupPath is not null) TryDelete(setupPath);
        }
        finally
        {
            if (setupPath is not null) TryDelete(setupPath);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installStarted && !_installFinished) return;
        Close();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* Temporary cleanup is best-effort. */ }
    }
}