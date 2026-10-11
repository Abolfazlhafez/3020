# FoxyVPN Windows — Phase 1.6 HEV Native Core and DNS Relay

**Version:** 1.6.0  
**Status:** Source phase prepared; acceptance criteria are not met.  
**Environment limitation:** This authoring environment has no .NET SDK or MSBuild. C# compilation, xUnit execution, HEV native build/load, real HTTP/2 interoperability and Windows routing behavior were not run or verified.

## What changed in this phase

### 1. Reuse the Android tun2socks engine

- Added `WindowsHevConfigWriter` to generate a secret-free configuration for the pinned HEV socks5 tunnel C API. It uses the Android reference IPv4 tunnel address `10.8.0.2`, MTU 8500, fake DNS address `198.18.0.2`, mapping network `100.64.0.0/10`, and a loopback SOCKS5 endpoint. An IPv6 interface address is configured for Windows but is not yet validated with routes or real traffic.
- Added `WindowsHevNativeCore` as a serialized, lifetime-aware wrapper around `hev_socks5_tunnel_main_from_file` and `hev_socks5_tunnel_quit`. It checks elevation and required app-local native files, asks HEV to own its Windows TUN (`tun_fd = -1`), observes whether the expected interface appears, and avoids unloading a native module while its worker could still be running.
- Added a Windows CI workflow that pins HEV 2.17.1, builds it with MSYS2, checks exports and x64 PE format, downloads the official Wintun 0.14.1 archive, checks its SHA-256 and Authenticode publisher, then bundles the native files beside a self-contained build. **This CI workflow has not been run here.** The binary and all transitive runtime dependencies therefore remain unverified.

### 2. DNS-only SOCKS5 UDP association

- Extended the loopback-only SOCKS5 listener to handle RFC 1928 `UDP ASSOCIATE` while keeping the TCP control channel alive.
- The UDP association validates the advertised source address and, when the client supplies a nonzero source port, accepts datagrams only from that IP/port. The requested association port is correctly treated as the **client UDP source port**, not as the DNS destination port.
- Added `WindowsSocks5UdpDnsRelay`: only UDP destination port 53 is processed. DNS payloads are bounded to 4096 bytes, concurrent work is capped at 32 requests, queries have a 10-second deadline, DoH attempts have a smaller timeout, and DNS transaction ID/response flags are validated before a response is returned.
- DNS-over-HTTPS is attempted by opening a destination `CONNECT` stream through the authenticated HTTP/2 upstream, then validating nested TLS and making an HTTP/1.1 DNS-message request. When DoH fails, DNS-over-TCP is attempted through the same authenticated upstream. There is **no direct Internet socket fallback**.
- Non-DNS UDP, including QUIC, is deliberately dropped. The available FoxyVPN edge transport accepts TCP `CONNECT`; this phase does not pretend that this automatically provides general UDP transport.
- Added deterministic xUnit test source for UDP DNS over a fake upstream (including DoH failure then DNS-over-TCP fallback) and for dropping non-DNS UDP without opening an upstream. These tests were **not executed** in this environment.

### 3. Upstream endpoint metadata

`WindowsHttp2ConnectSession` now exposes the connected remote endpoint and `WindowsProxyTunnelSession` forwards it. The eventual route transaction needs this resolved edge IP to keep a host route through the original physical gateway before any default route is directed into a tunnel.

## Important source and licensing choices

- HEV source is pinned to upstream tag `2.17.1` (MIT): https://github.com/heiher/hev-socks5-tunnel
- The CI workflow fetches Wintun `0.14.1` from the official distribution, verifies archive hash and Authenticode publisher, and preserves the accompanying license notice: https://www.wintun.net/
- Native binaries are deliberately not committed in the source ZIP. The source CI must build and bundle them before a Windows publish can use the HEV runtime.
- See `windows/THIRD_PARTY_NOTICES.md` and `windows/FoxyVPN.Windows/Native/README.md`.

## Checks done here

- Readiness, native lifecycle and SOCKS/DNS source reviewed statically.
- Android-compatible HEV config values have explicit xUnit test source.
- SOCKS5 UDP association test inputs now supply a real loopback client UDP source port.
- XML/XAML and CI workflow syntax, project references, XAML event bindings, ZIP integrity, and removed-file references are checked before packaging.
- `dotnet` / MSBuild are absent, so there is no compile or test result to report. The CI workflow has not run and no EXE/MSIX/installer was generated here.

## What is not complete

The full Windows system tunnel remains intentionally **unregistered and fail-closed**. This phase does not configure system routes or system DNS, apply an endpoint bypass route, start a kill-switch, or prove that all Windows traffic uses the VPN. It does not verify that the native HEV build loads successfully or that the custom HTTP/2 transport interoperates with the real edge.

Remaining blockers:

1. Run Windows CI and fix actual build/test failures; verify the HEV DLL loads and all runtime dependencies are found.
2. Integrate lifecycle in the safe order: authenticated Guardian/HTTP2 connection → loopback SOCKS5 → HEV native core. Stop in reverse order and roll back on partial failures.
3. Implement a separate elevated route/DNS transaction that snapshots prior state, pins the current edge endpoint through the original physical gateway, applies tunnel defaults only after HEV and forwarding are live, and removes only the routes/settings it changed. Never flush the routing table.
4. Define fail-closed DNS and IPv4/IPv6 leak protection. Any rollback failure must remain visible and must not fall back to direct networking.
5. Run end-to-end Windows 10/11 tests for real HTTP/2 CONNECT, DNS-over-HTTPS, DNS-over-TCP fallback, IPv4/IPv6, application traffic, adapter/edge failure, sleep/wake, reconnect, and route rollback.

**Acceptance result: incomplete.** Proxy-only DNS-relay source and a native tunnel bridge exist in the tree, but neither has been built or tested in this environment, and system-wide VPN mode must not report Connected.
