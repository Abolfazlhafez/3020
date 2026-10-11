using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FoxyVPN.Windows.Services;
using FoxyVPN.Windows.Views;

namespace FoxyVPN.Windows;

public partial class MainWindow : Window
{
    private readonly HomeView _home;
    private readonly LoginView _login;
    private readonly ServersView _servers;
    private readonly AccountView _account;
    private readonly SettingsView _settings;
    private readonly LogsView _logs;
    private Button? _activeNav;
    private string _currentPage = "";
    private bool _statusDotPulsing;
    private bool _sessionEnding;

    public MainWindow()
    {
        InitializeComponent();
        _home = new HomeView();
        _login = new LoginView();
        _servers = new ServersView();
        _account = new AccountView();
        _settings = new SettingsView();
        _logs = new LogsView();
        _home.RequestNavigation += target => Navigate(target);
        _login.AuthenticationChanged += (_, _) => RefreshStatus();
        App.Services.Log.Changed += (_, _) => Dispatcher.Invoke(RefreshStatus);
        Application.Current.SessionEnding += (_, _) => _sessionEnding = true;
        Navigate("Home", animate: false);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        var services = App.Services;
        if (e.Cancel || _sessionEnding || services is null || !services.Settings.Current.ConfirmDisconnect) return;
        if (!services.IsProxyOnlySessionActive && !services.Tunnel.IsConnected) return;
        var answer = MessageBox.Show(this,
            "FoxyVPN is connected. Closing the app will disconnect it and restore the routes and DNS settings it changed. Close anyway?",
            "FoxyVPN", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) e.Cancel = true;
    }

    private void Navigate(string page, bool animate = true)
    {
        if (page == _currentPage && PageHost.Content is not null)
        {
            if (PageHost.Content is IRefreshableView currentView) _ = currentView.RefreshAsync();
            return;
        }
        PageHost.Content = page switch
        {
            "Home" => _home,
            "Locations" => _servers,
            "Firefox account" => _login,
            "Account usage" => _account,
            "Settings" => _settings,
            "Logs" => _logs,
            _ => _home
        };
        _currentPage = page;
        HeaderTitle.Text = page switch { "Home" => "Overview", "Logs" => "Diagnostics", _ => page };
        _activeNav = page switch
        {
            "Home" => HomeNav,
            "Locations" => ServersNav,
            "Firefox account" => LoginNav,
            "Account usage" => AccountNav,
            "Settings" => SettingsNav,
            "Logs" => LogsNav,
            _ => HomeNav
        };
        StyleNav(HomeNav, HomeNav == _activeNav, animate);
        StyleNav(ServersNav, ServersNav == _activeNav, animate);
        StyleNav(LoginNav, LoginNav == _activeNav, animate);
        StyleNav(AccountNav, AccountNav == _activeNav, animate);
        StyleNav(SettingsNav, SettingsNav == _activeNav, animate);
        StyleNav(LogsNav, LogsNav == _activeNav, animate);
        RefreshStatus();
        AnimatePage(animate);
        if (PageHost.Content is IRefreshableView refreshable) _ = refreshable.RefreshAsync();
    }

    private void AnimatePage(bool requested)
    {
        PageHost.BeginAnimation(OpacityProperty, null);
        if (!requested || !Motion.Enabled)
        {
            PageHost.Opacity = 1;
            PageHost.RenderTransform = Transform.Identity;
            return;
        }
        var slide = new TranslateTransform(0, 18);
        var scale = new ScaleTransform(0.985, 0.985);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(slide);
        PageHost.RenderTransformOrigin = new Point(0.5, 0.35);
        PageHost.RenderTransform = group;
        PageHost.Opacity = 0;
        var soft = new CubicEase { EasingMode = EasingMode.EaseOut };
        var settle = new QuinticEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = soft });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = settle });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = settle });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = settle });
    }

    public void RefreshNavigationTheme()
    {
        StyleNav(HomeNav, HomeNav == _activeNav);
        StyleNav(ServersNav, ServersNav == _activeNav);
        StyleNav(LoginNav, LoginNav == _activeNav);
        StyleNav(AccountNav, AccountNav == _activeNav);
        StyleNav(SettingsNav, SettingsNav == _activeNav);
        StyleNav(LogsNav, LogsNav == _activeNav);
    }

    private static void StyleNav(Button button, bool selected, bool animate = false)
    {
        var resources = Application.Current.Resources;
        var selectedColor = (resources["SelectedNavBrush"] as SolidColorBrush)?.Color ?? Colors.Transparent;
        var clear = Color.FromArgb(0, selectedColor.R, selectedColor.G, selectedColor.B);
        var toColor = selected ? selectedColor : clear;
        var animated = animate && Motion.Enabled;
        var fromColor = (button.Background as SolidColorBrush)?.Color ?? clear;
        // A private brush per button, so the shared theme brush is never animated.
        var brush = new SolidColorBrush(animated ? fromColor : toColor);
        button.Background = brush;
        if (animated)
            brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(toColor, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        button.Foreground = selected ? (Brush)resources["AccentBrush"] : (Brush)resources["PrimaryTextBrush"];
        button.BorderBrush = selected ? (Brush)resources["SelectedNavBrush"] : Brushes.Transparent;
    }

    private void RefreshStatus()
    {
        var signedIn = App.Services.Auth.HasSession;
        var tunnelActive = App.Services.Tunnel.IsConnected;
        HeaderStatus.Text = tunnelActive
            ? "System tunnel active · TCP + DNS; general UDP restricted"
            : signedIn ? "Account signed in · VPN offline" : "Sign in to connect FoxyVPN";
        HeaderStatusDot.Fill = tunnelActive
            ? (Brush)new SolidColorBrush(Color.FromRgb(79, 190, 153))
            : signedIn ? (Brush)new SolidColorBrush(Color.FromRgb(232, 169, 71)) : (Brush)new SolidColorBrush(Color.FromRgb(207, 112, 137));
        if (tunnelActive != _statusDotPulsing)
        {
            _statusDotPulsing = tunnelActive;
            Motion.Breathe(HeaderStatusDot, tunnelActive);
        }
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e) => Navigate("Home");
    private void ServersNav_Click(object sender, RoutedEventArgs e) => Navigate("Locations");
    private void LoginNav_Click(object sender, RoutedEventArgs e) => Navigate("Firefox account");
    private void AccountNav_Click(object sender, RoutedEventArgs e) => Navigate("Account usage");
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => Navigate("Settings");
    private void LogsNav_Click(object sender, RoutedEventArgs e) => Navigate("Logs");
}
