using System.Windows;
using System.Windows.Controls;
using FoxyVPN.Windows.Models;
using FoxyVPN.Windows.Services;

namespace FoxyVPN.Windows.Views;

public partial class AccountView : UserControl, IRefreshableView
{
    private bool _loading;

    public AccountView() => InitializeComponent();

    public async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            string? token;
            try
            {
                token = await App.Services.Auth.GetFreshAccessTokenAsync();
            }
            catch (FirefoxChallengeRequiredException challenge)
            {
                var imported = InteractiveChallengeDialogFlow.ShowAndImport(
                    Window.GetWindow(this), challenge.ChallengeOrigin,
                    App.Services.Auth.ImportInteractiveChallengeCookies);
                if (!imported.UserCompleted || imported.ImportedCookies == 0)
                {
                    StatusText.Text = imported.UserCompleted
                        ? "No eligible Firefox Accounts challenge cookie was returned. Token refresh was not retried."
                        : "Firefox Accounts verification was canceled. Token refresh was not retried.";
                    return;
                }
                StatusText.Text = "Verification state imported. Retrying token refresh once…";
                token = await App.Services.Auth.GetFreshAccessTokenAsync(forceRefresh: true);
            }
            if (string.IsNullOrWhiteSpace(token))
            {
                StatusText.Text = "No active Firefox Account session. Sign in on the Firefox account page first.";
                EntitlementText.Text = "Not signed in";
                RemainingText.Text = "—";
                UsageBar.Value = 0;
                UsageText.Text = "Usage percentage is not available.";
                return;
            }
            StatusText.Text = "Contacting Firefox VPN Guardian…";
            Entitlement entitlement;
            try
            {
                entitlement = await App.Services.Guardian.FetchUserInfoAsync(token);
            }
            catch (GuardianChallengeRequiredException challenge)
            {
                var imported = InteractiveChallengeDialogFlow.ShowAndImport(
                    Window.GetWindow(this), challenge.ChallengeOrigin,
                    App.Services.Guardian.ImportInteractiveChallengeCookies);
                if (!imported.UserCompleted || imported.ImportedCookies == 0)
                {
                    StatusText.Text = imported.UserCompleted
                        ? "No eligible Guardian challenge cookie was returned. Account status was not retried."
                        : "Guardian verification was canceled. Account status was not retried.";
                    return;
                }
                StatusText.Text = "Verification state imported. Retrying Guardian once…";
                entitlement = await App.Services.Guardian.FetchUserInfoAsync(token);
            }
            EntitlementText.Text = entitlement.Subscribed ? "Active" : entitlement.LimitedBandwidth ? "Bandwidth limited" : "Not active";
            RemainingText.Text = entitlement.QuotaRemaining is long remaining ? FormatBytes(remaining) : "Not reported";
            if (entitlement.UsageRatio is double ratio)
            {
                UsageBar.Value = ratio * 100;
                UsageText.Text = $"{ratio:P0} of the allowance used";
            }
            else
            {
                UsageBar.Value = 0;
                UsageText.Text = entitlement.MaxBytes is long max ? $"Allowance limit: {FormatBytes(max)}; remaining usage not reported." : "Usage percentage is not available from the service response.";
            }
            StatusText.Text = "Account status was retrieved from Guardian.";
        }
        catch (GuardianChallengeRequiredException ex)
        {
            StatusText.Text = ex.Message + " The service still requires verification after one user-mediated retry.";
            App.Services.Log.Warn("Guardian", "Guardian challenge was not accepted after one user-mediated retry.");
        }
        catch (FirefoxChallengeRequiredException ex)
        {
            StatusText.Text = ex.Message + " Firefox Accounts still requires verification after one user-mediated retry.";
            App.Services.Log.Warn("FirefoxAuth", "Token refresh challenge was not accepted after one user-mediated retry.");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not retrieve account usage: {ex.Message}";
            App.Services.Log.Warn("Guardian", $"Account usage refresh failed: {ex.Message}");
        }
        finally { _loading = false; }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
}
