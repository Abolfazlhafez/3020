# FoxyVPN Windows — Phase 1.4 data-plane foundation report

Date: 2026-10-10  
Windows source version: 1.4.0  
Target: .NET 8 / WPF / Windows 10 and Windows 11

## Implemented in source

- Added `WindowsSocks5ProxyServer`, a loopback-only SOCKS5 RFC 1928 CONNECT listener behind the `IUpstreamStreamProvider` and `IUpstreamDuplexStream` contracts.
- The listener refuses to start if the injected upstream is not connected. It never opens a direct socket to the requested destination, and the desktop app does not register or start it while the actual HTTP/2 CONNECT implementation is absent.
- Supports SOCKS5 no-auth negotiation for clients on the local machine, hostname/IPv4/IPv6 destination parsing, TCP CONNECT, max-concurrent-session limiting, handshake and upstream-open timeouts, cancellation, bidirectional stream relay, and TCP half-close draining.
- Restricts binding to loopback. Filters loopback, private, link-local, unique-local, multicast and selected local hostname destinations. The policy is defence in depth, not a substitute for a complete egress policy.
- Returns command-not-supported for UDP ASSOCIATE because Windows UDP/DNS relay support is not implemented.
- Added test source for non-loopback bind rejection, refusing to start without upstream, successful local fake-upstream data relay and client half-close, loopback target rejection, and UDP ASSOCIATE rejection.
- Updated current Windows/installer/CI version metadata to 1.4.0 and documented the new foundation.

## Validation performed

- Static XML/XAML checks are rerun as part of this package creation; this checks well-formedness only and does not compile WPF or C#.
- Source review confirms the listener’s forwarding path calls only the injected provider; it contains no `TcpClient.ConnectAsync` for the requested destination. The only actual destination socket in the source test is inside the fake test provider.
- ZIP archive integrity and XAML event-reference checks are run before release of these source bundles.
- `dotnet`, MSBuild, `csc` and `mcs` are not installed in the authoring environment. Therefore restore, compile, xUnit execution, Windows launch, listener runtime behavior, and installer generation have **not** been verified. Added tests are test source, not passing test results.

## Critical remaining blockers

1. Real HTTP/2 transport: port Android `H2UpstreamSession.kt` to TLS with ALPN `h2`, multiplexed CONNECT streams, `proxy-authorization: Bearer`, correct TLS hostname validation, flow control/backpressure, timeouts, GOAWAY, cancellation, proxy-pass renewal, and upstream-proxy chaining. Then implement a real `IUpstreamStreamProvider`.
2. UDP/DNS parity: port SOCKS5 UDP ASSOCIATE, DNS relay and DoH behavior, with privacy and leak tests.
3. Windows TUN packet I/O: select and validate a maintained/licensed/signed Windows implementation and its redistributable packaging model.
4. Route and DNS transaction: install routes, set DNS and rollback on every failed start, crash/restart, disconnect and upgrade path. Never leave a broken default route behind.
5. Register the runtime only after the entire pipeline can pass real traffic and failure/leak tests. `Connected` must continue to require a runtime confirmation and health check.
6. Run Windows CI `dotnet restore`, Release build and xUnit tests, fix all actual failures, and attach a real Windows build + installer artifact before claiming release readiness.

## Status summary

Phase 1.4 adds a useful, testable SOCKS5 protocol foundation. It is not integrated with the application, because no genuine HTTP/2 stream provider exists. This is **not a working Windows VPN**, does not route OS traffic, and is not an executable release. The original Android project remains included and unchanged as the reference implementation.
