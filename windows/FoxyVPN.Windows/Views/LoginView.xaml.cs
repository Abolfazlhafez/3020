using System.Windows;
using System.Windows.Controls;
using FoxyVPN.Windows.Services;

namespace FoxyVPN.Windows.Views;

public partial class LoginView : UserControl, IRefreshableView
{
    public event EventHandler? AuthenticationChanged;

    public LoginView() => InitializeComponent();

    public Task RefreshAsync()
    {
        if (App.Services.Auth.HasSession)
        {
            CredentialsPanel.Visibility = Visibility.Collapsed;
            TwoFactorPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = "A Firefox Account session is stored on this Windows user. The session can be removed below.";
            if (SignOutButton is null)
            {
                var signOut = new Button { Name = "SignOutButton", Content = "Sign out", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
                signOut.Click += SignOutButton_Click;
                // The named visual is only added while signed in; the WPF tree is kept simple for the initial login state.
                CredentialsPanel.Parent.As<StackPanel>()?.Children.Add(signOut);
                SignOutButton = signOut;
            }
            SignOutButton.Visibility = Visibility.Visible;
        }
        else
        {
            CredentialsPanel.Visibility = Visibility.Visible;
            if (TwoFactorPanel.Visibility != Visibility.Visible) StatusText.Text = "Your password is used only for the Firefox Accounts authentication request and is not stored by this app.";
            if (SignOutButton is not null) SignOutButton.Visibility = Visibility.Collapsed;
        }
        return Task.CompletedTask;
    }

    private Button? SignOutButton { get; set; }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        StatusText.Text = "Contacting Firefox Accounts…";
        try
        {
            bool needsCode;
            try
            {
                needsCode = await App.Services.Auth.StartLoginAsync(EmailBox.Text, PasswordInput.Password);
            }
            catch (FirefoxChallengeRequiredException challenge)
            {
                var imported = InteractiveChallengeDialogFlow.ShowAndImport(
                    Window.GetWindow(this), challenge.ChallengeOrigin,
                    App.Services.Auth.ImportInteractiveChallengeCookies);
                if (!imported.UserCompleted)
                {
                    StatusText.Text = "Verification was canceled. No login request was retried.";
                    return;
                }
                if (imported.ImportedCookies == 0)
                {
                    StatusText.Text = "The browser did not return an eligible service cookie. Retry only if the service page confirms the challenge is complete.";
                    return;
                }
                StatusText.Text = "Verification state imported for the approved service origin. Retrying sign-in once…";
                needsCode = await App.Services.Auth.StartLoginAsync(EmailBox.Text, PasswordInput.Password);
            }

            if (needsCode)
            {
                PasswordInput.Clear();
                CredentialsPanel.Visibility = Visibility.Collapsed;
                TwoFactorPanel.Visibility = Visibility.Visible;
                StatusText.Text = "Verification is required. The session token is held in memory only until the code is submitted.";
            }
            else
            {
                PasswordInput.Clear();
                StatusText.Text = "Signed in successfully. Tokens are protected with Windows DPAPI.";
                AuthenticationChanged?.Invoke(this, EventArgs.Empty);
                await RefreshAsync();
            }
        }
        catch (FirefoxChallengeRequiredException ex)
        {
            StatusText.Text = ex.Message + " The service still requires verification after one user-mediated retry.";
            App.Services.Log.Warn("FirefoxAuth", "The Firefox Accounts challenge was not accepted after one user-mediated retry.");
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            App.Services.Log.Warn("FirefoxAuth", $"Sign-in failed: {ex.Message}");
        }
        finally { SignInButton.IsEnabled = true; }
    }

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        VerifyButton.IsEnabled = false;
        StatusText.Text = "Verifying code…";
        try
        {
            try
            {
                await App.Services.Auth.SubmitTwoFactorCodeAsync(CodeBox.Text);
            }
            catch (FirefoxChallengeRequiredException challenge)
            {
                var imported = InteractiveChallengeDialogFlow.ShowAndImport(
                    Window.GetWindow(this), challenge.ChallengeOrigin,
                    App.Services.Auth.ImportInteractiveChallengeCookies);
                if (!imported.UserCompleted || imported.ImportedCookies == 0)
                {
                    StatusText.Text = imported.UserCompleted
                        ? "No eligible service challenge cookie was returned; the verification code was not resubmitted."
                        : "Verification was canceled; the code was not resubmitted.";
                    return;
                }
                StatusText.Text = "Verification state imported. Retrying the code request once…";
                await App.Services.Auth.SubmitTwoFactorCodeAsync(CodeBox.Text);
            }
            CodeBox.Clear();
            PasswordInput.Clear();
            TwoFactorPanel.Visibility = Visibility.Collapsed;
            CredentialsPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = "Signed in successfully. Tokens are protected with Windows DPAPI.";
            AuthenticationChanged?.Invoke(this, EventArgs.Empty);
            await RefreshAsync();
        }
        catch (FirefoxChallengeRequiredException ex) { StatusText.Text = ex.Message + " The service still requires verification after one user-mediated retry."; }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            App.Services.Log.Warn("FirefoxAuth", $"Email verification failed: {ex.Message}");
        }
        finally { VerifyButton.IsEnabled = true; }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        TwoFactorPanel.Visibility = Visibility.Collapsed;
        CredentialsPanel.Visibility = Visibility.Visible;
        CodeBox.Clear();
        StatusText.Text = "Enter your email and password to start a new sign-in attempt.";
    }

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show("Remove the saved Firefox Account session from this Windows user?", "Sign out", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        App.Services.Auth.SignOut();
        StatusText.Text = "Signed out. Encrypted local session tokens have been removed.";
        AuthenticationChanged?.Invoke(this, EventArgs.Empty);
        _ = RefreshAsync();
    }
}

internal static class WpfVisualExtensions
{
    public static T? As<T>(this object? value) where T : class => value as T;
}
