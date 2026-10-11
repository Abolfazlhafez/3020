using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using FoxyVPN.Windows.Services;
using FoxyVPN.Windows.Models;

namespace FoxyVPN.Windows.Views;

public partial class ServersView : UserControl, IRefreshableView
{
    private readonly ObservableCollection<ServerOption> _options = new();
    private ICollectionView? _view;
    private bool _loading;
    private readonly DispatcherTimer _refreshTimer;
    private readonly ServerProbeService _probeService = new();

    public ServersView()
    {
        InitializeComponent();
        ServerGrid.ItemsSource = _options;
        _view = CollectionViewSource.GetDefaultView(ServerGrid.ItemsSource);
        _view.Filter = FilterOption;
        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Tick += async (_, _) =>
        {
            ConfigureRefreshTimer();
            if (App.Services.Settings.Current.AutoRefreshServers && IsVisible)
                await RefreshAsync();
        };
        ConfigureRefreshTimer();
        UpdateFavoriteButton();
    }

    public async Task RefreshAsync()
    {
        ConfigureRefreshTimer();
        if (_loading) return;
        _loading = true;
        StatusText.Text = "Loading the live server catalog from Mozilla…";
        try
        {
            var options = await App.Services.Servers.FetchOptionsAsync();
            _options.Clear();
            foreach (var option in options) _options.Add(option);
            _view?.Refresh();
            StatusText.Text = $"Loaded {options.Count} live endpoints from Mozilla Remote Settings. TCP testing checks port reachability only; it does not test the VPN tunnel.";
            var current = App.Services.Settings.Current;
            ServerGrid.SelectedItem = _options.FirstOrDefault(x => x.Host.Equals(current.SelectedServerHost, StringComparison.OrdinalIgnoreCase) && x.Port == current.SelectedServerPort)
                ?? _options.FirstOrDefault(x => x.CountryCode.Equals(current.SelectedCountryCode, StringComparison.OrdinalIgnoreCase) && x.CityCode.Equals(current.SelectedCityCode, StringComparison.OrdinalIgnoreCase));
            UpdateFavoriteButton();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load locations: {ex.Message}";
            App.Services.Log.Warn("ServerCatalog", $"Location refresh failed: {ex.Message}");
        }
        finally { _loading = false; }
    }

    private bool FilterOption(object item)
    {
        if (item is not ServerOption option) return false;
        var search = SearchBox?.Text?.Trim();
        if (FavoritesOnlyCheck?.IsChecked == true && !App.Services.Settings.Current.FavoriteServerKeys.Contains(option.Key, StringComparer.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(search)) return true;
        return option.CountryName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               option.CityName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               option.Endpoint.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               option.CountryCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               option.CityCode.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void ConfigureRefreshTimer()
    {
        if (!App.Services.Settings.Current.AutoRefreshServers)
        {
            _refreshTimer.Stop();
            return;
        }
        _refreshTimer.Interval = TimeSpan.FromMinutes(App.Services.Settings.Current.ServerRefreshIntervalMinutes);
        if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void FavoritesOnlyCheck_Changed(object sender, RoutedEventArgs e) => _view?.Refresh();
    private void ServerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFavoriteButton();

    private void UpdateFavoriteButton()
    {
        if (FavoriteButton is null) return;
        if (ServerGrid.SelectedItem is not ServerOption selected)
        {
            FavoriteButton.Content = "☆ Add favorite";
            FavoriteButton.IsEnabled = false;
            ProbeButton.IsEnabled = false;
            return;
        }
        var isFavorite = App.Services.Settings.Current.FavoriteServerKeys.Contains(selected.Key, StringComparer.OrdinalIgnoreCase);
        FavoriteButton.Content = isFavorite ? "★ Remove favorite" : "☆ Add favorite";
        FavoriteButton.IsEnabled = true;
        ProbeButton.IsEnabled = true;
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ServerGrid.SelectedItem is not ServerOption selected) return;
        var key = selected.Key;
        var wasFavorite = App.Services.Settings.Current.FavoriteServerKeys.Contains(key, StringComparer.OrdinalIgnoreCase);
        App.Services.Settings.Update(s =>
        {
            if (wasFavorite) s.FavoriteServerKeys.RemoveAll(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
            else if (!s.FavoriteServerKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) s.FavoriteServerKeys.Add(key);
        });
        _view?.Refresh();
        UpdateFavoriteButton();
        StatusText.Text = wasFavorite ? $"Removed {selected.DisplayName} from favorites." : $"Added {selected.DisplayName} to favorites.";
        App.Services.Log.Info("Favorites", $"Favorite list updated for {selected.CountryCode}/{selected.CityCode}.");
    }

    private async void ProbeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ServerGrid.SelectedItem is not ServerOption selected) return;
        ProbeButton.IsEnabled = false;
        FavoriteButton.IsEnabled = false;
        StatusText.Text = $"Checking TCP reachability for {selected.Endpoint}…";
        try
        {
            var result = await _probeService.ProbeTcpAsync(selected);
            StatusText.Text = $"{selected.DisplayName} ({selected.Endpoint}): {result.Message}";
            if (result.Reachable)
                App.Services.Log.Info("ServerProbe", $"TCP reachable for {selected.CountryCode}/{selected.CityCode} in {result.ElapsedMilliseconds} ms.");
            else
                App.Services.Log.Warn("ServerProbe", $"TCP check failed for {selected.CountryCode}/{selected.CityCode}: {result.Message}");
        }
        finally
        {
            UpdateFavoriteButton();
        }
    }

    private void UseSelectedButton_Click(object sender, RoutedEventArgs e) => SaveSelection();
    private void ServerGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => SaveSelection();

    private void SaveSelection()
    {
        if (ServerGrid.SelectedItem is not ServerOption option)
        {
            StatusText.Text = "Select a location first.";
            return;
        }
        App.Services.Settings.Update(s =>
        {
            s.SelectedCountryCode = option.CountryCode;
            s.SelectedCityCode = option.CityCode;
            s.SelectedLocationDisplay = option.DisplayName;
            s.SelectedServerHost = option.Host;
            s.SelectedServerPort = option.Port;
        });
        StatusText.Text = $"Selected {option.DisplayName} ({option.Endpoint}). It is saved locally; the VPN data plane is not active in this build.";
        App.Services.Log.Info("Settings", $"Selected VPN endpoint {option.Endpoint} in {option.CountryCode}/{option.CityCode}.");
    }
}
