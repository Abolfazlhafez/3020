# FoxyVPN Windows — Phase 1.5 VPN Transport Report

**Source version:** 1.5.0  
**Scope:** Windows TLS/HTTP/2 CONNECT transport, Guardian proxy-pass handling, and explicit loopback SOCKS5 proxy-only wiring.  
**Acceptance status:** partial implementation; not build-verified, not live-interoperability-tested, and not a complete system-wide VPN.

## What was added

### 1. TLS and HTTP/2 transport source

`windows/FoxyVPN.Windows/Services/WindowsHttp2ConnectSession.cs` now contains a direct TCP + TLS 1.2/1.3 session that:

- validates the TLS certificate and keeps the selected VPN hostname as the TLS target/SNI name, even if a custom edge IP is used for the TCP connection;
- requires ALPN `h2` and fails instead of silently falling back to HTTP/1.1;
- exchanges the HTTP/2 client preface and SETTINGS, and does not report the transport initialized until the peer's initial SETTINGS frame is received;
- opens multiplexed odd-numbered HTTP/2 streams using the classic CONNECT form;
- manages core SETTINGS, HEADERS/CONTINUATION, DATA, WINDOW_UPDATE, RST_STREAM, GOAWAY, PING, flow-control windows, stream cancellation, and duplex stream wrappers;
- sends periodic PINGs and fails active streams if the connection fails;
- applies connection, TLS, stream-open, token-fetch, and keep-alive timeouts.

The Android reference implementation in `app/src/main/java/com/vauth/foxyvpn/vpn/upstream/H2UpstreamSession.kt` was inspected. The Windows CONNECT request follows that contract: `:method = CONNECT`, `:authority = host:port`, and `proxy-authorization: Bearer …`; it deliberately does not add `:scheme` or `:path` to a classic CONNECT request.

`Http2ConnectHeaderEncoder` emits the Bearer authorization value as an HPACK **never-indexed** literal. HPACK response header decoding is provided by NuGet `hpack` 1.5.1; see `windows/THIRD_PARTY_NOTICES.md`.

### 2. Guardian proxy-pass provider

`GuardianProxyPassProvider` obtains a fresh Firefox Accounts access token through the existing auth service, requests the Guardian proxy pass, validates it before use, caches it only until expiry (or a short fallback interval), and avoids logging the token itself.

Guardian HTTP 406 still reports a specific limitation: the Android Fastly anti-bot challenge solver has not yet been ported to Windows.

### 3. Explicit proxy-only mode

`WindowsProxyTunnelSession` assembles the HTTP/2 CONNECT provider and `WindowsSocks5ProxyServer`. Home can start or stop it if **Proxy-only mode** is explicitly enabled in Settings and the user has signed in. It binds only to loopback (`127.0.0.1` or `::1`). Applications must be configured individually to use the local SOCKS5 endpoint.

The session explicitly refuses configured upstream-proxy chaining rather than silently ignoring that setting. SOCKS5 UDP ASSOCIATE is still rejected. This mode does not install Windows routes and does not redirect system traffic.

### 4. Safety and lifecycle adjustments

- HTTP/2 request authorization is never-indexed in HPACK.
- The local unauthenticated SOCKS5 server cannot bind to a LAN interface.
- SOCKS5 does not fall back to direct destination sockets if the upstream is missing or fails.
- RST_STREAM and GOAWAY paths mark rejected streams unwritable and pulse flow-control waiters.
- Startup checks for a peer-closed connection around the initial SETTINGS handoff so startup does not intentionally mark a known-closed session as connected.
- `WindowsTunnelEngine` remains separate and fails closed when no real system TUN runtime is registered.

## Static verification performed in this environment

- Parsed **10/10** XAML, project, and related XML files successfully.
- Found **0** missing XAML event-handler references and **0** duplicate XAML names in the Windows UI.
- Checked **37** C# source files and found **0** gross delimiter-balance issues in a lightweight static source check.
- Confirmed the new transport, proxy session, Guardian pass provider, and SOCKS5 server source files are present.
- Added a unit test source for HPACK CONNECT header semantics, never-indexed authorization, and header-injection rejection.

These checks are not compilation or protocol conformance tests. The environment does not have `dotnet`, MSBuild, or a C# compiler, and package restoration / real Guardian access were not available. The new H2 code and tests therefore **have not been compiled, executed, or verified against the live VPN edge**.

## Important limitations still open

The Windows project is **not yet a complete VPN**. Specifically:

- No Wintun/TUN adapter is opened, and no IP packets are read from or written to a virtual adapter.
- No system routes are installed, monitored, rolled back, or restored on failure/disconnect.
- No full-system DNS handling, DoH relay, UDP forwarding, or SOCKS5 UDP ASSOCIATE exists.
- Upstream proxy chaining, split tunneling, leak protection / kill-switch behavior, reconnect/failover, and in-session Guardian token-expiry recovery are not ported.
- HTTP/2 protocol source needs a real build, independent code review, conformance/integration testing, and a live test against a permitted compatible endpoint before production use.
- Firefox/Fastly anti-bot challenge handling for HTTP 406 remains unported.
- No EXE, MSIX, MSI, or installer is claimed as generated or tested in this environment.

## Build and test on Windows

From the repository root, with Visual Studio 2022/.NET 8 installed:

```powershell
dotnet restore .\windows\FoxyVPN.sln
dotnet build .\windows\FoxyVPN.sln --configuration Release
dotnet test .\windows\FoxyVPN.sln --configuration Release
```

First resolve every compile/test failure and inspect the HTTP/2 transport before implementing system TUN routing. A local fake HTTP/2 TLS peer should exercise initial SETTINGS, CONNECT accepted/rejected, fragmented HEADERS, DATA flow control, PING ACK, GOAWAY, RST_STREAM, cancellation, and peer close. Then test end-to-end SOCKS5 TCP through a compatible test endpoint.

## Next implementation phase priority

1. Build and test Phase 1.5 on Windows CI; fix all real compiler/test failures and validate H2 protocol behavior before relying on the transport.
2. Choose and license-review a Windows TUN backend (for example, Wintun plus an explicitly packaged/installed signed driver, or a maintained compatible packet-forwarding component).
3. Implement a packet-forwarding data plane that routes TCP flows through the existing CONNECT provider and handles UDP/DNS separately; don't advertise a full VPN while only TCP CONNECT works.
4. Implement transactional route/DNS setup, prior-state capture, rollback on every startup failure, disconnect restoration, IPv4/IPv6 handling, and leak/kill-switch tests before enabling full-system mode.
5. Port upstream proxy chaining, token refresh/reconnect, split tunneling, and anti-bot handling as needed; document any non-portable Android behavior.
6. Produce a signed/traceable Windows build and installer only after integration and security testing pass.
