# Continuation Prompt — FoxyVPN Windows Phase 1.5 → Phase 1.6

Act as a senior Windows networking engineer and continue from this exact repository, preserving the full Android source and all current WPF work. Do not recreate the project. Begin with `PHASE_1_5_VPN_TRANSPORT_REPORT.md`, `windows/MIGRATION_REPORT.md`, `windows/README_WINDOWS.md`, and the existing source/tests.

## Current source state

- Native desktop target: C# / .NET 8 / WPF under `windows/FoxyVPN.Windows/`; solution: `windows/FoxyVPN.sln`; tests: `windows/FoxyVPN.Tests/`.
- Original Android project is retained as the reference implementation.
- The 2027-inspired WPF design, ambient animations, persisted Reduce Motion option, auto-start, local-server favourites/probes, periodic server refresh, diagnostics export, authentication/account API, and prior phases must be preserved.
- Phase 1.5 adds `WindowsHttp2ConnectSession`, `Http2ConnectHeaderEncoder`, `GuardianProxyPassProvider`, `WindowsProxyTunnelSession`, and a loopback-only SOCKS5 TCP CONNECT path for explicit proxy-only mode.
- HTTP/2 uses TLS certificate validation, requires ALPN `h2`, classic CONNECT (`:method`, `:authority`), and a never-indexed `proxy-authorization: Bearer …` header. `hpack` 1.5.1 is referenced for response header decoding.
- Proxy-only mode is **not** system-wide VPN: other applications must be configured to use local SOCKS5 manually. It has no UDP/DNS relay and does not alter OS routes.
- A full-system TUN runtime is still absent; `WindowsTunnelEngine` must continue to fail closed instead of faking Connected.
- `dotnet`, MSBuild, and a C# compiler were unavailable in the previous authoring environment. No build/test/installer result is verified yet.

## Phase 1.6 — mandatory order of work

### A. Build and validate first

On a Windows 10/11 machine or Windows CI runner with .NET 8 and Visual Studio build tools:

```powershell
dotnet restore .\windows\FoxyVPN.sln
dotnet build .\windows\FoxyVPN.sln --configuration Release
dotnet test .\windows\FoxyVPN.sln --configuration Release
```

Fix all compiler/analyzer/test failures. Review `WindowsHttp2ConnectSession` as networking/security code rather than assuming it is RFC-conformant. Build a local mock TLS/HTTP2 peer for tests covering SETTINGS, HEADERS/CONTINUATION, classic CONNECT success/rejection, DATA and flow control, PING ACK/timeout, RST_STREAM, GOAWAY, malformed frames, cancellation, half-close, concurrency, and server disconnect. Verify behavior against the Android `H2UpstreamSession.kt` contract before testing any real compatible endpoint.

Do not claim tests passed unless their commands ran and results are recorded.

### B. Implement the real Windows packet data plane

1. Inspect the Android implementation end-to-end: `H2UpstreamSession.kt`, SOCKS5/UDP/DNS relay code, `FoxyVpnService`, HEV tun2socks integration, custom-edge handling, proxy chaining, split tunneling, reconnect and token renewal.
2. Select a maintained Windows TUN/packet backend and document licenses, driver distribution, architecture compatibility, signing, elevation requirements, and version pinning. Do not include an unknown or unsigned kernel driver by assumption.
3. Create an isolated `IWindowsTunRuntime` implementation that is only registered after adapter I/O and rollback exist. The runtime must read IPv4/IPv6 packets from the adapter, parse/forward flows safely, and write resulting packets back.
4. Bridge TCP through the existing HTTP/2 CONNECT stream provider. Provide an explicit TCP flow mapping/connection table, finite buffers, flow-control/backpressure, cancellation, idle timeouts, IPv6 handling, and correct TCP semantics. Do not assume a SOCKS proxy alone can consume raw IP packets.
5. Implement UDP and DNS independently (including the upstream's expected UDP mechanism and DoH policy); do not convert unsupported UDP silently into a direct local socket. Until these paths work, clearly label TCP-only proxy mode and keep full-system VPN unavailable.
6. Implement route and DNS transactions: capture previous routes/resolver state, create routes only after packet engine is ready, handle default route and IPv6 deliberately, rollback partial changes on every error, and restore state on disconnect/process failure. Add tests for rollback, cancellation, interface loss, network change, DNS failure, and disconnect leak prevention.
7. Port upstream proxy chaining only after its exact connection contract is understood; never silently bypass a configured proxy. Port token refresh/reauthentication, reconnection, server failover, split tunnel policies, and the 406 challenge behavior where necessary.
8. Add mock/integration tests and a documented test matrix. Test with an explicitly permitted test endpoint, verify public IPv4/IPv6 and DNS egress, confirm disconnect restores routes, and confirm there is no traffic leak on startup failure or tunnel loss.

### C. UX and release

Preserve the existing design and all Phase 1.1–1.5 features. Show **Proxy-only** versus **System VPN** as distinct, honest modes. Never show Connected until packet forwarding is confirmed. Display actionable errors for missing driver/elevation, Guardian 406, quota exhaustion, TLS/ALPN rejection, upstream CONNECT rejection, DNS failure, route rollback, and network loss.

Only after build, test, live integration, cleanup, privacy and rollback verification should you generate a portable build and MSIX/EXE installer. Provide:

- updated full-project ZIP (Android + Windows), Windows-only ZIP, migration report and a new continuation prompt;
- exact commands/results from Windows CI;
- feature checklist with verified vs. unverified statuses;
- driver/dependency/license inventory;
- known limitations with no claims beyond evidence.

**Hard rules:** Never replace functionality with mocks for release. Mocks are allowed only in tests. Never bypass TLS checks, log access/proxy tokens, route system traffic without a rollback path, or claim full VPN while TUN, UDP/DNS, route rollback, and leak tests are incomplete.
