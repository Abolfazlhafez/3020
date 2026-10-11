# FoxyVPN Windows — Phase 1.8 Connectivity Lifecycle & Guardian Renewal Report

## Scope and workflow

This phase continues the user's explicit preference: finish coding/integration first and run comprehensive tests only after the coding backlog is complete. Lightweight static checks are allowed to catch obvious source-structure issues, but no `dotnet build`, `dotnet test`, native build, live service test or live Windows network mutation was run here.

## New source implemented

### Network, adapter and power lifecycle

`WindowsSystemTunnelRuntime` subscribes to `NetworkChange.NetworkAddressChanged`, `NetworkAvailabilityChanged`, and Windows `SystemEvents.PowerModeChanged`. Resume and network changes are debounced briefly before an inspection.

When the tunnel is active, the runtime asks `WindowsSystemRouteTransaction.RefreshPhysicalEgressGuardAsync` to extend the journaled public-egress block rules to newly connected or renamed active physical adapters. The exact control-endpoint exception is granted only on the original uplink; other adapters get no endpoint exception. Old rules remain journaled and are removed only after routes and interface metrics are restored.

The runtime also checks that the upstream socket's original local IP remains assigned to an active adapter, and checks current HTTP/2, HEV and route-transaction health. If a required layer or the pinned local endpoint becomes unhealthy, it changes the visible state to `Faulted`/safety hold and retains the route transaction, DNS journal, active forwarding components when possible, and kill-switch rules. It does not attempt a transparent reconnect over a potentially stale route/endpoint exception. The user must perform explicit disconnect/recovery before a new connection can start.

This is a best-effort user-mode event response. It does **not** prove zero leakage during adapter transitions, nor can Windows Firewall rules prevent a different process/user from disabling the Firewall service after activation. A real Wi-Fi-to-Ethernet, sleep/resume, adapter rename/removal and Firewall-disabled test remains required.

### Guardian proxy-pass renewal

`GuardianProxyPassProvider` now exposes a forced refresh path while retaining the existing short cache for ordinary CONNECT requests. The full-system runtime starts a background renewal loop when all tunnel layers are active. It schedules refresh around half of the remaining pass lifetime, with a 15-second minimum and 30-minute maximum; when expiry is unknown it uses a two-minute fallback. Failed refreshes are retried after 30 seconds, and tokens are never logged. Future HTTP/2 CONNECT streams use the latest provider token; currently open streams are not torn down solely to refresh a token.

The pure `ProxyPassRenewalSchedule` helper and xUnit source were added so the timing policy can be verified during the final test phase. A pending route journal at app startup triggers elevated recovery where possible; a non-elevated session displays a warning instead of silently ignoring the stale state.

### Startup recovery and fault handling retained

At application startup, a pending route journal triggers elevated automatic recovery where possible, or a visible warning instructing the user to relaunch elevated. Route changes remain journaled under ProgramData; the app refuses a new session on top of uncertain routes/safety hold. HTTP/2/HEV health loss no longer leaves the UI displaying a healthy connection.

## Known incomplete code parity

- The Android Fastly/Guardian anti-bot challenge solver is not ported. On HTTP 406 the Windows client reports the challenge condition rather than disabling certificate validation or inventing a success result. A supported user-mediated challenge flow remains open.
- Windows per-application split tunneling is not implemented. Windows Firewall application rules alone cannot reliably route selected processes through Wintun while preserving the right fail-closed behavior; a safe Windows Filtering Platform/AppID design still needs investigation and validation.
- The configured edge supports HTTP/2 TCP CONNECT. General UDP/QUIC remains unsupported. DNS-only handling does not imply general UDP capability.
- The source code and CI pipeline are not yet a verified release. Native HEV ABI loading, actual Wintun creation, HTTP/2 edge compatibility, PowerShell Firewall/NRPT syntax, route rollback and all leak behavior must be validated on a Windows 10/11 machine.

## Static structural checks performed after this coding increment

- XML/XAML/project/manifest parse: **11 valid, 0 invalid**.
- Visual Studio solution project references: **2 found, 0 missing**.
- XAML event-handler references: **28 checked, 0 unresolved**.
- C# lexical source check: **49 files scanned, 0 unterminated ordinary strings/comments or delimiter mismatches**.

These checks are not a C# compiler. The environment has no .NET SDK/MSBuild, so the managed/native build and all xUnit tests remain unexecuted by design, following the requested code-first/test-last workflow.

## Phase 1.9 coding priorities before the final test pass

1. Continue parity review for auth/API edge cases and create an approved user-mediated Fastly challenge experience without faking a challenge result or weakening TLS.
2. Investigate the correct native Windows per-app split-tunnel path; implement only if the route and kill-switch semantics can be made reliable, otherwise document it as unsupported.
3. Review event/lifecycle concurrency, proxy renewal retry semantics, adapter handover and Firewall service transitions before declaring coding complete.
4. Once the coding backlog is complete, run restore/build/test, CI's pinned native HEV/Wintun steps, live Guardian/edge interoperability and controlled Windows 10/11 IPv4, IPv6, DNS, proxy-chain, sleep/resume, adapter change, crash recovery, kill-switch, leak and installer/uninstall tests. Resolve failures before creating the release installer.
