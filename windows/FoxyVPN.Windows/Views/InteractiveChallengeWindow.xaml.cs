using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using FoxyVPN.Windows.Services;

namespace FoxyVPN.Windows.Views;

/// <summary>Interactive service challenge UI. Only the user completes the page; this code never fabricates challenge answers.</summary>
public partial class InteractiveChallengeWindow : Window
{
    private readonly Uri _origin;
    private bool _initializing;
    private bool _initialized;
    private bool _navigationBlockedNoticeShown;

    public IReadOnlyList<InteractiveChallengeCookie> CapturedCookies { get; private set; } = Array.Empty<InteractiveChallengeCookie>();

    public InteractiveChallengeWindow(Uri origin)
    {
        if (!InteractiveChallengeCookieBridge.IsAllowedOrigin(origin))
            throw new ArgumentException("Only approved HTTPS Firefox/Guardian challenge origins are supported.", nameof(origin));
        _origin = new Uri(origin.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
        InitializeComponent();
        var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoxyVPN", "challenge-webview", _origin.Host);
        Directory.CreateDirectory(userDataFolder);
        ChallengeBrowser.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = userDataFolder };
        OriginText.Text = _origin.GetLeftPart(UriPartial.Authority);
        Loaded += Window_Loaded;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await InitializeBrowserAsync();

    private async Task InitializeBrowserAsync()
    {
        if (_initializing || _initialized) return;
        _initializing = true;
        try
        {
            await ChallengeBrowser.EnsureCoreWebView2Async();
            var core = ChallengeBrowser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.NavigationStarting += Core_NavigationStarting;
            core.NavigationCompleted += Core_NavigationCompleted;
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var popupUri) &&
                    popupUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                    popupUri.IsDefaultPort && IsApprovedChallengeNavigationHost(popupUri.Host))
                {
                    core.Navigate(popupUri.AbsoluteUri);
                    return;
                }
                StatusText.Text = "A pop-up outside approved HTTPS Firefox/Guardian service domains was blocked.";
            };
            _initialized = true;
            StatusText.Text = "Secure page ready. Complete the service’s own verification if it is shown.";
            core.Navigate(_origin.AbsoluteUri);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"The secure verification view could not start ({ex.GetType().Name}). Install or repair the Microsoft Edge WebView2 Runtime, then retry.";
            CompleteButton.IsEnabled = false;
        }
        finally { _initializing = false; }
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) ||
            !target.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !target.IsDefaultPort || !IsApprovedChallengeNavigationHost(target.Host))
        {
            e.Cancel = true;
            if (!_navigationBlockedNoticeShown)
            {
                _navigationBlockedNoticeShown = true;
                StatusText.Text = "Navigation outside the approved HTTPS Firefox/Guardian service domains was blocked.";
            }
        }
    }

    private static bool IsApprovedChallengeNavigationHost(string host) =>
        host.Equals("firefox.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".firefox.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("mozilla.org", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".mozilla.org", StringComparison.OrdinalIgnoreCase);

    private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            StatusText.Text = "Page loaded. If a challenge is visible, complete it; then retry the original request.";
            CompleteButton.IsEnabled = true;
        }
        else
        {
            StatusText.Text = "The page did not finish loading. Check connectivity and retry, or cancel and return to FoxyVPN.";
            CompleteButton.IsEnabled = _initialized;
        }
    }

    private async void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        CompleteButton.IsEnabled = false;
        StatusText.Text = "Collecting eligible service cookies…";
        try
        {
            var core = ChallengeBrowser.CoreWebView2;
            var cookies = await core.CookieManager.GetCookiesAsync(_origin.AbsoluteUri);
            var collected = new List<InteractiveChallengeCookie>(Math.Min(cookies.Count, 128));
            foreach (var cookie in cookies.Take(129))
            {
                DateTimeOffset? expires = null;
                if (!cookie.IsSession && cookie.Expires > DateTime.MinValue)
                {
                    try
                    {
                        var utcExpiry = DateTime.SpecifyKind(cookie.Expires, DateTimeKind.Utc);
                        expires = new DateTimeOffset(utcExpiry);
                    }
                    catch (ArgumentOutOfRangeException) { continue; }
                }
                collected.Add(new InteractiveChallengeCookie(cookie.Name, cookie.Value, cookie.Domain,
                    string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
                    cookie.IsHttpOnly, cookie.IsSecure, cookie.IsSession, expires));
            }
            if (collected.Count > 128) throw new InvalidDataException("The service returned too many cookies for a safe challenge handoff.");
            CapturedCookies = collected;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not capture verification state ({ex.GetType().Name}). No cookies were transferred.";
            CompleteButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CapturedCookies = Array.Empty<InteractiveChallengeCookie>();
        DialogResult = false;
    }
}
