using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text.Json;

namespace FoxyVPN.Windows.Services;

/// <summary>
/// Owns every Windows routing/DNS/firewall mutation made by one FoxyVPN full-tunnel session.
/// Routes are ActiveStore-only and all owned changes are journaled under ProgramData. A named
/// cross-process semaphore serializes changes; a persistent physical-interface block remains until
/// route/metric restoration is verified. A root NRPT rule is created only when no existing root rule conflicts.
/// </summary>
public sealed class WindowsSystemRouteTransaction : IAsyncDisposable
{
    private static readonly SemaphoreSlim ProcessGate = new(1, 1);
    // Named OS semaphore serializes route journal and firewall mutations even when two elevated
    // FoxyVPN processes are launched concurrently. Semaphore (unlike Mutex) can safely be released
    // after asynchronous continuations resume on a different thread.
    private static readonly Lazy<Semaphore> CrossProcessGate = new(() => new Semaphore(1, 1, @"Global\FoxyVPN.SystemRouteTransaction"));
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private readonly string _journalPath;
    private readonly string _dnsComment;
    private readonly Action<string, string> _log;
    private readonly List<RouteMutation> _routes = new();
    private readonly List<InterfaceMetricMutation> _interfaceMetrics = new();
    private readonly List<string> _firewallRuleNames = new();
    private readonly Dictionary<string, string> _guardedAdapters = new(StringComparer.OrdinalIgnoreCase);
    private string? _uplinkInterfaceId;
    private string? _tunnelInterfaceId;
    private IPEndPoint? _upstreamRemote;
    private string? _sessionId;
    private int _nextFirewallRuleOrdinal;
    private bool _dnsRuleMayExist;
    private bool _completed;
    private int _disposed;

    private WindowsSystemRouteTransaction(string journalPath, string dnsComment, Action<string, string> logger)
    {
        _journalPath = journalPath;
        _dnsComment = dnsComment;
        _log = logger;
    }

    public bool IsActive => _completed && Volatile.Read(ref _disposed) == 0;
    public string RecoveryJournalPath => _journalPath;
    public static bool HasPendingRecoveryJournal
    {
        get
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FoxyVPN", "runtime", "active-system-routes.json");
                return File.Exists(path);
            }
            catch { return true; } // inability to inspect is treated as pending state, never as clean
        }
    }

    public static async Task<WindowsSystemRouteTransaction> StartAsync(
        int tunnelIpv4InterfaceIndex,
        int tunnelIpv6InterfaceIndex,
        string tunnelInterfaceId,
        IPEndPoint upstreamRemote,
        IPEndPoint upstreamLocal,
        Action<string, string>? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("System route management is available only on Windows.");
        if (!IsAdministrator()) throw new UnauthorizedAccessException("Full-system VPN requires running FoxyVPN with administrator privileges. No system routes were changed.");
        if (tunnelIpv4InterfaceIndex <= 0) throw new ArgumentOutOfRangeException(nameof(tunnelIpv4InterfaceIndex));
        if (tunnelIpv6InterfaceIndex <= 0) throw new ArgumentOutOfRangeException(nameof(tunnelIpv6InterfaceIndex));
        if (string.IsNullOrWhiteSpace(tunnelInterfaceId)) throw new ArgumentException("A stable tunnel interface identity is required for safe rollback.", nameof(tunnelInterfaceId));
        ArgumentNullException.ThrowIfNull(upstreamRemote);
        ArgumentNullException.ThrowIfNull(upstreamLocal);
        if (upstreamRemote.AddressFamily != upstreamLocal.AddressFamily)
            throw new InvalidOperationException("The upstream local and remote endpoint address families do not match; route bypass cannot be established safely.");

        var log = logger ?? ((_, _) => { });
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FoxyVPN", "runtime");
        Directory.CreateDirectory(root);
        var journalPath = Path.Combine(root, "active-system-routes.json");
        await ProcessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var crossProcessGateHeld = false;
        try
        {
            AcquireCrossProcessGate(cancellationToken);
            crossProcessGateHeld = true;
            await RecoverStaleJournalCoreAsync(journalPath, log, cancellationToken, allowCurrentProcessOwner: false).ConfigureAwait(false);
            var comment = "FoxyVPN:SystemTunnel:" + Guid.NewGuid().ToString("N");
            var transaction = new WindowsSystemRouteTransaction(journalPath, comment, log);
            try
            {
                // Persistent block rules are a safety prerequisite, not a best-effort enhancement.
                // Fail before touching routes when Windows Firewall profiles are disabled or unavailable.
                await EnsureWindowsFirewallEnabledAsync(cancellationToken).ConfigureAwait(false);
                var uplink = FindUplink(upstreamLocal.Address);
                var ipProperties = uplink.GetIPProperties();
                var gateway = ipProperties.GatewayAddresses
                    .Select(item => item.Address)
                    .FirstOrDefault(address => address.AddressFamily == upstreamRemote.AddressFamily && !IsUnspecified(address));
                if (gateway is null)
                    throw new InvalidOperationException($"No usable {upstreamRemote.AddressFamily} default gateway was found on the interface carrying the VPN upstream socket. Full-tunnel routing was not installed.");
                var uplinkIndex = GetInterfaceIndex(uplink, upstreamRemote.AddressFamily);
                if (uplinkIndex <= 0) throw new InvalidOperationException("Could not determine the physical uplink interface index.");

                // Preserve direct reachability to the physical gateway and the edge/proxy endpoint before
                // any broad tunnel routes are installed. Otherwise a local-subnet capture route could make
                // the next-hop gateway unreachable and strand the transport.
                var family = FamilyName(upstreamRemote.AddressFamily);
                var unspecifiedNextHop = upstreamRemote.AddressFamily == AddressFamily.InterNetwork ? "0.0.0.0" : "::";
                await transaction.AddRouteAsync(new RouteMutation(
                    family,
                    ToPrefix(gateway, upstreamRemote.AddressFamily == AddressFamily.InterNetwork ? 32 : 128),
                    uplinkIndex,
                    uplink.Id,
                    unspecifiedNextHop,
                    1), cancellationToken).ConfigureAwait(false);
                await transaction.AddRouteAsync(new RouteMutation(
                    family,
                    ToPrefix(upstreamRemote.Address, upstreamRemote.AddressFamily == AddressFamily.InterNetwork ? 32 : 128),
                    uplinkIndex,
                    uplink.Id,
                    RouteAddress(gateway),
                    1), cancellationToken).ConfigureAwait(false);

                // Install persistent physical-interface egress blocks before broad tunnel routes. Public
                // traffic can leave a physical interface only to the active HTTP/2 edge endpoint; private
                // LAN/link-local traffic remains available. Rules survive a process crash and are removed
                // only after routes are proven restored or the recovery journal is successfully replayed.
                await transaction.InstallKillSwitchAsync(uplink, tunnelInterfaceId, upstreamRemote, cancellationToken).ConfigureAwait(false);

                // Force a low, deterministic Wintun interface metric so duplicate connected prefixes
                // do not win on the physical adapter. Previous AutomaticMetric/InterfaceMetric values are
                // journaled first and restored on every successful disconnect/recovery.
                await transaction.SetTunnelInterfaceMetricAsync("ipv4", tunnelIpv4InterfaceIndex, tunnelInterfaceId, cancellationToken).ConfigureAwait(false);
                await transaction.SetTunnelInterfaceMetricAsync("ipv6", tunnelIpv6InterfaceIndex, tunnelInterfaceId, cancellationToken).ConfigureAwait(false);

                // Split defaults avoid replacing/deleting a user's default routes. Existing connected LAN
                // routes stay local by design; the persistent firewall guard blocks public destinations if
                // a more-specific user route or a vanished adapter would otherwise escape the tunnel.
                var desiredRoutes = BuildTunnelRoutes(tunnelIpv4InterfaceIndex, tunnelIpv6InterfaceIndex, tunnelInterfaceId);
                foreach (var route in desiredRoutes)
                    await transaction.AddRouteAsync(route, cancellationToken).ConfigureAwait(false);

                // Record intent before creating the rule. If the process stops between the command and
                // the next save, startup recovery still knows the unique comment to remove.
                transaction._dnsRuleMayExist = true;
                await transaction.SaveJournalAsync(cancellationToken).ConfigureAwait(false);
                await AddRootDnsRuleAsync(comment, cancellationToken).ConfigureAwait(false);
                if (!await RootDnsRuleExistsAsync(comment, cancellationToken).ConfigureAwait(false))
                    throw new IOException("Windows did not retain the FoxyVPN root NRPT DNS rule after creation.");
                transaction._completed = true;
                await transaction.SaveJournalAsync(cancellationToken).ConfigureAwait(false);
                log("INFO", $"Full-tunnel routes installed on IPv4/IPv6 interface indices {tunnelIpv4InterfaceIndex}/{tunnelIpv6InterfaceIndex}; the physical gateway and edge endpoint have explicit bypass routes. Persistent physical-interface kill-switch rules are active, and NRPT directs DNS to {WindowsHevConfigWriter.MapDnsAddress} through HEV mapdns.");
                return transaction;
            }
            catch (Exception startError)
            {
                try { await transaction.RollbackCoreAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception rollbackError)
                {
                    log("ERROR", $"Route rollback after startup failure was incomplete ({rollbackError.GetType().Name}). Recovery journal retained at {transaction._journalPath}; keep the VPN transport alive until cleanup succeeds.");
                    throw new WindowsRouteTransactionException(
                        "System route setup failed and automatic rollback was incomplete. The journal was retained; do not terminate the native tunnel until recovery is completed.",
                        rollbackError,
                        rollbackIncomplete: true,
                        startError);
                }
                throw;
            }
        }
        finally
        {
            if (crossProcessGateHeld) CrossProcessGate.Value.Release();
            ProcessGate.Release();
        }
    }

    /// <summary>
    /// Adds persistent public-egress blocks to newly connected or renamed physical adapters while the
    /// tunnel is active. New adapters receive no edge-endpoint exception, so they cannot bypass the
    /// currently pinned TCP transport. The existing rules remain until route rollback is verified.
    /// </summary>
    public async Task RefreshPhysicalEgressGuardAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("System firewall management is available only on Windows.");
        await ProcessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var crossProcessGateHeld = false;
        try
        {
            AcquireCrossProcessGate(cancellationToken);
            crossProcessGateHeld = true;
            if (!_completed || Volatile.Read(ref _disposed) != 0)
                throw new InvalidOperationException("The FoxyVPN route transaction is not active; refusing to change firewall coverage.");
            // The rules have no protective effect while a Firewall profile/service is disabled.
            // Detect that state before assuming coverage can be extended to a newly connected adapter.
            await EnsureWindowsFirewallEnabledAsync(cancellationToken).ConfigureAwait(false);
            await EnsurePhysicalEgressGuardCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (crossProcessGateHeld) CrossProcessGate.Value.Release();
            ProcessGate.Release();
        }
    }

    /// <summary>Removes only routes and DNS rules recorded by this app's journal.</summary>
    public static async Task RecoverStaleJournalAsync(Action<string, string>? logger = null, CancellationToken cancellationToken = default, bool allowCurrentProcessOwner = false)
    {
        if (!OperatingSystem.IsWindows() || !IsAdministrator()) return;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FoxyVPN", "runtime");
        var journalPath = Path.Combine(root, "active-system-routes.json");
        await ProcessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var crossProcessGateHeld = false;
        try
        {
            AcquireCrossProcessGate(cancellationToken);
            crossProcessGateHeld = true;
            await RecoverStaleJournalCoreAsync(journalPath, logger ?? ((_, _) => { }), cancellationToken, allowCurrentProcessOwner).ConfigureAwait(false);
        }
        finally
        {
            if (crossProcessGateHeld) CrossProcessGate.Value.Release();
            ProcessGate.Release();
        }
    }

    private static async Task RecoverStaleJournalCoreAsync(string journalPath, Action<string, string> logger, CancellationToken cancellationToken, bool allowCurrentProcessOwner)
    {
        if (!File.Exists(journalPath)) return;
        RouteJournal? journal;
        try { journal = JsonSerializer.Deserialize<RouteJournal>(await File.ReadAllTextAsync(journalPath, cancellationToken).ConfigureAwait(false)); }
        catch (Exception ex) { throw new InvalidOperationException($"The FoxyVPN system-route recovery journal cannot be read ({ex.GetType().Name}). Refusing to add more routes until it is repaired: {journalPath}"); }
        if (journal is null || journal.SchemaVersion is not (2 or 3) || string.IsNullOrWhiteSpace(journal.DnsComment) || journal.Routes is null)
            throw new InvalidOperationException($"The FoxyVPN system-route recovery journal is invalid. Refusing to alter routes: {journalPath}");
        if (journal.Routes.Any(route => string.IsNullOrWhiteSpace(route.InterfaceId)) ||
            (journal.InterfaceMetrics ?? Array.Empty<InterfaceMetricMutation>()).Any(metric => string.IsNullOrWhiteSpace(metric.InterfaceId)))
            throw new InvalidOperationException($"The recovery journal at {journalPath} predates stable network-interface ownership IDs. FoxyVPN will not delete routes whose ownership cannot be proven; review the journal before manual recovery.");
        if (!allowCurrentProcessOwner && IsOwnerProcessAlive(journal))
            throw new InvalidOperationException("A different live FoxyVPN process appears to own the active system routes. Recovery was refused to avoid disconnecting its running tunnel.");

        // DNS policy must be removed before the tunnel routes; persistent firewall guards remain in place
        // until every route and adapter metric has been restored. Never loosen the guard after partial cleanup.
        await RemoveRootDnsRuleAsync(journal.DnsComment, cancellationToken).ConfigureAwait(false);
        if (await RootDnsRuleExistsAsync(journal.DnsComment, cancellationToken).ConfigureAwait(false))
            throw new IOException($"The owned NRPT DNS rule {journal.DnsComment} remains after removal. Persistent kill-switch rules remain enabled and the journal is retained.");
        foreach (var route in journal.Routes.Reverse())
        {
            try { await DeleteRouteAsync(route, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { logger("WARN", $"Could not remove stale route {route.Prefix} from interface {route.InterfaceIndex} ({ex.GetType().Name})."); }
        }
        var stillPresent = new List<RouteMutation>();
        foreach (var route in journal.Routes)
        {
            if (await RouteExistsAsync(route, cancellationToken).ConfigureAwait(false)) stillPresent.Add(route);
        }
        if (stillPresent.Count > 0)
            throw new InvalidOperationException($"Could not recover {stillPresent.Count} stale FoxyVPN route(s). Persistent kill-switch rules remain enabled; the journal is retained at {journalPath}.");

        foreach (var metric in (journal.InterfaceMetrics ?? Array.Empty<InterfaceMetricMutation>()).Reverse())
        {
            try { await RestoreInterfaceMetricAsync(metric, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { logger("WARN", $"Could not restore the {metric.Family} metric on interface {metric.InterfaceIndex} ({ex.GetType().Name})."); }
        }
        var metricRestoreFailed = false;
        foreach (var metric in (journal.InterfaceMetrics ?? Array.Empty<InterfaceMetricMutation>()))
        {
            if (!await IsInterfaceMetricRestoredAsync(metric, cancellationToken).ConfigureAwait(false)) metricRestoreFailed = true;
        }
        if (metricRestoreFailed)
            throw new InvalidOperationException($"Could not restore one or more interface metrics from {journalPath}. Persistent kill-switch rules remain enabled.");

        foreach (var name in (journal.FirewallRuleNames ?? Array.Empty<string>()).Reverse())
        {
            try { await RemoveKillSwitchRuleAsync(name, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { logger("WARN", $"Could not remove persistent kill-switch rule {name} ({ex.GetType().Name}); public physical egress remains blocked."); }
        }
        var remainingFirewallRules = new List<string>();
        foreach (var name in journal.FirewallRuleNames ?? Array.Empty<string>())
        {
            if (await KillSwitchRuleExistsAsync(name, cancellationToken).ConfigureAwait(false)) remainingFirewallRules.Add(name);
        }
        if (remainingFirewallRules.Count > 0)
            throw new InvalidOperationException($"Could not remove {remainingFirewallRules.Count} FoxyVPN kill-switch firewall rule(s). The recovery journal remains at {journalPath}; public egress remains blocked.");

        File.Delete(journalPath);
        logger("INFO", "Recovered and removed the previous FoxyVPN system-route journal.");
    }

    private async Task AddRouteAsync(RouteMutation route, CancellationToken cancellationToken)
    {
        if (!IsExpectedInterfaceAtIndex(route.InterfaceId, route.Family, route.InterfaceIndex))
            throw new InvalidOperationException($"Interface {route.InterfaceIndex} no longer matches the expected network adapter identity. No route was added.");

        // Refuse to adopt a pre-existing route. Ownership must be unambiguous so rollback never removes
        // a route that belonged to Windows, another VPN, or the user.
        if (await RouteExistsAsync(route, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"A route already exists for {route.Prefix} on interface {route.InterfaceIndex}. Refusing to take ownership of a route that FoxyVPN did not create.");

        // Write intent before mutating the routing table. A crash after New-NetRoute succeeds but before
        // returning to managed code therefore still leaves enough information for recovery.
        _routes.Add(route);
        await SaveJournalAsync(cancellationToken).ConfigureAwait(false);
        await CreateRouteAsync(route, cancellationToken).ConfigureAwait(false);
        _log("DEBUG", $"Added {route.Family} route {route.Prefix} on interface {route.InterfaceIndex}.");
    }

    private async Task SetTunnelInterfaceMetricAsync(string family, int interfaceIndex, string interfaceId, CancellationToken cancellationToken)
    {
        var current = await ReadInterfaceMetricAsync(family, interfaceIndex, interfaceId, cancellationToken).ConfigureAwait(false);
        var mutation = new InterfaceMetricMutation(family, interfaceIndex, interfaceId, current.AutomaticMetric, current.InterfaceMetric);
        _interfaceMetrics.Add(mutation);
        await SaveJournalAsync(cancellationToken).ConfigureAwait(false);
        await SetInterfaceMetricAsync(family, interfaceIndex, automaticMetric: false, interfaceMetric: 1, cancellationToken).ConfigureAwait(false);
        _log("DEBUG", $"Pinned {family} tunnel interface {interfaceIndex} metric to 1; prior settings are journaled for restore.");
    }

    private async Task RollbackCoreAsync(CancellationToken cancellationToken)
    {
        if (_dnsRuleMayExist)
        {
            await RemoveRootDnsRuleAsync(_dnsComment, cancellationToken).ConfigureAwait(false);
            if (await RootDnsRuleExistsAsync(_dnsComment, cancellationToken).ConfigureAwait(false))
                throw new IOException("DNS NRPT rollback verification failed; persistent kill-switch rules remain enabled.");
        }
        foreach (var route in _routes.AsEnumerable().Reverse()) await DeleteRouteAsync(route, cancellationToken).ConfigureAwait(false);
        foreach (var route in _routes)
            if (await RouteExistsAsync(route, cancellationToken).ConfigureAwait(false))
                throw new IOException($"Route rollback verification failed for {route.Prefix} on interface {route.InterfaceIndex}; the kill switch remains enabled.");

        foreach (var metric in _interfaceMetrics.AsEnumerable().Reverse()) await RestoreInterfaceMetricAsync(metric, cancellationToken).ConfigureAwait(false);
        foreach (var metric in _interfaceMetrics)
            if (!await IsInterfaceMetricRestoredAsync(metric, cancellationToken).ConfigureAwait(false))
                throw new IOException($"Interface metric rollback verification failed for {metric.Family} interface {metric.InterfaceIndex}; the kill switch remains enabled.");

        // Remove the persistent egress block only after route removal and metric restoration are verified.
        foreach (var name in _firewallRuleNames.AsEnumerable().Reverse()) await RemoveKillSwitchRuleAsync(name, cancellationToken).ConfigureAwait(false);
        foreach (var name in _firewallRuleNames)
            if (await KillSwitchRuleExistsAsync(name, cancellationToken).ConfigureAwait(false))
                throw new IOException($"The persistent kill-switch rule {name} still exists. Keeping the recovery journal.");

        _routes.Clear();
        _interfaceMetrics.Clear();
        _firewallRuleNames.Clear();
        if (File.Exists(_journalPath)) File.Delete(_journalPath);
        _completed = false;
    }

    private async Task InstallKillSwitchAsync(NetworkInterface uplink, string tunnelInterfaceId, IPEndPoint upstreamRemote, CancellationToken cancellationToken)
    {
        _uplinkInterfaceId = uplink.Id;
        _tunnelInterfaceId = tunnelInterfaceId;
        _upstreamRemote = upstreamRemote;
        _sessionId = _dnsComment[("FoxyVPN:SystemTunnel:").Length..];
        await EnsurePhysicalEgressGuardCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsurePhysicalEgressGuardCoreAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_uplinkInterfaceId) || string.IsNullOrWhiteSpace(_tunnelInterfaceId) ||
            _upstreamRemote is null || string.IsNullOrWhiteSpace(_sessionId))
            throw new InvalidOperationException("The route transaction has no validated uplink/endpoint context for its kill switch.");

        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                          nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                          !SameInterfaceId(nic.Id, _tunnelInterfaceId))
            .ToArray();
        if (adapters.Length == 0)
            throw new InvalidOperationException("No active physical egress interface was found; refusing to enable full-system VPN without a persistent kill switch.");

        foreach (var adapter in adapters)
        {
            // Interface ID is stable across most adapter renames; because firewall rules bind to alias,
            // a renamed adapter needs fresh coverage even though the old journaled rule remains owned.
            if (_guardedAdapters.TryGetValue(adapter.Id, out var knownAlias) &&
                string.Equals(knownAlias, adapter.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var onControlUplink = SameInterfaceId(adapter.Id, _uplinkInterfaceId);
            foreach (var family in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 })
            {
                var allowedEndpoint = onControlUplink && _upstreamRemote.AddressFamily == family
                    ? _upstreamRemote.Address
                    : null;
                var blockedRanges = BuildBlockedRemoteRanges(family, allowedEndpoint);
                var suffix = family == AddressFamily.InterNetwork ? "V4" : "V6";
                await AddJournaledFirewallRuleAsync(
                    NextFirewallRuleName(suffix), adapter.Name, blockedRanges, "Any", null, cancellationToken).ConfigureAwait(false);

                if (allowedEndpoint is not null)
                {
                    // The existing control socket is a single TCP endpoint. No general host bypass is made.
                    var endpointPrefix = ToPrefix(allowedEndpoint, family == AddressFamily.InterNetwork ? 32 : 128);
                    var endpointSuffix = family == AddressFamily.InterNetwork ? "4" : "6";
                    await AddJournaledFirewallRuleAsync(
                        NextFirewallRuleName($"EP{endpointSuffix}-UDP"), adapter.Name, [endpointPrefix], "UDP", null, cancellationToken).ConfigureAwait(false);
                    var endpointPort = _upstreamRemote.Port;
                    if (endpointPort > 1)
                        await AddJournaledFirewallRuleAsync(
                            NextFirewallRuleName($"EP{endpointSuffix}-TCPLO"), adapter.Name, [endpointPrefix], "TCP", $"1-{endpointPort - 1}", cancellationToken).ConfigureAwait(false);
                    if (endpointPort < 65535)
                        await AddJournaledFirewallRuleAsync(
                            NextFirewallRuleName($"EP{endpointSuffix}-TCPHI"), adapter.Name, [endpointPrefix], "TCP", $"{endpointPort + 1}-65535", cancellationToken).ConfigureAwait(false);
                    var icmpProtocol = family == AddressFamily.InterNetwork ? "ICMPv4" : "ICMPv6";
                    await AddJournaledFirewallRuleAsync(
                        NextFirewallRuleName($"EP{endpointSuffix}-ICMP"), adapter.Name, [endpointPrefix], icmpProtocol, null, cancellationToken).ConfigureAwait(false);
                }
            }

            _guardedAdapters[adapter.Id] = adapter.Name;
            _log("INFO", $"Persistent egress kill-switch coverage installed for physical adapter {adapter.Name}; only the already-established TCP endpoint can leave the original uplink.");
        }
    }

    private string NextFirewallRuleName(string suffix)
    {
        var session = _sessionId ?? throw new InvalidOperationException("The firewall session ID was not initialized.");
        return $"FoxyVPN-KS-{session[..Math.Min(16, session.Length)]}-{_nextFirewallRuleOrdinal++:D4}-{suffix}";
    }

    private static IReadOnlyList<string> BuildBlockedRemoteRanges(AddressFamily family, IPAddress? allowedEndpoint) =>
        WindowsKillSwitchAddressRangeBuilder.Build(family, allowedEndpoint);

    private static async Task EnsureWindowsFirewallEnabledAsync(CancellationToken cancellationToken)
    {
        const string script = "$ErrorActionPreference='Stop'; $profiles=@(Get-NetFirewallProfile -ErrorAction Stop); if($profiles.Count -eq 0 -or @($profiles | Where-Object { -not $_.Enabled }).Count -gt 0){ [Console]::Error.WriteLine('Windows Defender Firewall must be enabled for the persistent FoxyVPN kill switch.'); exit 2 }; exit 0";
        var result = await RunProcessAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("FoxyVPN full-tunnel mode requires Windows Defender Firewall to be enabled for all profiles so public physical egress remains blocked during failures. No route changes were made. " + Bound(result.StandardError + " " + result.StandardOutput));
    }

    private async Task AddJournaledFirewallRuleAsync(string name, string interfaceAlias, IReadOnlyList<string> remoteRanges, string protocol, string? remotePorts, CancellationToken cancellationToken)
    {
        _firewallRuleNames.Add(name);
        await SaveJournalAsync(cancellationToken).ConfigureAwait(false); // journal intent before every persistent firewall mutation
        await AddKillSwitchRuleAsync(name, interfaceAlias, remoteRanges, protocol, remotePorts, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddKillSwitchRuleAsync(string name, string interfaceAlias, IReadOnlyList<string> blockedRanges, string protocol, string? remotePorts, CancellationToken cancellationToken)
    {
        var nameQ = EscapePs(name);
        var aliasQ = EscapePs(interfaceAlias);
        var protocolQ = EscapePs(protocol);
        var ranges = string.Join(",", blockedRanges.Select(range => "'" + EscapePs(range) + "'"));
        var portsArgument = remotePorts is null ? string.Empty : " -RemotePort @('" + EscapePs(remotePorts) + "')";
        var script = "$ErrorActionPreference='Stop'; $name='" + nameQ + "'; if(Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue){ throw 'Firewall rule name already exists; refusing to adopt it.' }; " +
                     "$blocked=@(" + ranges + "); New-NetFirewallRule -Name $name -DisplayName $name -Direction Outbound -Action Block -Enabled True -Profile Any -InterfaceAlias '" + aliasQ + "' -Protocol '" + protocolQ + "' -RemoteAddress $blocked" + portsArgument + " -ErrorAction Stop | Out-Null";
        await RunCheckedAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RemoveKillSwitchRuleAsync(string name, CancellationToken cancellationToken)
    {
        var safeName = EscapePs(name);
        var script = "$ErrorActionPreference='Stop'; $rules=@(Get-NetFirewallRule -Name '" + safeName + "' -ErrorAction SilentlyContinue); foreach($rule in $rules){ Remove-NetFirewallRule -InputObject $rule -ErrorAction Stop }; exit 0";
        await RunCheckedAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> KillSwitchRuleExistsAsync(string name, CancellationToken cancellationToken)
    {
        var safeName = EscapePs(name);
        var script = "$ErrorActionPreference='Stop'; $r=@(Get-NetFirewallRule -Name '" + safeName + "' -ErrorAction SilentlyContinue); if($r.Count -gt 0){exit 3}else{exit 0}";
        var result = await RunProcessAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        if (result.ExitCode == 3) return true;
        if (result.ExitCode != 0) throw new IOException("Could not inspect Windows Firewall while recovering kill-switch rules.");
        return false;
    }

    private async Task SaveJournalAsync(CancellationToken cancellationToken)
    {
        using var currentProcess = Process.GetCurrentProcess();
        var journal = new RouteJournal(3, _dnsComment, _dnsRuleMayExist, Environment.ProcessId,
            currentProcess.StartTime.ToUniversalTime().Ticks, _routes.ToArray(), _interfaceMetrics.ToArray(), _firewallRuleNames.ToArray());
        var temporary = _journalPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(journal), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _journalPath, overwrite: true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }

    private static async Task CreateRouteAsync(RouteMutation route, CancellationToken cancellationToken)
    {
        var family = route.Family == "ipv4" ? "IPv4" : "IPv6";
        var script = "$ErrorActionPreference='Stop'; $p='" + EscapePs(route.Prefix) + "'; $i=" + route.InterfaceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; " +
                     "$h='" + EscapePs(route.NextHop) + "'; $m=" + route.Metric.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; " +
                     "$existing=@(Get-NetRoute -PolicyStore ActiveStore -AddressFamily " + family + " -DestinationPrefix $p -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceIndex -eq $i }); " +
                     "if($existing.Count -gt 0){ throw 'Route identity already exists; refusing to adopt it.' }; " +
                     "New-NetRoute -PolicyStore ActiveStore -DestinationPrefix $p -InterfaceIndex $i -NextHop $h -RouteMetric $m -ErrorAction Stop | Out-Null";
        await RunCheckedAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteRouteAsync(RouteMutation route, CancellationToken cancellationToken)
    {
        if (!IsExpectedInterfaceAtIndex(route.InterfaceId, route.Family, route.InterfaceIndex))
        {
            if (await RouteExistsAsync(route, cancellationToken).ConfigureAwait(false))
                throw new IOException($"Route {route.Prefix} remains on interface index {route.InterfaceIndex}, but that index no longer belongs to {route.InterfaceId}. Refusing to delete a route with uncertain ownership.");
            return;
        }
        var family = route.Family == "ipv4" ? "IPv4" : "IPv6";
        var script = "$ErrorActionPreference='Stop'; $p='" + EscapePs(route.Prefix) + "'; $i=" + route.InterfaceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; " +
                     "$h='" + EscapePs(route.NextHop) + "'; $m=" + route.Metric.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; " +
                     "$all=@(Get-NetRoute -PolicyStore ActiveStore -AddressFamily " + family + " -DestinationPrefix $p -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceIndex -eq $i }); " +
                     "if($all.Count -eq 0){ exit 0 }; " +
                     "$exact=@($all | Where-Object { $_.NextHop -eq $h -and $_.RouteMetric -eq $m }); " +
                     "if($exact.Count -eq 0){ throw 'The route exists but its next hop/metric no longer matches the FoxyVPN journal; refusing to delete a route with uncertain ownership.' }; " +
                     "foreach($route in $exact){ $route | Remove-NetRoute -Confirm:$false -ErrorAction Stop }; " +
                     "$left=@(Get-NetRoute -PolicyStore ActiveStore -AddressFamily " + family + " -DestinationPrefix $p -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceIndex -eq $i }); " +
                     "if($left.Count -gt 0){ throw 'A matching route remains after removal.' }";
        await RunCheckedAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> RouteExistsAsync(RouteMutation route, CancellationToken cancellationToken)
    {
        var family = route.Family == "ipv4" ? "IPv4" : "IPv6";
        var script = "$ErrorActionPreference='Stop'; $p='" + EscapePs(route.Prefix) + "'; $i=" + route.InterfaceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; " +
                     "$r=@(Get-NetRoute -PolicyStore ActiveStore -AddressFamily " + family + " -DestinationPrefix $p -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceIndex -eq $i }); if($r.Count -gt 0){exit 3}else{exit 0}";
        var result = await RunProcessAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        if (result.ExitCode == 3) return true;
        if (result.ExitCode != 0) throw new IOException("Could not inspect Windows route table while recovering stale routes.");
        return false;
    }

    private static async Task<InterfaceMetricMutation> ReadInterfaceMetricAsync(string family, int interfaceIndex, string interfaceId, CancellationToken cancellationToken)
    {
        if (!IsExpectedInterfaceAtIndex(interfaceId, family, interfaceIndex))
            throw new IOException($"Interface {interfaceIndex} is not the expected adapter {interfaceId}.");
        var addressFamily = family == "ipv4" ? "IPv4" : "IPv6";
        var script = "$ErrorActionPreference='Stop'; $i=" + interfaceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; " +
                     "$n=Get-NetIPInterface -InterfaceIndex $i -AddressFamily " + addressFamily + " -ErrorAction Stop | Select-Object -First 1; " +
                     "if($null -eq $n){ throw 'Interface family is not available.' }; " +
                     "$n | Select-Object AutomaticMetric,InterfaceMetric | ConvertTo-Json -Compress";
        var result = await RunProcessAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken, allowNonZero: false).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new IOException($"Could not read the {family} metric for Wintun interface {interfaceIndex}: {Bound(result.StandardError)}");
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        var automatic = root.GetProperty("AutomaticMetric").GetBoolean();
        var metric = root.GetProperty("InterfaceMetric").GetInt32();
        return new InterfaceMetricMutation(family, interfaceIndex, interfaceId, automatic, metric);
    }

    private static async Task SetInterfaceMetricAsync(string family, int interfaceIndex, bool automaticMetric, int interfaceMetric, CancellationToken cancellationToken)
    {
        var addressFamily = family == "ipv4" ? "IPv4" : "IPv6";
        var auto = automaticMetric ? "Enabled" : "Disabled";
        var metric = automaticMetric ? string.Empty : " -InterfaceMetric " + interfaceMetric.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var script = "$ErrorActionPreference='Stop'; Set-NetIPInterface -InterfaceIndex " + interfaceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                     " -AddressFamily " + addressFamily + " -AutomaticMetric " + auto + metric + " -ErrorAction Stop | Out-Null";
        await RunCheckedAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RestoreInterfaceMetricAsync(InterfaceMetricMutation mutation, CancellationToken cancellationToken)
    {
        var nic = FindInterfaceById(mutation.InterfaceId);
        if (nic is null) return; // Adapter was removed; there is no surviving metric to restore.
        var index = GetInterfaceIndex(nic, mutation.Family == "ipv4" ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6);
        if (index <= 0) throw new IOException($"Could not find the current {mutation.Family} interface index for adapter {mutation.InterfaceId}.");
        await SetInterfaceMetricAsync(mutation.Family, index, mutation.AutomaticMetric, mutation.InterfaceMetric, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsInterfaceMetricRestoredAsync(InterfaceMetricMutation mutation, CancellationToken cancellationToken)
    {
        var nic = FindInterfaceById(mutation.InterfaceId);
        if (nic is null) return true;
        var index = GetInterfaceIndex(nic, mutation.Family == "ipv4" ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6);
        if (index <= 0) return false;
        var current = await ReadInterfaceMetricAsync(mutation.Family, index, mutation.InterfaceId, cancellationToken).ConfigureAwait(false);
        if (current.AutomaticMetric != mutation.AutomaticMetric) return false;
        return mutation.AutomaticMetric || current.InterfaceMetric == mutation.InterfaceMetric;
    }

    private static async Task AddRootDnsRuleAsync(string comment, CancellationToken cancellationToken)
    {
        var escapedComment = EscapePs(comment);
        var script = "$ErrorActionPreference='Stop'; try { " +
                     "$existing=@(Get-DnsClientNrptRule -ErrorAction SilentlyContinue | Where-Object { $_.Namespace -contains '.' }); " +
                     "if($existing.Count -gt 0){ throw 'An existing root DNS policy conflicts with FoxyVPN.' }; " +
                     "Add-DnsClientNrptRule -Namespace '.' -NameServers '" + WindowsHevConfigWriter.MapDnsAddress + "' -Comment '" + escapedComment + "' -PassThru -ErrorAction Stop | Out-Null; exit 0 " +
                     "} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
        await RunCheckedAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RemoveRootDnsRuleAsync(string comment, CancellationToken cancellationToken)
    {
        var escapedComment = EscapePs(comment);
        var script = "$ErrorActionPreference='Stop'; try { " +
                     "$rules=@(Get-DnsClientNrptRule -ErrorAction SilentlyContinue | Where-Object { $_.Comment -eq '" + escapedComment + "' }); " +
                     "foreach($rule in $rules){ Remove-DnsClientNrptRule -Name $rule.Name -Force -Confirm:$false -ErrorAction Stop }; exit 0 " +
                     "} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
        await RunCheckedAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> RootDnsRuleExistsAsync(string comment, CancellationToken cancellationToken)
    {
        var escapedComment = EscapePs(comment);
        var script = "$ErrorActionPreference='Stop'; $rules=@(Get-DnsClientNrptRule -ErrorAction Stop | Where-Object { $_.Comment -eq '" + escapedComment + "' }); if($rules.Count -gt 0){exit 3}else{exit 0}";
        var result = await RunProcessAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        if (result.ExitCode == 3) return true;
        if (result.ExitCode != 0) throw new IOException("Could not verify the FoxyVPN DNS NRPT rule while checking route rollback.");
        return false;
    }

    private static async Task RunCheckedAsync(string executable, IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(executable, args, cancellationToken, allowNonZero: false).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new IOException($"Windows network command failed ({Path.GetFileName(executable)}, exit {result.ExitCode}): {Bound(result.StandardError + " " + result.StandardOutput)}");
    }

    private static async Task<ProcessResult> RunProcessAsync(string executable, IEnumerable<string> args, CancellationToken cancellationToken, bool allowNonZero)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException($"Could not start {executable}.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        var result = new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        if (!allowNonZero && result.ExitCode != 0) return result;
        return result;
    }

    private static IReadOnlyList<RouteMutation> BuildTunnelRoutes(int tunnelIpv4InterfaceIndex, int tunnelIpv6InterfaceIndex, string tunnelInterfaceId)
    {
        var routes = new Dictionary<string, RouteMutation>(StringComparer.OrdinalIgnoreCase);
        void Add(string family, string prefix)
        {
            var interfaceIndex = family == "ipv4" ? tunnelIpv4InterfaceIndex : tunnelIpv6InterfaceIndex;
            routes.TryAdd(family + "|" + prefix,
                new RouteMutation(family, prefix, interfaceIndex, tunnelInterfaceId, family == "ipv4" ? "0.0.0.0" : "::", 1));
        }

        Add("ipv4", "0.0.0.0/1");
        Add("ipv4", "128.0.0.0/1");
        Add("ipv4", "10.0.0.0/8");
        Add("ipv4", "172.16.0.0/12");
        Add("ipv4", "192.168.0.0/16");
        Add("ipv4", "169.254.0.0/16");
        Add("ipv6", "::/1");
        Add("ipv6", "8000::/1");
        Add("ipv6", "fc00::/7");
        Add("ipv6", "fe80::/10");

        // Do not replace connected LAN routes: their more-specific entries remain local. The kill-switch
        // blocks public destinations on physical interfaces if a route unexpectedly bypasses the tunnel.
        return routes.Values.ToArray();
    }

    private static NetworkInterface FindUplink(IPAddress localAddress)
    {
        var normalized = Normalize(localAddress);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            var properties = SafeGetProperties(nic);
            if (properties is null) continue;
            if (properties.UnicastAddresses.Any(item => Normalize(item.Address).Equals(normalized)) &&
                properties.GatewayAddresses.Any(item => item.Address.AddressFamily == normalized.AddressFamily && !IsUnspecified(item.Address)))
                return nic;
        }
        throw new InvalidOperationException("Could not identify the physical network interface that carries the already-established VPN control socket. No full-tunnel routes were changed.");
    }

    private static int GetInterfaceIndex(NetworkInterface nic, AddressFamily family)
    {
        var properties = SafeGetProperties(nic);
        if (properties is null) return 0;
        return family == AddressFamily.InterNetwork
            ? properties.GetIPv4Properties()?.Index ?? 0
            : properties.GetIPv6Properties()?.Index ?? 0;
    }

    private static IPInterfaceProperties? SafeGetProperties(NetworkInterface nic)
    {
        try { return nic.GetIPProperties(); }
        catch (NetworkInformationException) { return null; }
        catch (PlatformNotSupportedException) { return null; }
    }

    private static bool IsExpectedInterfaceAtIndex(string interfaceId, string family, int expectedIndex)
    {
        var familyEnum = family == "ipv4" ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!SameInterfaceId(nic.Id, interfaceId)) continue;
            return GetInterfaceIndex(nic, familyEnum) == expectedIndex;
        }
        return false;
    }

    private static NetworkInterface? FindInterfaceById(string interfaceId) =>
        NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(nic => SameInterfaceId(nic.Id, interfaceId));

    private static bool SameInterfaceId(string left, string right)
    {
        var a = left.Trim().Trim('{', '}');
        var b = right.Trim().Trim('{', '}');
        return Guid.TryParse(a, out var ga) && Guid.TryParse(b, out var gb)
            ? ga == gb
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static IPAddress GetNetworkAddress(IPAddress address, int prefixLength)
    {
        var bytes = address.GetAddressBytes();
        for (var i = 0; i < bytes.Length; i++)
        {
            var bitsRemaining = prefixLength - i * 8;
            var mask = bitsRemaining >= 8 ? 0xFF : bitsRemaining <= 0 ? 0 : (0xFF << (8 - bitsRemaining)) & 0xFF;
            bytes[i] = (byte)(bytes[i] & mask);
        }
        return new IPAddress(bytes);
    }

    private static int CountPrefixBits(byte[] mask)
    {
        var count = 0;
        var foundZero = false;
        foreach (var value in mask)
            for (var bit = 7; bit >= 0; bit--)
            {
                var set = (value & (1 << bit)) != 0;
                if (set && foundZero) return -1;
                if (set) count++; else foundZero = true;
            }
        return count;
    }

    private static string ToPrefix(IPAddress address, int prefixLength) => $"{RouteAddress(address)}/{prefixLength}";
    private static string RouteAddress(IPAddress address) => new IPAddress(Normalize(address).GetAddressBytes()).ToString();
    private static string FamilyName(AddressFamily family) => family == AddressFamily.InterNetwork ? "ipv4" : "ipv6";
    private static bool IsUnspecified(IPAddress address) => address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    private static bool IsOwnerProcessAlive(RouteJournal journal)
    {
        if (journal.OwnerProcessId <= 0 || journal.OwnerProcessStartTimeUtcTicks <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(journal.OwnerProcessId);
            if (process.HasExited) return false;
            return process.StartTime.ToUniversalTime().Ticks == journal.OwnerProcessStartTimeUtcTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; } // fail closed when ownership cannot be inspected
    }

    private static void AcquireCrossProcessGate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool acquired;
        try { acquired = CrossProcessGate.Value.WaitOne(TimeSpan.FromSeconds(30)); }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Could not acquire the system-wide FoxyVPN route-transaction lock. Refusing to mutate Windows routes without cross-process serialization.", ex);
        }
        if (!acquired) throw new TimeoutException("Another FoxyVPN instance is changing system routes. Retry after it completes.");
        if (cancellationToken.IsCancellationRequested)
        {
            CrossProcessGate.Value.Release();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static bool IsAdministrator()
    {
        try { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }
    private static string EscapePs(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static string Bound(string value) => value.Length <= 500 ? value.Trim() : value[..500].Trim();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await ProcessGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        var crossProcessGateHeld = false;
        try
        {
            AcquireCrossProcessGate(CancellationToken.None);
            crossProcessGateHeld = true;
            await RollbackCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch { Volatile.Write(ref _disposed, 0); throw; }
        finally
        {
            if (crossProcessGateHeld) CrossProcessGate.Value.Release();
            ProcessGate.Release();
        }
    }

    private sealed record RouteMutation(string Family, string Prefix, int InterfaceIndex, string InterfaceId, string NextHop, int Metric);
    private sealed record InterfaceMetricMutation(string Family, int InterfaceIndex, string InterfaceId, bool AutomaticMetric, int InterfaceMetric);
    private sealed record RouteJournal(int SchemaVersion, string DnsComment, bool DnsRuleMayExist, int OwnerProcessId, long OwnerProcessStartTimeUtcTicks, IReadOnlyList<RouteMutation> Routes, IReadOnlyList<InterfaceMetricMutation>? InterfaceMetrics = null, IReadOnlyList<string>? FirewallRuleNames = null);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

public sealed class WindowsRouteTransactionException : Exception
{
    public bool RollbackIncomplete { get; }
    public Exception StartupFailure { get; }

    public WindowsRouteTransactionException(string message, Exception innerException, bool rollbackIncomplete, Exception startupFailure)
        : base(message, innerException)
    {
        RollbackIncomplete = rollbackIncomplete;
        StartupFailure = startupFailure;
    }
}
