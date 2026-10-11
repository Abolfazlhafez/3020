# Continue FoxyVPN Windows from Phase 1.7 → Phase 1.8

Continue directly from `FoxyVPN-Android-and-Windows-Phase1.7.zip` (or the current complete project ZIP supplied by the user). Preserve the original Android project and all existing Windows work. Do not recreate the application from scratch.

## User's workflow preference

The user explicitly prefers **complete coding and integration first, comprehensive tests at the end**. Do not interrupt every coding phase with a full test run. Use lightweight structural checks to avoid corrupting the source; once code is considered complete, run the whole build/test/runtime/security suite together and resolve every failure before creating a release artifact. Always state honestly which checks were and were not executed.

## Phase 1.7 code already added

- Native C#/.NET 8 WPF desktop shell with motion-led 2027 visual direction, animation toggles/Reduce Motion, Windows auto-start/start minimized, themes, Firefox Account UI/API, server catalog, favorites, TCP reachability, automatic catalog refresh and diagnostics export.
- `WindowsHttp2ConnectSession`: TLS with normal certificate validation, required ALPN h2, HTTP/2 CONNECT, HPACK header handling, flow control, PING/GOAWAY/RST handling, never-indexed Bearer authorization and optional upstream HTTP CONNECT/SOCKS5 proxy chaining. It does not silently fall back to a direct connection.
- `WindowsProxyTunnelSession` and localhost-only `WindowsSocks5ProxyServer`; DNS-only UDP ASSOCIATE uses a validated DoH request inside the authenticated CONNECT stream and DNS-over-TCP fallback through the same upstream. General UDP/QUIC is dropped because the current edge protocol offers TCP CONNECT only.
- `WindowsHevConfigWriter` and `WindowsHevNativeCore` for HEV 2.17.1 C API with Windows-owned Wintun, Android-aligned fake DNS and tunnel addresses.
- `WindowsSystemTunnelRuntime` is registered. The source startup order is HTTP/2 edge → SOCKS5 → HEV/Wintun → system route/DNS transaction; shutdown reverses it. The UI/runtime exposes a faulted state when a previously connected runtime stops confirming health.
- `WindowsSystemRouteTransaction`: named cross-process semaphore, stable network interface identity in a ProgramData recovery journal, physical gateway and upstream endpoint bypass routes, IPv4/IPv6 split routes, reversible interface metrics, unique root NRPT DNS rule, and persistent Windows Firewall rules blocking public physical egress until route/metric restoration is verified. The exact upstream TCP port is exempt, while UDP/ICMP/other TCP ports to that address are blocked. Startup fails before route mutation when Windows Defender Firewall profiles are disabled.
- `WindowsKillSwitchAddressRangeBuilder` plus xUnit tests for IPv4/IPv6 block ranges and runtime-loss-to-faulted state.

## Continue coding priorities (do not start final test phase yet)

1. **Port Android anti-bot challenge behavior** used by Firefox Accounts and Guardian HTTP 406 paths. Inspect the original Kotlin implementation and dependencies before choosing a native Windows strategy. Do not use a placeholder, weaken TLS, hardcode secrets or repeatedly retry a challenge without limits. If a challenge cannot be safely automated on Windows, expose the closest supported user flow and document it.
2. **Review split tunneling parity.** Android package exclusion has no direct Windows UI equivalent; investigate Windows Filtering Platform/AppID filtering and whether HEV/Wintun can safely exclude chosen applications. Implement only if packet routing can be made correct and testable. Otherwise document the limitation clearly rather than faking it.
3. Add lifecycle handling for Windows network availability/adapter changes, sleep/resume, and upstream loss. A changed uplink must never remove or loosen the kill switch before routes are safely restored. Define a serializable recovery state and explicit retry UX.
4. Review Guardian proxy-pass expiration during a live HTTP/2 session and the Android refresh behavior; implement safe reconnect/failover while retaining route/DNS rollback guarantees.
5. Review settings UX (proxy-only/full tunnel switch, upstream proxy credentials, startup mode, auto refresh, reduced motion) and the diagnostic report for stale messaging.
6. Keep compatibility explicit: the known edge protocol only provides HTTP/2 TCP CONNECT. DNS may use the current DNS-only route; general UDP and QUIC must remain dropped until upstream server support is confirmed.
7. Update `README.md`, `WINDOWS_SOURCE_README.md`, `windows/README_WINDOWS.md`, `windows/MIGRATION_REPORT.md`, CI artifact list, manifest, report and prompt as coding progresses.

## Final test phase — do this only after coding is declared complete

On a Windows 10/11 x64 runner with .NET 8 and MSYS2:

1. `dotnet restore windows/FoxyVPN.sln`
2. `dotnet build windows/FoxyVPN.sln --configuration Release --no-restore`
3. `dotnet test windows/FoxyVPN.sln --configuration Release --no-build --logger "trx;LogFileName=foxyvpn-tests.trx"`
4. Run `.github/workflows/windows-build.yml`; validate the native HEV x64 PE library, all dependent DLLs, both exported C symbols and the signed Wintun 0.14.1 runtime.
5. On a controlled Windows test machine, validate actual HEV start/stop, Guardian/edge H2 interoperability, proxy-only TCP, DNS mapping/DoH/DNS-over-TCP, upstream proxy chaining, IPv4 and IPv6 traffic, DNS leak checks, crash recovery, changed Wi-Fi/gateway, sleep/resume, firewall disabled/error cases, and every partial-startup rollback stage.
6. Verify no public egress bypasses the tunnel while connected, no token/credential appears in logs, route/DNS/firewall ownership is restored on disconnect, and recovery never deletes unrelated user routes.
7. Fix every error/failure, repeat the whole suite, then build the self-contained portable x64 output and Inno Setup installer and test installation, upgrade, uninstall, auto-start and rollback.

## Non-negotiable constraints

- Do not claim a build, test, real VPN connection, zero leak or installer is verified until a real result supports it.
- Do not remove or replace existing Android source or working app features.
- Do not flush routes or rewrite unrelated DNS settings. Every Windows mutation must be journaled before it happens and only owned changes may be removed.
- Keep Windows Defender Firewall's persistent block in place whenever route restoration cannot be proven. If the firewall is disabled externally after activation, report this limitation and test it explicitly.
- Keep TLS certificate validation on. Never log tokens/passwords. Never silently bypass upstream proxy configuration.
- Never claim general UDP/QUIC support when the server only accepts TCP CONNECT.
- Deliver the next full Android+Windows ZIP, Windows-only source ZIP, concise phase report, and next continuation prompt when this coding increment is complete.
