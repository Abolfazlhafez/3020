namespace FoxyVPN.Windows.Models;

public sealed class AppSettings
{
    public string ThemeMode { get; set; } = "System";
    public bool ReduceMotion { get; set; }
    public bool ConnectOnLaunch { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool AutoRefreshServers { get; set; } = true;
    public int ServerRefreshIntervalMinutes { get; set; } = 15;
    public List<string> FavoriteServerKeys { get; set; } = new();
    public bool ConfirmDisconnect { get; set; }
    public bool ExitCheckEnabled { get; set; } = true;
    public bool ProxyOnlyMode { get; set; }
    public string SocksBindAddress { get; set; } = "127.0.0.1";
    public int SocksPort { get; set; } = 1080;
    public string DohProvider { get; set; } = "Automatic";
    public bool CustomDnsEnabled { get; set; }
    public string CustomDnsServer { get; set; } = "1.1.1.1";
    public string CustomEdgeAddress { get; set; } = "";
    public bool UpstreamProxyEnabled { get; set; }
    public string UpstreamProxyType { get; set; } = "SOCKS5";
    public string UpstreamProxyHost { get; set; } = "";
    public int UpstreamProxyPort { get; set; } = 1080;
    public string UpstreamProxyUsername { get; set; } = "";
    public string UpstreamProxyPassword { get; set; } = "";
    public string SelectedCountryCode { get; set; } = "REC";
    public string SelectedCityCode { get; set; } = "";
    public string SelectedLocationDisplay { get; set; } = "Recommended location";
    public string SelectedServerHost { get; set; } = "";
    public int SelectedServerPort { get; set; }
}
