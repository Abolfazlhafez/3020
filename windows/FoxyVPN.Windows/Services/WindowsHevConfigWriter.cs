using System.IO;
using System.Text;

namespace FoxyVPN.Windows.Services;

/// <summary>
/// Writes a secret-free HEV tun2socks configuration matching the Android tunnel defaults.
/// The native HEV runtime owns creation and packet processing for its Wintun adapter.
/// </summary>
public static class WindowsHevConfigWriter
{
    public const string AdapterName = "FoxyVPN";
    public const string AdapterGuid = "C463FE0A-18A4-4CE7-9C95-2CDA7C799E1F";
    public const string TunnelIpv4 = "10.8.0.2";
    public const string TunnelIpv6 = "fc00::1";
    public const string MapDnsAddress = "198.18.0.2";
    public const string MapDnsNetwork = "100.64.0.0";
    public const string MapDnsNetmask = "255.192.0.0";
    public const int Mtu = 8500;

    public static string BuildYaml(int socksPort, string adapterName = AdapterName, string adapterGuid = AdapterGuid)
    {
        if (socksPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(socksPort), "The loopback SOCKS5 port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(adapterName) || adapterName.Length > 64 ||
            adapterName.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_' or '.')))
            throw new ArgumentException("Adapter name must be simple ASCII letters, digits, spaces, dots, underscores or hyphens.", nameof(adapterName));
        if (!Guid.TryParse(adapterGuid, out var parsedGuid))
            throw new ArgumentException("A valid Wintun interface GUID is required.", nameof(adapterGuid));

        var safeName = adapterName.Replace("'", "''", StringComparison.Ordinal);
        var safeGuid = parsedGuid.ToString("D");
        return $$"""
tunnel:
  name: '{{safeName}}'
  guid: '{{safeGuid}}'
  mtu: {{Mtu}}
  ipv4: '{{TunnelIpv4}}'
  ipv6: '{{TunnelIpv6}}'
  icmp: 'reply'
socks5:
  address: '127.0.0.1'
  port: {{socksPort}}
  udp: 'udp'
mapdns:
  address: '{{MapDnsAddress}}'
  port: 53
  network: '{{MapDnsNetwork}}'
  netmask: '{{MapDnsNetmask}}'
  cache-size: 10000
misc:
  task-stack-size: 86016
  connect-timeout: 20000
  tcp-read-write-timeout: 300000
  udp-read-write-timeout: 60000
  tcp-buffer-size: 65536
  udp-recv-buffer-size: 524288
  udp-copy-buffer-nums: 16
  max-session-count: 0
  log-level: 'warn'
""";
    }

    public static async Task<string> WriteAsync(string directory, int socksPort, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A config directory is required.", nameof(directory));
        var yaml = BuildYaml(socksPort);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(Path.GetFullPath(directory), "hev-socks5-tunnel.yaml");
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, yaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { /* cleanup best effort */ }
        }
    }
}
