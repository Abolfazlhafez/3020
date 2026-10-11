# FoxyVPN for Windows (native WPF)

This is the native Windows desktop port under active development. It is a separate WPF application; the original Android project remains unchanged.

## Current scope

The current Windows source includes a native desktop shell and implementations for the Firefox Accounts password-derived authentication request, email verification-code step, OAuth token refresh, DPAPI-protected session storage, Mozilla Remote Settings server-catalog parsing/fetching, Guardian status/quota/proxy-pass API clients, DPAPI-protected settings, logs, and native WPF screens for Home, Locations, Firefox account, Account usage, Settings, and Logs.

**Latest status — Phase 2.5:** source now includes the full intended orchestration sequence (authenticated HTTP/2 CONNECT → localhost SOCKS5 → HEV/Wintun → journalled route/DNS transaction and persistent Firewall guard), network/suspend lifecycle monitoring, Guardian proxy-pass renewal, connect-on-launch recovery gating, and a user-mediated WebView2 challenge window for Firefox Accounts/Guardian HTTP 406 responses. Challenge cookies are filtered by secure-transport, expiry, host/domain allow-list, path/name/value limits and imported only into the respective in-memory HTTP client cookie jar. Operations retry no more than once after user verification. The WebView2 user-data folder is separated by approved origin. Phase 2.0 added deterministic test seams for upstream HTTP CONNECT and SOCKS5 negotiation. Phase 2.1 fixes HTTP/2 TCP half-close behavior, propagates runtime safety-hold state, gates disconnected-state reporting on verified cleanup after cancellation, and makes repeated async disposal share one cleanup task. Phase 2.3 serializes stream initiation, bounds frame writes, tightens GOAWAY/closed-stream validation, and applies response header budgets and syntax checks. Phase 2.4 correctly waits through legal informational HTTP/2 response headers before evaluating the final CONNECT response, and aligns CI installer version metadata. Phase 2.5 validates returned DNS transaction IDs, opcode and semantic question tuples (name/type/class), rejecting mismatched DoH/TCP answers without direct-network fallback. This remains **source-authored and not build/live-verified**: .NET/MSBuild and the native HEV build have not run in this authoring environment, and no Windows 10/11 live tunnel, DNS-leak or rollback test is claimed. General UDP/QUIC remains unsupported by the TCP-CONNECT-only edge. Per-app split tunneling is explicitly unavailable; no placeholder toggle is exposed. Comprehensive tests should be run as the final stage, after coding is complete.

## Build on Windows 10/11

Install Visual Studio 2022 with the **.NET desktop development** workload, the .NET 8 SDK, and the Microsoft Edge WebView2 Evergreen Runtime (needed only when a service requires interactive verification), then open `FoxyVPN.sln` or run these commands in PowerShell from the repository root:

```powershell
dotnet restore .\windows\FoxyVPN.sln
dotnet build .\windows\FoxyVPN.sln --configuration Release
dotnet test .\windows\FoxyVPN.sln --configuration Release
```

Create a compact, framework-dependent x64 publish folder. The app needs the .NET 8 Desktop Runtime; the distributed installer downloads and installs it from Microsoft only when it is missing:

```powershell
dotnet publish .\windows\FoxyVPN.Windows\FoxyVPN.Windows.csproj `
  --configuration Release --runtime win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  --output .\artifacts\portable
```

To create an installer, install Inno Setup 6 and run `windows\installer\FoxyVPN.iss` with `SourceDir` pointing at `artifacts\portable`. The compact release ZIP contains one installer, not duplicate self-contained installers. The installer checks for the .NET 8 Desktop Runtime and downloads it from Microsoft's official endpoint only if it is missing; this keeps the initial FoxyVPN download small while still allowing clean installs. The repository's GitHub Actions workflow (`.github/workflows/windows-build.yml`) builds, tests, publishes the framework-dependent app, and packages the installer on a Windows runner.

## Storage and privacy

Session tokens and settings (including any saved upstream-proxy credentials) are encrypted with Windows DPAPI for the current Windows user in `%LOCALAPPDATA%\FoxyVPN`. Diagnostic logs are stored under `%LOCALAPPDATA%\FoxyVPN\logs`. Secrets, passwords, and bearer tokens are deliberately not logged. The password entered in the sign-in view is not persisted.


## Added desktop convenience features (Phase 1.1)

- **Windows sign-in auto-start:** the Settings toggle writes/removes the current-user `Run` registry entry; it does not require administrator rights. The setting can optionally launch the app minimized to the taskbar.
- **Start minimized on sign-in:** uses a private `--minimized` command-line flag on the startup entry. Manual launches still open normally.
- **Favorite locations:** add/remove a live server endpoint as a favorite and filter the list to favorites. Favorites are saved in the existing DPAPI-protected settings file.
- **TCP reachability check:** checks whether the selected endpoint accepts a TCP connection within four seconds. This is a port reachability diagnostic only; it is not an end-to-end VPN test and does not establish a VPN tunnel.
- **Automatic catalog refresh:** the server page refreshes its Mozilla Remote Settings catalogue every 5, 15, or 30 minutes when enabled and when that page is visible.
- **Diagnostic ZIP export:** the Logs page can export a ZIP with a diagnostic summary and current/rotated app logs. The summary omits credentials, tokens, account IDs, and selected endpoint hostnames; log files may contain hostnames/error details, so users are asked to review them before sharing.

### Current limitations before Phase 1.5

The earlier phases did not implement the full data plane. Phase 1.5 adds an HTTP/2 CONNECT transport source and a distinct proxy-only mode, but global traffic routing, TUN packet I/O, UDP/DNS runtime integration, upstream proxy chaining, route rollback and a system-wide connected state remain unavailable. Build/run/test validation still requires a Windows environment with the .NET 8 SDK because this authoring environment does not have `dotnet`, MSBuild, or the Windows desktop runtime installed.

## Phase 1.2 — Motion-led desktop design

The desktop shell has been refreshed with a motion-led 2027 visual direction while retaining a native WPF UI (no embedded browser):

- Animated page entry using a short fade and eased vertical slide; the transition is skipped when Reduce motion is enabled.
- Native button hover/press micro-interactions and animated hover elevation on content cards.
- Home dashboard with layered orbit rings, a rotating vector orbit marker, and a softly breathing ambient glow. These loops are started only while Home is loaded, then stopped when the view is unloaded.
- Light/dark/system palette support with updated accent tokens; the hero panel uses a controlled deep-space gradient for contrast.
- Settings > General now includes **Reduce motion**, saved in the DPAPI-protected settings. It disables the ambient home loops and page-slide transitions; button hover feedback remains intentionally subtle.

The animation is purely presentational and does not indicate a VPN connection. The home panel continues to state that the Windows tunnel/data plane is unavailable. This phase is source-authored and statically checked only: visual behavior and smoothness still require a real Windows 10/11 run, including a reduced-motion accessibility pass.



## Phase 1.3 — tunnel readiness and lifecycle foundation

The overview action now runs a readiness preflight and lists blockers for supported Windows, account session, selected endpoint, Wintun runtime file, HTTP/2 CONNECT, SOCKS5 relay, and route/DNS lifecycle. The Wintun check only looks for `wintun.dll` beside the app; it does not load code, create an adapter, install drivers, or edit routes.

`WindowsTunnelEngine` is now serialized and cancellation-aware, with `Disconnected`, `Starting`, `Connected`, `Stopping`, and `Faulted` states behind `IWindowsTunnelRuntime`. It only reports connected after the registered runtime positively reports an active tunnel. No runtime is currently registered, so connection requests still fail closed.

Version metadata is `1.3.0`. Automated tests were authored for readiness reporting and tunnel state transitions, but they have not been run in this authoring environment because `dotnet` / MSBuild are unavailable. See [`../PHASE_1_3_READINESS_REPORT.md`](../PHASE_1_3_READINESS_REPORT.md) and [`../CONTINUATION_PROMPT_PHASE_1_3.md`](../CONTINUATION_PROMPT_PHASE_1_3.md).


## Phase 1.4 — SOCKS5 CONNECT foundation

Version metadata is `1.4.0`. `WindowsSocks5ProxyServer` adds a loopback-only RFC 1928 SOCKS5 CONNECT listener. It does not create direct destination sockets: each requested destination must be opened by an injected `IUpstreamStreamProvider`. It refuses to start when that provider is not connected, refuses LAN binding, caps concurrent clients, applies handshake/upstream timeouts, rejects loopback/private/link-local destinations, supports duplex relay and TCP half-close, and deliberately rejects UDP ASSOCIATE until the UDP/DNS relay is ported. The listener is not registered or started by the app because the actual HTTP/2 CONNECT provider is not implemented yet.

Protocol/relay test source covers loopback binding rejection, start refusal without an upstream, a TCP CONNECT echo round-trip with client half-close, loopback-destination blocking, DNS-only UDP ASSOCIATE over a fake upstream, and dropping non-DNS UDP without opening an upstream. These tests have **not been run** here because the .NET SDK/MSBuild is unavailable. The native Windows VPN data plane remains incomplete; see [`../PHASE_1_4_DATA_PLANE_FOUNDATION_REPORT.md`](../PHASE_1_4_DATA_PLANE_FOUNDATION_REPORT.md) and [`../CONTINUATION_PROMPT_PHASE_1_4.md`](../CONTINUATION_PROMPT_PHASE_1_4.md).


## Phase 1.5 — HTTP/2 CONNECT transport and proxy-only mode

Version metadata is `1.6.0`. The Windows source now contains:

- `WindowsHttp2ConnectSession`: direct TCP + validated TLS 1.2/1.3, required ALPN `h2`, initial peer SETTINGS gate, classic HTTP/2 CONNECT streams, HTTP/2 frame parsing, HPACK response decoding, stream/connection flow control, SETTINGS/WINDOW_UPDATE/RST_STREAM/GOAWAY/PING handling, a 30-second keep-alive PING, cancellation, per-stream open deadlines, and duplex stream wrappers for SOCKS5.
- `Http2ConnectHeaderEncoder`: emits `:method = CONNECT`, `:authority`, and `proxy-authorization: Bearer …`; classic CONNECT intentionally omits `:scheme` and `:path`. The Bearer header is emitted as HPACK **never-indexed** and outbound request headers do not depend on an encoder dynamic table.
- `GuardianProxyPassProvider`: obtains/refreshed Guardian proxy passes using the Firefox Accounts access token, caches only until token expiry (or a short fallback lifetime), and never logs the token.
- `WindowsProxyTunnelSession`: connects the selected endpoint (or custom edge address) while retaining the selected hostname for TLS validation/SNI, then wires the HTTP/2 provider into the loopback-only SOCKS5 TCP relay. Proxy-only mode is now exposed from Home when explicitly enabled in Settings.
- `hpack` 1.5.1 is added for HPACK response decoding; third-party license is documented in `THIRD_PARTY_NOTICES.md`.

The code intentionally refuses to silently ignore configured upstream proxy chaining. It also refuses LAN SOCKS binding, and the UI no longer offers `0.0.0.0` because local clients are unauthenticated. At Phase 1.6, SOCKS UDP ASSOCIATE is accepted only for loopback-associated clients; the relay processes DNS datagrams on port 53 and drops other UDP/QUIC.

**Not yet implemented:** Windows Wintun adapter creation and packet I/O, system routing and rollback, DNS/DoH packet relay, SOCKS UDP relay, split tunneling, upstream proxy chaining, token renewal/recovery inside an already-open transport, auto-reconnect/failover, and system-wide leak protection. The full-tunnel `WindowsTunnelEngine` remains unregistered and fails closed.

The Phase 1.5 transport code has **not been compiled or tested against the real service** because `dotnet`/MSBuild and network access to restore packages are unavailable here. The new HPACK header test and prior SOCKS/tunnel tests are test *source*, not verified test results. See [`../PHASE_1_5_VPN_TRANSPORT_REPORT.md`](../PHASE_1_5_VPN_TRANSPORT_REPORT.md) and [`../CONTINUATION_PROMPT_PHASE_1_5.md`](../CONTINUATION_PROMPT_PHASE_1_5.md).


## Phase 1.6 — Native HEV tunnel core

The Windows source now contains a C-API wrapper for HEV `2.17.1`, a secret-free YAML writer based on the Android reference configuration, and a CI step that builds the native library from source. CI downloads the official Wintun `0.14.1` x64 runtime and verifies its SHA-256 before bundling `wintun.dll` beside the app.

`WindowsHevNativeCore` is deliberately a **core lifecycle helper**, not the full system VPN runtime. It can load the native library, start HEV with a Windows-owned Wintun adapter (`tun_fd = -1`), wait for the configured interface, and request clean native shutdown without unloading code while the worker is alive. It does not install default routes and is not registered as the app's `IWindowsTunnelRuntime`.

Phase 1.6 now includes source for SOCKS5 UDP ASSOCIATE with a DNS-only relay: it attempts DoH to known resolver IPs over authenticated HTTP/2 CONNECT with nested TLS certificate validation, then falls back to DNS-over-TCP over the same upstream. The relay limits message size and in-flight work, binds the UDP association to the loopback TCP peer and its advertised UDP source port when supplied, rejects private/internal resolver targets, and never falls back to direct networking. General UDP/QUIC is intentionally dropped because the current edge protocol supports TCP CONNECT only. This source is not yet live-verified.

The next required stage is a transactionally safe Windows route/DNS manager that pins the actual H2 edge endpoint to the original physical gateway before any tunnel default route, records only changes made by FoxyVPN, and restores those changes in reverse order on every failure/disconnect. Then wire the proxy session → SOCKS5 → HEV chain, configure DNS/fake-map behavior and test leak protection on Windows. Do not distribute as a production VPN until native CI builds, H2 and DNS interoperate with the real service, and TCP/DNS/IPv6/rollback tests pass.

For native dependencies see [`FoxyVPN.Windows/Native/README.md`](FoxyVPN.Windows/Native/README.md). Native binaries are generated by `.github/workflows/windows-build.yml`; they are not tracked in this source archive.


## Phase 1.7 — full-tunnel orchestration and egress guard

- `WindowsSystemTunnelRuntime` is registered with `WindowsTunnelEngine` and starts the authenticated transport, SOCKS5, native HEV/Wintun, then route/DNS transaction. Disconnect reverses the order, and the engine only reports connected while every runtime layer is active.
- `WindowsSystemRouteTransaction` creates an upstream physical-interface bypass for the gateway and actual edge/proxy endpoint before broad routes; installs the Wintun IPv4/IPv6 split-default routes; applies root DNS NRPT to HEV's fake-DNS address; and journals every route, prior interface metric, DNS comment and unique firewall-rule name under `%ProgramData%\FoxyVPN\runtime`.
- The transaction takes a named cross-process semaphore so two elevated app instances cannot simultaneously mutate system routes. Journal cleanup validates the stable `NetworkInterface.Id`, exact route attributes and restored interface metric before removing the persistent kill-switch firewall rules. If restoration cannot be proven, the journal and firewall guard remain for recovery.
- Before route changes, the route manager requires all Windows Defender Firewall profiles to be enabled. Its persistent outbound block rules are installed on every active non-loopback interface except FoxyVPN's own Wintun adapter; public IPv4/IPv6 ranges are blocked while the live upstream endpoint and private/link-local ranges remain excluded. External disabling of Windows Firewall while connected cannot be guaranteed against by application-level firewall rules and remains an explicit final-test/security-review item.
- Added a separately testable CIDR complement builder and xUnit source for IPv4/IPv6 public-range coverage, private/link-local exceptions, exact edge endpoint exemption, and non-overlapping ranges. Source tests are authored but **not run**.
- `HomeView` refreshes runtime status every two seconds while visible. If HTTP/2 or HEV unexpectedly dies, the UI exposes a faulted/recovery-needed state rather than leaving a false healthy connection badge.
- Optional upstream HTTP CONNECT and SOCKS5 chaining is retained in `WindowsHttp2ConnectSession`; connection failure never silently falls back to a direct socket.

### Still open after Phase 1.7

1. Build the pinned HEV Windows shared library and managed solution on actual Windows CI; verify symbol ABI, native load and real adapter startup.
2. Run all tests only at the final testing stage, as requested, then add/fix failures before packaging.
3. Validate real Guardian/edge interoperability, TLS/SNI and custom-edge behavior, DNS fake-map resolution, Windows 10/11 routing, sleep/wake and Wi-Fi changes, full IPv4/IPv6 DNS leak scenarios, process-crash recovery, firewall profile changes, and every startup/rollback failure injection.
4. Port the Android anti-bot challenge flow and split-tunneling exclusions where a safe Windows equivalent exists. General UDP/QUIC must remain marked unsupported until the server offers a compatible datagram transport.
5. Produce and install-test the actual portable build/EXE only after final verification.

No successful build, automated test run, live VPN connection, or installer is claimed for this Phase 1.7 source archive.


## Phase 1.8 — connectivity lifecycle and proxy-pass renewal

The runtime now listens for network-address/availability changes and Windows suspend/resume notifications. After a short debounce it expands the persistent physical-egress block rules to newly connected or renamed adapters; new interfaces never get the pinned upstream endpoint exception. It verifies that the upstream local IP still belongs to an active adapter and checks HTTP/2, HEV and route-transaction health. If the old transport becomes unhealthy, the runtime changes to Faulted/safety-hold and preserves the route journal and guard for orderly disconnect/recovery. It does **not** transparently reconnect onto uncertain routes.

Guardian proxy passes now have a half-lifetime refresh worker (clamped to a 15-second minimum and 30-minute maximum; a two-minute fallback when expiry is unknown). New HTTP/2 CONNECT streams obtain the refreshed cached token while current streams remain untouched. Token values are not logged. Source tests cover the pure renewal-delay policy; they have not been executed yet.

**Limits:** the network event handler is best-effort; Windows adapter handovers and external Firewall-service changes still require live testing, and zero-leak behavior is not claimed until tested on real Windows 10/11. The anti-bot challenge solver and per-application split tunneling are not ported. General UDP/QUIC remains unsupported by the current edge.


## Phase 1.9 — user-mediated challenge and startup completion

- Added `InteractiveChallengeWindow` using the official WPF WebView2 control. It opens only allow-listed HTTPS Firefox/Guardian service pages and relies on the user to complete any verification. DevTools are disabled; pop-ups and navigation are limited to HTTPS Firefox/Mozilla domains. A per-origin WebView2 user-data directory avoids sharing the browser profile between unrelated service origins.
- Added `InteractiveChallengeCookieBridge`: enforces an explicit origin allow-list, cookie-count and field-size bounds, secure cookies only, expiry checks, path validation, control-character checks, and a domain-to-origin check before importing into the relevant API client's in-memory cookie jar. No cookies, passwords or bearer tokens are logged.
- Firefox Accounts sign-in, verification-code submission, token refresh in the account page, Guardian account refresh, proxy-only connect, system-tunnel connect and connect-on-launch all permit a single user-mediated challenge completion and one retry. A repeated 406 is surfaced, and the background token-renewal loop stops rather than hammering a challenge endpoint.
- `ConnectOnLaunch` is now gated behind recovery-journal reconciliation; automatic full-tunnel startup is skipped when administrator privileges or safe route recovery are not available.
- Added negative tests for malformed cookie snapshots. These are test **source only**; do not mark tests/build as passed until final validation.

**Still open:** run full restore/build/test on Windows at the end of coding; test WebView2 challenge behavior against live service responses; verify native HEV/Wintun ABI and packaging; implement/verify safe per-app split tunneling only if a genuine per-process routing design is selected; test real TCP and DNS forwarding, IPv4/IPv6 leaks, UDP limitations, sudden process termination, route rollback, sleep/wake and Wi-Fi handover. No installer binary is included in this source archive.


## Phase 2.0 — upstream proxy-chain hardening

Version metadata is `2.0.0`. `WindowsHttp2ConnectSession` exposes internal-only deterministic protocol seams for the HTTP CONNECT and SOCKS5 pre-TLS handshake. `FoxyVPN.Tests/UpstreamProxyHandshakeTests.cs` covers HTTP CONNECT Basic auth, strict HTTP 200 acceptance, IPv6 authority formatting, SOCKS5 no-auth and RFC 1929 auth exchanges, bad upstream responses, and malformed credentials. The preflight reports incomplete SOCKS5 credentials before trying to connect. Temporary username/password byte buffers are zeroed via `finally`, including failed writes. These tests are authored but intentionally not run until the owner-directed final test stage.


## Phase 2.1 — tunnel lifecycle hardening

- HTTP/2 CONNECT streams now allow the client-to-edge direction to remain open after a remote `END_STREAM`; the two directions are independently half-closed as required for TCP semantics.
- `IWindowsTunnelRuntime` exposes an optional `IsSafetyHold` signal. `WindowsTunnelEngine.State` reports `Faulted` while route/firewall cleanup is uncertain; Connect is refused until explicit recovery, and Disconnect does not early-return when a safety hold remains.
- A cancelled startup becomes `Disconnected` only after the runtime's `StopAsync` returns and confirms both `IsConnected == false` and `IsSafetyHold == false`. Failed or uncertain cleanup stays `Faulted`.
- `WindowsTunnelEngine` and `WindowsSystemTunnelRuntime` now return the same async disposal task to repeated callers. Their small synchronization primitives are not disposed while late operation waiters or network callbacks may still unwind.
- Added test source for failed cleanup, safety-hold recovery, held-state connect rejection, cancelled startup cleanup and concurrent `DisposeAsync`. These tests are intentionally not run until the coding checklist is complete.

Application metadata is `2.1.0` in the WPF project, Inno Setup script and Windows CI installer command. Static source inspection is not a substitute for compilation. The final Windows restore/build/test, native HEV/Wintun acceptance, route/DNS rollback and IPv4/IPv6/DNS leak matrix remain pending.


## Phase 2.4 — HTTP/2 informational responses and CI alignment

Version metadata is `2.4.0` in the WPF project, Inno Setup script and Windows CI installer invocation. The CONNECT session now treats legal informational HTTP/2 response statuses (100–199 except 101) as preliminary headers and waits for the final status; it rejects an informational response carrying `END_STREAM`. Test source was added but not run. The CI installer command was corrected from stale `2.2.0` metadata and the workflow artifact list now includes the current phase documentation. No build, live tunnel, leak test or installer generation is claimed.

## Phase 2.3 — HTTP/2 protocol hardening

Version metadata was `2.3.0` in the WPF project and Inno Setup script; Phase 2.4 found and corrected the stale Windows CI installer command. This increment serializes stream-ID allocation and HEADERS emission, keeps ID arithmetic from wrapping, bounds frame writes, aborts the session after a potentially partial frame write, validates GOAWAY/idle-stream state, and applies response header count/decoded-byte/name/value limits. Pure protocol rule tests were added but not run, following the owner’s request to defer comprehensive tests until coding is complete. No managed/native build, live VPN, leak check or installer generation is claimed.


## Phase 2.5 — DNS response correlation hardening

The Windows SOCKS5 DNS relay now validates response correlation against the original query before returning any upstream answer: transaction ID, QR/opcode, question count, and each canonical DNS question name/type/class must match. DNS name parsing is length-bounded, validates label sizes and message bounds, and rejects forward/self-referential compression pointers. A mismatched DoH answer triggers the existing DNS-over-TCP-over-authenticated-CONNECT fallback; a mismatched TCP response is discarded. Neither path opens a direct-network socket. xUnit source covers valid matches, ID/opcode/QR mismatches, different questions and malformed/truncated names. These tests have not been run. Active Windows version metadata is now `2.5.0`; no compile, native ABI, end-to-end VPN, leak or installer-generation result is claimed.


Phase 2.6: DoH HTTP response headers are now parsed by a strict bounded helper. Duplicate Content-Length/Content-Type, transfer encoding, malformed/folded headers, invalid field names, control/non-ASCII bytes, and out-of-range body lengths are rejected. xUnit source covers these cases but has not been run. Active version metadata is 2.6.0; no build or live VPN validation is claimed.
