# Continuation prompt — FoxyVPN native Windows port (Phase 1.3 → Phase 1.4)

Continue from `FoxyVPN-Android-and-Windows-Phase1.3.zip`. Preserve the existing Android Kotlin project and all Windows UI, settings, animations, startup, server catalog, authentication, Guardian, and diagnostics work. Do not rebuild from scratch.

## Current Phase 1.3 changes
- Windows project version 1.3.0.
- Added `TunnelReadinessService` and `WindowsTunRuntimeProbe`. The probe only looks for side-by-side `wintun.dll`; it does not load native code or alter system state.
- Added readiness UI output with explicit blockers.
- Replaced the static tunnel stub with a serializing `WindowsTunnelEngine` lifecycle and injectable `IWindowsTunnelRuntime` contract.
- `Connected` is allowed only if the registered runtime positively reports a connected tunnel. No runtime is currently registered, so connection requests must still fail closed.
- Added tests for readiness and tunnel lifecycle.
- See `PHASE_1_3_READINESS_REPORT.md` and `windows/MIGRATION_REPORT.md` for limits.

## Required workflow (do first)
1. On Windows 10/11 or Windows CI: run `dotnet restore windows/FoxyVPN.sln`, `dotnet build windows/FoxyVPN.sln --configuration Release --no-restore`, and `dotnet test windows/FoxyVPN.sln --configuration Release --no-build`.
2. Fix actual compiler, XAML, and test failures. Record command, exit code, and logs. Do not treat XML/static validation as a successful build.
3. Launch and inspect the app, auto-start, server refresh, diagnostics ZIP, Reduce Motion, 100/150/200% DPI, keyboard navigation, and resize/minimize/maximize.

## Main engineering task: implement the actual data plane
Inspect and map these Android sources before porting:
- `app/src/main/java/com/vauth/foxyvpn/vpn/FoxyVpnService.kt`
- `app/src/main/java/com/vauth/foxyvpn/vpn/upstream/H2UpstreamSession.kt`
- `app/src/main/java/com/vauth/foxyvpn/vpn/socks/LocalSocks5Server.kt`
- `app/src/main/java/com/vauth/foxyvpn/vpn/socks/Socks5UdpDnsRelay.kt`
- `app/src/main/java/com/vauth/foxyvpn/vpn/socks/DohDnsClient.kt`
- `app/src/main/java/com/vauth/foxyvpn/vpn/tun/HevSocks5TunnelConfig.kt`
- `app/build.gradle.kts` (pins `hev-socks5-tunnel` 2.17.1 from its upstream repository)

The upstream protocol is TLS with ALPN `h2`, persistent/multiplexed HTTP/2 streams, outbound `CONNECT` per destination, `:authority=host:port`, and `proxy-authorization: Bearer <Guardian proxy pass>`. Preserve certificate hostname validation even when using a custom edge IP. Port proper backpressure, remote GOAWAY/settings, cancellation, timeout handling, and upstream-proxy chaining. Do not assume `HttpClient` is sufficient for a full-duplex CONNECT stream without a verified integration test.

Select a Windows TUN implementation only after checking Windows 10/11 compatibility, current maintenance, license, redistribution, signature, architecture and install/update path. Implement packet reads/writes, IP routes, DNS, route rollback on failure/shutdown, kill/leak protection, cancellation and recoverability. Do not use shell-interpolated user input. Never leave routes behind after a failed start or process exit.

Port SOCKS5 CONNECT first and verify stream data bidirectionally; then port UDP ASSOCIATE and DNS behavior with parity tests. Implement connection pass renewal, failover, health checks, byte counters and external-IP verification. Split-tunnel exclusions need a documented Windows process-routing design.

## Release rules
- Keep WPF and native Windows controls; never make a WebView/emulator.
- Never display Connected before a verified tunnel and health check.
- Mark checklist items complete only after real verification.
- Update the migration report and this prompt every phase.
- Produce the complete source ZIP and Windows-only source ZIP after actual edits.
- Do not claim a portable EXE, installer, or passing build/test unless the Windows CI artifact/exit code confirms it.
