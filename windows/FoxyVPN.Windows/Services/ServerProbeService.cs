using System.Diagnostics;
using System.Net.Sockets;
using FoxyVPN.Windows.Models;

namespace FoxyVPN.Windows.Services;

public sealed record ServerProbeResult(bool Reachable, long? ElapsedMilliseconds, string Message);

/// <summary>Checks TCP reachability only; it does not claim the VPN protocol or tunnel works.</summary>
public sealed class ServerProbeService
{
    public async Task<ServerProbeResult> ProbeTcpAsync(ServerOption server, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(4);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(limit);
        using var client = new TcpClient();
        var sw = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(server.Host, server.Port, linked.Token).ConfigureAwait(false);
            sw.Stop();
            return new ServerProbeResult(true, sw.ElapsedMilliseconds, $"TCP reachable in {sw.ElapsedMilliseconds} ms. This checks the port only, not VPN functionality.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ServerProbeResult(false, null, $"No TCP response within {limit.TotalSeconds:0.#} seconds.");
        }
        catch (SocketException ex)
        {
            return new ServerProbeResult(false, null, $"TCP check failed: {ex.SocketErrorCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ServerProbeResult(false, null, $"TCP check failed: {ex.GetType().Name}.");
        }
    }
}
