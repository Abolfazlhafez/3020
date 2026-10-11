# FoxyVPN Android → Windows migration report

**Source inspected:** `fox5-main` Android/Kotlin project, version `1.0.5`, version code `42`.

**Windows architecture selected:** C# / .NET 8 / WPF. WPF is a native Windows desktop UI framework with window resizing, keyboard/mouse input, DPI-aware manifest settings and standard Windows installer/publish options; it is not a WebView app wrapper or emulator (WebView2 is used only for the explicit service-verification dialog). API/data/authentication code is separated from views in `Services/`, domain types in `Models/`, and screens in `Views/`.

**Status (latest Phase 2.5):** HTTP/2 stream-state/frame-write hardening, informational-response handling, and DNS response-correlation validation plus deterministic protocol-rule test source are authored. The broader Phase 1.9 interactive challenge and startup-flow source remains authored; the Windows VPN still is **not yet verified as a production client**. The Windows build and tests could not be executed in this sandbox: the environment has no .NET SDK/MSBuild and DNS/network access to install the SDK is unavailable. An automated Windows CI workflow and build/test instructions are included. The installer script is source only; no installer EXE or runnable Windows binary was generated in this environment.

## Feature mapping and current verification state

| Android feature/component | Windows implementation | Current state |
|---|---|---|
| Jetpack Compose navigation, Home, Login, Servers, Account, Settings, Logs | WPF `MainWindow` and separate `UserControl` views | Source created; UI has not been launched/visually tested on Windows |
| Firefox Accounts email/password login derivation | `FirefoxAuthService` PBKDF2-HMAC-SHA256 + HKDF authPW derivation | Implemented in source; API not verified from this environment |
| Firefox Accounts email 2FA | Hawk-authenticated verification-code request | Implemented in source; API not verified |
| FxA OAuth token exchange and refresh | `FirefoxAuthService` | Implemented in source; API not verified |
| Android `EncryptedSharedPreferences` token store | Current-user Windows DPAPI-protected token file | Native replacement implemented in source; not runtime-tested |
| Mozilla Remote Settings server list | `ServerCatalogService`, live HTTP request and filtering/flattening parser | Implemented in source; parser unit tests authored but not executed |
| Guardian status/quota, activation, proxy-pass request | `GuardianClient` | Implemented in source; API not verified |
| Android `SharedPreferences` settings | DPAPI-protected Windows settings file with validation and atomic writes | Implemented in source; not runtime-tested |
| Android application logs | Rolling local log with WPF log viewer | Implemented in source; not runtime-tested |
| Light/dark/system appearance preference | WPF theme resource switch; System mode reads Windows AppsUseLightTheme | Implemented in source; not visually verified |
| System-wide VPN / `VpnService.Builder` routing | Registered WPF runtime, HEV/Wintun native bridge, route/DNS transaction, edge bypass and persistent firewall egress guard | **Source integrated; Windows build/live tests not yet executed** |
| Native `hev-socks5-tunnel` Android JNI / TUN descriptor | Pinned HEV 2.17.1 C-API bridge, Android-compatible YAML writer, Windows runtime orchestration, and CI native source-build/bundle step | **Source integrated; build/runtime unverified** |
| Custom HTTP/2 upstream multiplexer, stream backpressure/keepalive, proxy-pass rotation | Native C# TLS/ALPN h2 session, CONNECT streams, flow control, ping and Guardian proxy-pass provider | **Source implemented; real service interoperability unverified** |
| Local SOCKS5 TCP/UDP forwarding, DNS-over-HTTPS relay | TCP CONNECT plus DNS-only UDP ASSOCIATE source; DNS tries DoH through authenticated H2 CONNECT then DNS-over-TCP through the same upstream; other UDP/QUIC dropped | **Source and deterministic tests added; not executed/live-verified** |
| Proxy-only mode | HTTP/2 provider connected to loopback SOCKS5 CONNECT and DNS-only UDP relay, with optional upstream HTTP CONNECT/SOCKS5 chaining | **Wired in source; real endpoint interoperability unverified** |
| Split tunneling / excluded applications | Android package-based exclusion has no safe direct equivalent in current Windows route transaction | **Not implemented; UI explicitly warns against a misleading placeholder toggle** |
| Connect-on-launch, connection timer, traffic speed/usage, exit verification | UI/settings groundwork only; no live tunnel state to drive them | **Not implemented against a live connection** |
| Android Quick Settings tile, foreground service, notification and reboot behavior | Desktop equivalents not created | Quick Settings tile is Android-specific; Windows tray/notification/background-service behavior remains unimplemented |
| Fastly anti-bot challenge handling used by FxA/Guardian requests | User-mediated WebView2 verification window + strict cookie import, no automatic solver | **Source implemented; live challenge compatibility not verified; a repeated 406 remains an error** |
| Portable executable and installer | Publish command + Inno Setup installer script + Windows CI workflow | Scripts created; **not built or verified here** |

## Security/data migration

- The Android project is preserved unchanged alongside the new `windows/` directory.
- The Windows app does not import or decrypt Android `EncryptedSharedPreferences` records. Android's keystore-bound tokens are not portable across operating systems; users must sign in again on Windows. This avoids an unsafe cross-platform key migration.
- Windows session tokens and settings are protected with DPAPI for the current Windows user. The user's sign-in password is not stored.
- No mock server list, fabricated quota, or simulated connected state is used. Failed/unavailable HTTP calls are shown as errors.
- The Firefox Accounts client identifier copied from the Android source is a public client identifier, not a secret. No private API key was added.

## Completion checklist

- [x] Inspect Android source tree, dependencies, screens, network/auth layer, and VPN implementation boundaries.
- [x] Preserve the original Android source tree.
- [x] Create a native WPF project and Visual Studio solution.
- [x] Create source for account sign-in/2FA/token refresh, server catalog, Guardian API client, settings/logs, and native desktop screens.
- [x] Add parser/settings automated tests.
- [x] Add Windows build/portable publish/installer CI source.
- [ ] Compile Windows solution on Windows 10/11.
- [ ] Run automated tests on Windows.
- [ ] Verify real Firefox Accounts and Guardian operations, including 2FA and challenge responses.
- [x] Add Windows TUN lifecycle, HTTP/2 stream engine, SOCKS5 and DNS relay source, route/DNS transaction and persistent egress guard.
- [ ] Verify system-wide tunnel, proxy-only mode, DNS, traffic counters, reconnection and route rollback.
- [ ] Produce and test an actual installer and portable build.
- [ ] Complete end-to-end feature parity and release acceptance.

The final acceptance criteria are **not met** until the unchecked tunnel/build/runtime verification items are complete.


## Phase 1.1 feature additions

| Feature | Implementation | Status / verification |
|---|---|---|
| Windows auto-start | Per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registration, controlled from Settings | Source implemented; registry behavior not executable-tested in this environment |
| Start minimized | Startup entry adds `--minimized`, parsed during WPF startup | Source implemented; UI launch not runtime-tested |
| Favorite locations | Endpoint key persisted in settings; favorite-only filtering in live locations page | Source implemented; UI behavior not runtime-tested |
| TCP reachability test | `TcpClient.ConnectAsync` with four-second timeout and explicit TCP-only messaging | Source implemented; network check not run in this environment |
| Automatic catalog refresh | DispatcherTimer, configurable 5/15/30 minutes, only refreshes while the Locations page is visible | Source implemented; timer not runtime-tested |
| Diagnostics ZIP | ZIP with runtime/environment report plus current and rotated logs; report excludes account IDs/tokens/passwords/endpoint hostname | Source implemented; file export not runtime-tested |

Static source checks can validate XAML/XML and references, but cannot certify compilation. The primary blocker is still the missing Windows .NET SDK/build toolchain; the Windows VPN data plane also remains unported.

## Phase 1.2 — desktop motion and visual design

| Area | Implementation | Verification state |
|---|---|---|
| Modern 2027 desktop treatment | Updated WPF palette/tokens, responsive dashboard layout, gradient hero, clearer control-center hierarchy | XAML/XML static validation only; no Windows visual review available here |
| Navigation motion | 230 ms opacity easing + 310 ms vertical ease-out on page changes | Source implemented; runtime animation not tested |
| Component micro-interactions | Button scale feedback; card hover scale, accent outline and shadow elevation | Source implemented; runtime animation not tested |
| Animated privacy core | Concentric vector rings, slow orbit rotation and ambient glow while Home is loaded | Source implemented; lifecycle stops loops when Home unloads; runtime/performance not tested |
| Reduced motion | Persisted `ReduceMotion` setting disables Home ambient loops and page slides | Source implemented; accessibility behavior not runtime-tested |
| Version / installer metadata | Windows project and Inno Setup/CI installer version set to 1.1.0 | Static configuration check only; no installer generated |

The visual motion is decorative only. It is not evidence of a secure or active tunnel; connection status stays honest while the Windows data plane is unavailable.



## Phase 1.3 — readiness and lifecycle foundation

| Area | Implementation | Status / verification |
|---|---|---|
| Tunnel readiness report | Structured checks for Windows baseline, auth session, selected endpoint, Wintun DLL presence, HTTP/2 CONNECT, SOCKS5 relay, and route/DNS lifecycle | Source added; XML/XAML static checks passed; unit tests authored but not executed; report intentionally remains blocked because core data plane is missing |
| Wintun preflight | Checks for a side-by-side `wintun.dll` without loading native code or modifying system state | Source added; no driver or DLL bundled; Windows runtime not tested |
| Tunnel engine lifecycle | Serialized/cancellation-aware state machine behind `IWindowsTunnelRuntime`; Connected requires runtime confirmation | Source added; fake-runtime unit tests authored, not executed |
| Readiness UI | Home action lists and logs each preflight blocker | Source added; not launched on Windows |
| Version metadata | Windows app and installer/CI version set to 1.3.0 | Static source/config check only |

### Phase 1.3 checklist

- [x] Add structured readiness model and visible preflight output.
- [x] Add Windows TUN runtime presence probe that never loads native code or changes routes.
- [x] Add explicit, serialized tunnel lifecycle and runtime injection contract.
- [x] Ensure the engine cannot report Connected without runtime confirmation.
- [x] Add unit test source for missing components, startup failures, and disconnect transitions.
- [ ] Run restore/build/tests on Windows 10/11 or Windows CI and retain logs.
- [ ] Integrate a signed/validated Windows TUN adapter and packet I/O.
- [ ] Port and verify HTTP/2 CONNECT multiplexing and Bearer proxy-pass handling.
- [ ] Port and verify SOCKS5 TCP/UDP and DNS relays.
- [ ] Implement route/DNS transactions, rollback and fail-closed behavior.
- [ ] Perform end-to-end data-plane and leak/rollback integration tests.

**Current acceptance status remains incomplete.** The authoring environment still lacks the .NET SDK and MSBuild; no build, runtime test, portable executable, or installer was generated here. The actual Windows VPN data plane remains unimplemented.


## Phase 1.4 — SOCKS5 CONNECT foundation

| Area | Implementation | Status / verification |
|---|---|---|
| SOCKS5 listener | `WindowsSocks5ProxyServer`: loopback-only listener, SOCKS5 no-auth greeting for local clients, domain/IPv4/IPv6 CONNECT parsing, bounded connection count, handshake/upstream timeouts | Source added; not activated by the app because no real HTTP/2 upstream provider is registered |
| Upstream safety | Requests are forwarded exclusively through injected `IUpstreamStreamProvider`; this class never creates a direct destination socket | Source inspection only; real upstream integration absent |
| Destination protections | Blocks loopback, RFC1918 IPv4, IPv4 link-local, IPv6 link-local/unique-local/multicast, localhost and local/internal suffixes | Test source includes loopback blocking; tests not run |
| Relay lifecycle | Bidirectional copy, cancellation, connection cleanup and TCP half-close drain | A local fake-upstream echo test was authored; not executed in this environment |
| Unsupported protocol | As of Phase 1.6, UDP ASSOCIATE has DNS-only relay source; older Phase 1.4 behavior rejected it | DNS fallback/drop tests authored; not executed |
| Version metadata | Windows project, shell label, installer and CI use 1.4.0 | Static source/config inspection only |

### Phase 1.4 checklist

- [x] Add loopback-only SOCKS5 CONNECT source behind an injected upstream transport contract.
- [x] Avoid direct-to-destination fallback and refuse start while upstream is disconnected.
- [x] Add test source for binding, upstream gating, relay data and half-close, local destination blocking, and UDP rejection.
- [x] Document that UDP/DNS is unsupported and the listener is not integrated into the app.
- [ ] Run `dotnet restore`, Release build, and xUnit tests on Windows CI; fix actual compiler/test failures.
- [ ] Implement and verify real HTTP/2 TLS/ALPN h2 CONNECT multiplexing, then supply its stream provider.
- [ ] Port SOCKS5 UDP + DoH DNS, Windows TUN packet I/O, transactional routes/DNS rollback, pass renewal and leak protection.

The Phase 1.4 source is still a protocol foundation, not a working Windows VPN. The .NET SDK/MSBuild was unavailable here, so tests/build and runtime behavior remain unverified. No EXE/MSIX/installer is claimed or included.

## Phase 1.5 — HTTP/2 CONNECT and explicit proxy-only mode

| Area | Implementation | Status / verification |
|---|---|---|
| TLS / ALPN | `WindowsHttp2ConnectSession` uses TLS certificate validation and requires ALPN `h2`; preserves VPN hostname for SNI when custom edge IP is used for TCP connect | Source implemented; compilation and live handshake not verified |
| HTTP/2 CONNECT | Multiplexed classic CONNECT streams, SETTINGS, HEADERS/CONTINUATION, DATA, WINDOW_UPDATE, PING, RST_STREAM, GOAWAY and flow-control code | Source implemented; not RFC conformance tested; requires Windows CI build and mock/live interoperability tests |
| Header encoding | `:method`, `:authority`, never-indexed `proxy-authorization: Bearer …`; no `:scheme` or `:path` on classic CONNECT | HPACK decoder test source added; test not executed |
| Guardian pass | Access-token refresh, proxy-pass retrieval and short expiry-aware cache; token value is not logged | Source implemented; Guardian anti-bot HTTP 406 solver is not ported |
| Proxy-only mode | App can bind local SOCKS5 to 127.0.0.1/::1 and route explicitly configured app TCP connections via HTTP/2 CONNECT | Wired in source; no end-to-end connection verified; no system route modification |
| Dependency | NuGet `hpack` 1.5.1, notice under `THIRD_PARTY_NOTICES.md` | Package restore not performed in this environment |
| Static check | 10 XML/XAML/project files parse, no missing/duplicate XAML event/name findings, no gross C# delimiter imbalance | Lightweight static checks only; not equivalent to compiler or test results |

### Phase 1.5 checklist

- [x] Add a TLS + ALPN h2 HTTP/2 CONNECT source implementation.
- [x] Add classic CONNECT header encoding and never-indexed Bearer authorization.
- [x] Add Guardian proxy-pass caching and connect it to the current auth service.
- [x] Wire the transport to a loopback-only SOCKS5 TCP proxy when Proxy-only mode is explicitly enabled.
- [x] Keep the full-system tunnel unregistered and fail-closed.
- [x] Add header-encoding test source and document the HPACK dependency/license.
- [x] Perform lightweight static XML/XAML/reference/delimiter checks.
- [ ] Restore, build and run tests on Windows 10/11 or Windows CI.
- [ ] Test the HTTP/2 implementation against a local mock peer and an explicitly permitted compatible endpoint.
- [ ] Implement and validate Wintun/TUN packet I/O, TCP flow mapping, UDP/DNS relay, route transactions/rollback, reconnect/token refresh, upstream proxy chaining and leak protection.
- [ ] Generate and test a portable build and installer only after the preceding criteria pass.

**Acceptance remains incomplete.** Phase 1.5 is a transport/proxy-only implementation source, not a verified system-wide Windows VPN. The authoring environment lacks `dotnet`/MSBuild and could not restore the NuGet dependency. No successful compile, test run, live VPN connection, or installer generation is claimed. See `../PHASE_1_5_VPN_TRANSPORT_REPORT.md` and `../CONTINUATION_PROMPT_PHASE_1_5.md`.


## Phase 1.6 — HEV native core and DNS-only UDP relay source

- `WindowsHevConfigWriter` emits a secret-free HEV YAML configuration using Android-compatible fake-DNS map (`198.18.0.2`, `100.64.0.0/10`), MTU 8500 and tunnel IPv4 `10.8.0.2`; Windows interface name/GUID and loopback SOCKS5 endpoint are added. IPv6 config is prepared but not runtime-verified.
- `WindowsHevNativeCore` binds the C API `hev_socks5_tunnel_main_from_file` and `hev_socks5_tunnel_quit`, serializes start/stop/dispose, checks elevation and side-by-side binaries, waits for the HEV-owned Wintun interface, and refuses to unload the native library if the native worker may still be active. It is a lifecycle helper only and is not registered as `IWindowsTunnelRuntime`.
- Windows CI source-builds HEV `2.17.1` under MSYS2, checks exported symbols and x64 PE format, and bundles official Wintun `0.14.1` only after archive SHA-256 and Authenticode publisher checks. **This workflow has not run in this authoring environment**, so the resulting binary and its runtime dependencies remain unverified.
- `WindowsSocks5ProxyServer` now supports RFC 1928 UDP ASSOCIATE for a loopback client endpoint. `WindowsSocks5UdpDnsRelay` processes only DNS packets to port 53, bounds packet size/in-flight queries/timeouts, validates DNS transaction ID and response flag, tries DoH over a TLS-validated nested connection reached via the authenticated H2 CONNECT stream, then falls back to DNS-over-TCP through the same upstream. Other UDP, including QUIC, is dropped because the current edge protocol only exposes TCP CONNECT. No direct external socket fallback is attempted.
- The UDP association now validates that its advertised client IP is wildcard or matches the loopback TCP peer and binds to the advertised UDP source port if nonzero, rather than incorrectly interpreting the SOCKS UDP ASSOCIATE port field as the DNS destination port.
- Added deterministic test source for HEV YAML values, the DNS UDP association → fake DoH failure → DNS-over-TCP fallback path, and the no-upstream-open behavior for non-DNS UDP. Tests are **not run**.
- Exposed the actual HTTP/2 edge `RemoteEndPoint` so a future route manager can retain a physical-interface bypass. No routes/DNS are modified at this phase.

**Phase 1.6 acceptance is not met.** The native HEV helper remains disconnected from the app's system runtime; the HTTP/2 stack, DoH/DNS relay, and native library have not compiled or been verified against the live service. Still blocked: orchestration order between authenticated proxy session → loopback SOCKS → HEV, safe elevated route/DNS transaction and reverse rollback, edge-route bypass via the original gateway, kill-switch/leak protection, IPv6 verification, Windows integration tests, and actual build/installer outputs. Keep system VPN mode fail-closed until all those checks pass.


## Phase 1.7 — full-tunnel orchestration source

Phase 1.7 registers `WindowsSystemTunnelRuntime` and wires startup as authenticated HTTP/2 edge → loopback SOCKS5 → HEV/Wintun → route/DNS transaction. Disconnect attempts route/DNS rollback before stopping forwarding components. `WindowsTunnelEngine.State` now exposes `Faulted` if its registered runtime no longer confirms a previously connected tunnel, and the Home page refreshes status while visible.

The new `WindowsSystemRouteTransaction` journals route mutations, stable adapter IDs, prior IPv4/IPv6 interface metrics, unique root NRPT DNS ownership and persistent Windows Firewall rule names. It creates the original physical gateway/edge endpoint bypass before tunnel routes, applies IPv4/IPv6 split defaults, and only removes its firewall block after routes and metrics are confirmed restored. A named cross-process semaphore serializes route changes; the route transaction refuses to begin if Windows Defender Firewall profiles are disabled. The public-address CIDR complement is isolated into `WindowsKillSwitchAddressRangeBuilder`, with xUnit source covering IPv4/IPv6 public blocking, private/link-local exceptions, upstream endpoint exemptions and non-overlapping ranges.

Configured upstream HTTP CONNECT/SOCKS5 chaining is implemented at the TCP transport layer with optional authentication; failure does not downgrade to a direct connection. General UDP/QUIC remains unsupported by the current TCP-CONNECT-only service. Remaining parity items include general UDP/QUIC under a compatible server protocol and a genuine application-specific routing design. The Android automatic anti-bot solver remains intentionally unported; Phase 1.9 adds user-mediated verification instead.

**Testing policy (per user request):** keep coding and integration first; run the comprehensive test/build/live-network suite at the end. This environment cannot run that suite because `.NET SDK`/MSBuild is absent. The current source and native CI path are not compiler-validated, and no live VPN / installer is claimed.

### Phase 1.7 checklist

- [x] Register and wire the full system-tunnel runtime and rollback order.
- [x] Add physical gateway/upstream endpoint bypass routes and IPv4/IPv6 split-route source.
- [x] Add journaled route ownership, prior interface metric restoration, root NRPT ownership, and cross-process serialization.
- [x] Add persistent IPv4/IPv6 Windows Firewall public-egress guard; fail closed if Windows Firewall profiles are disabled.
- [x] Add unit-test source for kill-switch CIDR computation.
- [x] Reflect lost runtime health in the visible UI instead of leaving a stale Connected state.
- [ ] Complete all code parity items and then run the requested final managed/native/Windows integration/leak test suite.
- [ ] Publish and install-test actual Windows x64 portable and installer artifacts.


## Phase 1.8 — connectivity lifecycle and proxy-pass renewal

The registered system runtime now listens for Windows network address/availability changes and power suspend/resume events. After a short debounce, it adds journaled public-egress block rules for new or renamed physical adapters and never gives those adapters the active edge endpoint exception. If the upstream local address disappears or the transport/core/route layer no longer reports healthy, the runtime marks the session Faulted and retains the route transaction, persistent firewall guard, and recovery journal. It requires explicit disconnect/recovery before reconnecting; no reconnect is attempted while route ownership is uncertain.

`GuardianProxyPassProvider` supports a forced refresh path while preserving its short cache, and the full-tunnel runtime refreshes at approximately half the remaining pass lifetime, bounded to 15 seconds–30 minutes. The new token is used for future CONNECT streams; active streams are not torn down solely for refresh. A pure timing-policy helper and xUnit source were added. No unit tests, build, live connection, adapter handover, sleep/resume or leakage test has yet been executed.

### Phase 1.8 checklist

- [x] Add network address/availability and suspend/resume event hooks.
- [x] Extend the journaled physical egress guard to new/renamed active adapters.
- [x] Fault closed when the current upstream local address disappears or a runtime layer is unhealthy; do not reconnect over uncertain routes.
- [x] Add proactive Guardian proxy-pass renewal and timing-policy test source.
- [ ] Add an approved/user-mediated anti-bot challenge flow equivalent enough for account API use.
- [ ] Determine and implement a correct native Windows per-application split-tunnel design, or clearly mark it unsupported if no safe implementation can be verified.
- [ ] Run final managed/native build, end-to-end Windows/CI integration, DNS/IP leak, sleep/resume, adapter-change and route-recovery tests after coding is complete.


## Phase 1.9 — interactive verification and startup flow

- [x] Add an explicit user-mediated WebView2 challenge dialog for HTTPS Firefox Accounts and Guardian service origins; do not auto-solve the Fastly challenge.
- [x] Add a cookie-import bridge with an origin host allow-list, cookie and field size limits, secure-cookie requirement, expiry validation, path validation, control-character rejection and domain-scope validation. Cookies are imported only into the corresponding API client's in-memory `CookieContainer`; cookie values are never logged.
- [x] Separate WebView2 user-data folders by approved service origin.
- [x] Handle the challenge for initial sign-in, email verification code, token refresh, account usage, proxy-only startup, full-system startup and connect-on-launch; every operation gets at most one user-mediated retry. A repeated challenge is surfaced to the user.
- [x] Gate connect-on-launch on completion of any previous route recovery journal and skip startup when elevated recovery is required but not available.
- [x] Correct the settings and reports to label per-application split tunneling as unavailable rather than implying it works.
- [x] Add unit-test source for malformed cookie snapshots; all comprehensive tests remain deferred as explicitly requested.

### Phase 1.9 limits and required final verification

The browser challenge flow relies on WebView2 Evergreen Runtime being present and on the relevant service presenting a user-actionable verification page at its approved origin. If that service requires a different challenge workflow or no eligible cookies are issued, the operation fails closed and asks the user to retry/contact service support; no bypass is attempted. The WPF/managed project and native HEV/Wintun have not been built in this authoring environment, and these tests have not been run. End-of-coding validation must include real service challenges, token refresh after expiry, duplicate retry prevention, Windows 10/11 route/DNS rollback, full IPv4/IPv6/DNS leak tests, native dependency load, installer/portable packaging and runtime shutdown/crash recovery.


## Phase 2.0 — upstream proxy-chain hardening (current source state)

- Application version metadata is `2.0.0` across the WPF project, CI installer invocation and Inno Setup script.
- HTTP CONNECT and SOCKS5 upstream handshakes in `WindowsHttp2ConnectSession` now expose internal test seams via `InternalsVisibleTo` only for `FoxyVPN.Tests`; production calls use the same implementation.
- Preflight validates upstream-proxy credentials before the TCP socket is opened. SOCKS5 username/password authentication requires both values to be non-empty and each UTF-8 encoding to be 1–255 bytes. HTTP Basic credentials are bounded to 1024 UTF-8 bytes per field; password-only configuration is rejected.
- Temporary SOCKS5 and HTTP Basic credential byte arrays are zeroed in `finally` blocks, including error/cancellation paths.
- New scripted-stream tests cover successful/rejected HTTP CONNECT, Basic auth, bracketed IPv6 authority, SOCKS5 no-auth, RFC 1929 authentication, wrong authentication choice, rejected CONNECT, incomplete credentials and credential lengths over the RFC limit. A readiness test covers an incomplete upstream SOCKS5 password.
- **Validation status:** these tests are authored and not executed. No `dotnet restore/build/test`, native HEV build, installer build, live service connection, or end-to-end tunnel/leak test has been performed during this increment. The user explicitly requested the complete coding phase precede comprehensive testing.


## Phase 2.1 — tunnel lifecycle hardening

- Application version metadata is now `2.1.0` across `FoxyVPN.Windows.csproj`, `windows/installer/FoxyVPN.iss`, and the Windows CI installer invocation.
- Fixed the HTTP/2 duplex write gate so remote `END_STREAM` closes only the incoming direction. This preserves TCP half-close behavior for CONNECT streams when the remote endpoint finishes sending but still expects client data.
- Added `IWindowsTunnelRuntime.IsSafetyHold` and surfaced it from the Windows runtime through the engine state. The UI-facing engine reports `Faulted` while the runtime cannot prove route/firewall restoration, refuses new connect attempts during that hold, and allows explicit disconnect/recovery to run even if the data plane is no longer connected.
- Startup failure cleanup is now checked before a cancelled operation may be shown as `Disconnected`; a failed rollback or safety hold remains `Faulted`.
- Repeated disposal calls on `WindowsTunnelEngine` and `WindowsSystemTunnelRuntime` share the same asynchronous cleanup task. Gates and the cancelled environment lifetime source are retained so pending waiters/callback continuations can unwind without racing against `Dispose` on semaphores/CTS objects.
- Added xUnit source for cancelled startup with verified cleanup, failed cleanup staying faulted, explicit recovery while the raw engine state is disconnected, connect rejection during safety hold, and concurrent disposal callers.

**Validation:** no `dotnet restore`, `dotnet build`, `dotnet test`, HEV/Wintun native build, live service connection, route rollback acceptance, DNS/IPv4/IPv6 leak test or installer generation was run in this coding increment. The user's instruction is to finish coding before comprehensive final testing. Static XML/project/YAML and source-structure checks only were performed during packaging.


## Phase 2.3 — HTTP/2 protocol hardening

- WPF, installer and Windows CI version metadata is `2.3.0`.
- A new `Http2ProtocolRules` helper validates monotonic GOAWAY IDs, idle client stream IDs, three-digit status syntax, lowercase/token header names, pseudo-header ordering and forbidden control bytes.
- Concurrent stream opens serialize stream ID allocation/registration/HEADERS emission. Stream-ID arithmetic uses `long`.
- Frame writes are bounded; cancellation cannot interrupt a frame after writing begins, and a stalled/failed partial write aborts the connection. CONNECT rejection has a single reset owner to avoid duplicate RST_STREAM frames.
- Added xUnit test source for protocol rules. It has not been executed.
- No .NET build/test, native HEV/Wintun build, live service tunnel, route rollback test, DNS/IPv4/IPv6 leak test or installer creation was executed.


## Phase 2.4 — HTTP/2 informational responses and build metadata

- Windows application, installer default and CI installer invocation are aligned at version `2.4.0`; the workflow's stale `2.2.0` argument was corrected.
- HTTP/2 CONNECT now ignores preliminary informational response blocks (100–199 excluding 101) and awaits the final response. The implementation rejects `END_STREAM` on an informational response.
- Added pure test source for informational-response classification. Tests were intentionally not run before completion of the coding audit.
- Added Phase 2.4 report, continuation prompt and manifest. Android sources were preserved.
- No managed/native build, live service connection, route rollback test, DNS/IPv4/IPv6 leak validation or installer generation is claimed.


## Phase 2.5 — DNS response correlation hardening

- Added `DnsMessageRules` to validate transaction ID, query/response bit, opcode, question count, and semantic DNS question tuples (case-insensitive ASCII names, record type and class).
- The parser bounds names to DNS's 255-octet expanded-name limit, bounds all reads to the message, rejects malformed label encodings, and refuses forward/self-referential compression pointers.
- DNS-over-HTTPS answers that fail correlation are not returned; the relay attempts the existing DNS-over-TCP path through the authenticated upstream. A mismatched DNS-over-TCP answer is dropped. Neither path adds a direct network fallback.
- Added `DnsMessageRulesTests.cs` with test source for positive correlation, transaction ID, QR flag, opcode, question name/type/class, truncation and malformed compression pointers. Tests are intentionally not executed until coding is declared complete.
- Active Windows application, installer and CI version metadata is `2.5.0`. Android sources were not intentionally changed.
- No .NET restore/build/test, HEV/Wintun native build, live tunnel, DNS/IPv4/IPv6 leak test, rollback test or installer generation is claimed.


### Phase 2.6 update
- Strict DoH HTTP response parsing rejects ambiguous framing and malformed headers before accepting a DNS response. Test source was added but not run. Windows app, installer and CI version metadata are aligned at 2.6.0. Runtime validation remains pending.
