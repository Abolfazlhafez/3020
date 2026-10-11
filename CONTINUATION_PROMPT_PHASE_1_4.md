# Continuation prompt — FoxyVPN native Windows port (Phase 1.4 → Phase 1.5)

Continue directly from `FoxyVPN-Android-and-Windows-Phase1.4.zip`. Keep all existing Android/Kotlin source and prior Windows WPF work, including auto-start, favorites, diagnostic ZIP, animated 2027-inspired UI, reduced-motion support, Firefox login/API source, readiness report, and tunnel lifecycle. Do not recreate the project or delete functionality.

## Phase 1.4 changes present in this ZIP

- Windows source version is 1.4.0.
- Added `WindowsSocks5ProxyServer.cs`, with loopback-only SOCKS5 CONNECT parsing, limited concurrent sessions, handshake/upstream timeouts, cancellation, duplex forwarding, and TCP half-close support.
- Added injected contracts `IUpstreamStreamProvider` / `IUpstreamDuplexStream`. The listener never opens a destination socket directly, and refuses to start unless a live provider is injected.
- Filters loopback/private/link-local/unique-local/multicast and selected local hostnames. UDP ASSOCIATE is intentionally rejected until the UDP/DNS relay is implemented.
- Added test source in `windows/FoxyVPN.Tests/WindowsSocks5ProxyServerTests.cs` for loopback bind rejection, fail-closed upstream gating, successful fake-upstream CONNECT/relay/half-close, local target blocking, and UDP rejection.
- **The SOCKS5 listener is deliberately not wired into `AppServices` or started by the app.** The real HTTP/2 CONNECT stream provider does not exist. Windows TUN, OS routes, and live DNS relay are also missing. Do not display Connected or imply system traffic is tunneled.
- See `PHASE_1_4_DATA_PLANE_FOUNDATION_REPORT.md`, `PHASE_1_3_READINESS_REPORT.md`, and `windows/MIGRATION_REPORT.md` for current limits.

## First task: use Windows CI to establish real build/test results

On Windows 10/11 or the included Windows GitHub Actions runner, run:

```powershell
dotnet restore windows/FoxyVPN.sln
dotnet build windows/FoxyVPN.sln --configuration Release --no-restore
dotnet test windows/FoxyVPN.sln --configuration Release --no-build --logger "trx;LogFileName=foxyvpn-tests.trx"
```

Fix every compiler, XAML, package and test failure. The authoring sandbox used to produce this archive had no .NET SDK, MSBuild, `csc` or `mcs`; no build or tests have been claimed as passing. Do not skip the fake-upstream SOCKS integration test just because it uses loopback. Verify its timeout, half-close behavior, disposal and cancellation, and add tests for malformed/truncated requests and connection-limit enforcement.

## Primary engineering priority: real HTTP/2 CONNECT transport

Read and map the existing Android implementation before porting:
- `app/src/main/java/com/vauth/foxyvpn/vpn/upstream/H2UpstreamSession.kt`
- `app/src/main/java/com/vauth/foxyvpn/vpn/socks/LocalSocks5Server.kt`
- `app/src/main/java/com/vauth/foxyvpn/vpn/socks/Socks5UdpDnsRelay.kt`
- `app/src/main/java/com/vauth/foxyvpn/vpn/socks/DohDnsClient.kt`
- `app/src/main/java/com/vauth/foxyvpn/vpn/tun/HevSocks5Tunnel.kt`
- `app/src/main/java/com/vauth/foxyvpn/vpn/tun/HevSocks5TunnelConfig.kt`
- `app/src/main/java/com/vauth/foxyvpn/vpn/FoxyVpnService.kt`

The upstream contract is TLS + ALPN `h2` and per-destination HTTP/2 CONNECT streams. Each CONNECT carries `:authority=host:port` and `proxy-authorization: Bearer <Guardian proxy pass>`. Preserve certificate hostname validation even if a server is reached through a custom edge IP. Implement/test stream multiplexing, independent input/output, flow-control backpressure, cancellation, timeouts, GOAWAY/settings, error/status mapping, and upstream-proxy chaining. Renew Guardian proxy passes without corrupting existing streams. Do not assume generic `HttpClient` behavior gives the required full-duplex tunnel unless a real integration test proves it.

Then implement a concrete `IUpstreamStreamProvider` adapter for the existing SOCKS5 service. Only after provider tests pass may readiness settings say SOCKS is integrated; still do not turn on a VPN Connected state yet.

## Following work: DNS, TUN and safe route lifecycle

1. Port SOCKS5 UDP ASSOCIATE and DNS-over-HTTPS behavior. Preserve request validation and timeout behavior; add privacy tests.
2. Select a Windows TUN implementation only after validating supported OS builds, project activity, license, signing, driver installation, architecture and redistribution. Implement packet read/write and lifecycle cleanup.
3. Implement transactional routes and DNS with rollback on each partial failure; add crash/disconnect/reconnect and route-leak tests. Do not use shell-interpolated user input. Never leave stale routes after failure or exit.
4. Register an `IWindowsTunnelRuntime` only after the full path is real. Connected requires successful route/adapter setup plus a real health check. No mock success or direct-to-internet fallback is allowed.
5. Validate actual end-to-end traffic, external IP, DNS leakage, kill/disconnect behavior, Guardian pass renewal, failover and restoration. Document Windows split-tunnel design separately because Android package-based exclusions do not map directly to Windows.

## Delivery rules

- Keep WPF and native Windows controls. No emulator or WebView wrapper.
- Preserve Android source and prior features.
- Update migration report, tests, readiness checklist, and this continuation prompt each phase.
- Deliver both the full Android+Windows ZIP and Windows-only ZIP.
- Do not claim build/test/installer success unless Windows runner output/artifacts confirm it. Do not claim Windows VPN complete until all final acceptance criteria are exercised on a real Windows machine.
