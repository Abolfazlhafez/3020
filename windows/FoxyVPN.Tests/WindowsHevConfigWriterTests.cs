using System.IO;
using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class WindowsHevConfigWriterTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1080)]
    [InlineData(65535)]
    public void BuildsSecretFreeWindowsConfigMatchingAndroidTunDefaults(int port)
    {
        var yaml = WindowsHevConfigWriter.BuildYaml(port);

        Assert.Contains("name: 'FoxyVPN'", yaml, StringComparison.Ordinal);
        Assert.Contains("guid: 'c463fe0a-18a4-4ce7-9c95-2cda7c799e1f'", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ipv4: '10.8.0.2'", yaml, StringComparison.Ordinal);
        Assert.Contains("ipv6: 'fc00::1'", yaml, StringComparison.Ordinal);
        Assert.Contains("address: '127.0.0.1'", yaml, StringComparison.Ordinal);
        Assert.Contains($"port: {port}", yaml, StringComparison.Ordinal);
        Assert.Contains("udp: 'udp'", yaml, StringComparison.Ordinal);
        Assert.Contains("address: '198.18.0.2'", yaml, StringComparison.Ordinal);
        Assert.Contains("network: '100.64.0.0'", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("token", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", yaml, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void RejectsInvalidSocksPort(int port) => Assert.Throws<ArgumentOutOfRangeException>(() => WindowsHevConfigWriter.BuildYaml(port));

    [Fact]
    public void RejectsInvalidAdapterNameAndGuid()
    {
        Assert.Throws<ArgumentException>(() => WindowsHevConfigWriter.BuildYaml(1080, "bad: yaml"));
        Assert.Throws<ArgumentException>(() => WindowsHevConfigWriter.BuildYaml(1080, adapterGuid: "not-a-guid"));
    }

    [Fact]
    public async Task WritesConfigAtomicallyWithoutByteOrderMark()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FoxyVPN-HEV-Test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = await WindowsHevConfigWriter.WriteAsync(dir, 18080);
            var bytes = await File.ReadAllBytesAsync(file);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.Contains("port: 18080", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp-*"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
