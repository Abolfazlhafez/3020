# Continuation prompt — FoxyVPN Windows Phase 1.6 → Phase 1.7

Continue from the complete archive `FoxyVPN-Android-and-Windows-Phase1.6.zip`. Preserve the original Android app and all existing WPF work: the 2027 motion-led design, Reduce Motion, Windows auto-start, account/API code, Guardian proxy-pass, HTTP/2 CONNECT source, SOCKS5 proxy-only mode, native HEV bridge and DNS-only UDP relay. Do not recreate the project from scratch.

## Current Phase 1.6 additions

- `WindowsHevConfigWriter.cs`: writes secret-free HEV YAML aligned with Android defaults (`10.8.0.2`, MTU 8500, fake DNS `198.18.0.2`, map `100.64.0.0/10`, loopback SOCKS5 endpoint).
- `WindowsHevNativeCore.cs`: lifecycle-aware HEV 2.17.1 C-API bridge using `hev_socks5_tunnel_main_from_file` / `hev_socks5_tunnel_quit`; it is not registered as a system tunnel.
- `.github/workflows/windows-build.yml`: CI source-builds pinned HEV, checks architecture/symbols, verifies the official Wintun archive hash and Authenticode publisher, restores/builds/tests/publishes x64 and makes an Inno Setup installer.
- `WindowsSocks5UdpDnsRelay.cs`: DNS-only SOCKS5 UDP relay. DNS is attempted via DoH inside authenticated CONNECT with nested TLS validation, then via DNS-over-TCP through the same upstream. Non-DNS UDP/QUIC is dropped. No direct-network fallback exists.
- SOCKS5 UDP ASSOCIATE source validates loopback peer + advertised UDP source port. The port in the ASSOCIATE request represents the client UDP source port, not remote DNS port 53.
- `WindowsProxyTunnelSession.UpstreamRemoteEndPoint` exposes the actual connected edge endpoint for a future physical-interface bypass route.
- xUnit source exists for HEV configuration and DNS-only UDP fallback/drop behavior, but **has not been executed**. This environment lacks `dotnet`/MSBuild.

## Priority 1 — run real Windows CI before adding more code

1. Run `dotnet restore windows/FoxyVPN.sln`.
2. Run `dotnet build windows/FoxyVPN.sln --configuration Release --no-restore` and fix every compiler error.
3. Run `dotnet test windows/FoxyVPN.sln --configuration Release --no-build` and fix every failure or flake.
4. Run `.github/workflows/windows-build.yml`; validate that MSYS2 produces a genuine x64 PE DLL (not an ELF `.so` merely renamed), that all imported runtime DLLs are bundled, that both HEV C symbols export, and the Wintun archive hash/signature/publisher checks pass.
5. Verify the generated native library loads via `NativeLibrary.Load`, starts HEV, creates the named Wintun interface, and stops without leaking the worker or native module. Capture real CI/test logs before claiming success.

## Priority 2 — verify the transport and DNS relay end to end

1. Validate the hand-written HTTP/2 session against a local mock HTTP/2 peer, then an authorized compatible FoxyVPN edge. Check SETTINGS/ALPN, CONNECT status, stream flow control, PING, GOAWAY, stream reset, half-close, timeouts, token expiry and cancellation.
2. Validate `UDP ASSOCIATE` against a standards-compliant SOCKS5 client. Check wildcard/specified loopback client address, advertised UDP port binding, control-connection lifetime, datagram source validation, fragment rejection and unsupported-command behavior.
3. Validate DNS DoH inside the authenticated CONNECT: TLS certificate and IP SAN verification, response Content-Type/status/length limits, DNS transaction ID and QR checks, 4096-byte maximum, in-flight cap, timeout, DNS-over-TCP fallback through the same upstream, and the absence of direct sockets.
4. Verify the current edge permits CONNECT to resolver port 443 and/or 53. If it does not, report DNS blocked rather than falling back to public direct DNS. General UDP/QUIC must remain marked unsupported while the edge only provides TCP CONNECT.

## Priority 3 — safely orchestrate the actual system VPN

Build a separate `IWindowsTunnelRuntime` only after the above gates pass. Required startup order:

1. Obtain a valid Guardian proxy pass and establish authenticated TLS + HTTP/2 to the selected edge; retain the validated remote endpoint returned by `UpstreamRemoteEndPoint`.
2. Start the loopback SOCKS5 session and confirm it is ready.
3. Start the HEV native core pointing at the exact SOCKS port; verify its Wintun interface and packet path.
4. In an elevated, independently testable route/DNS transaction, snapshot only the system state that is about to change, add a route for the actual edge IP through the original physical gateway/interface, and only then apply required tunnel route/DNS settings. Route installation must be the last stage, after forwarding is live.
5. On disconnect/startup failure, remove only routes/settings FoxyVPN created, in reverse order, restore prior DNS/metric values, and stop HEV → SOCKS → HTTP/2 in reverse startup order. Never run `route /f`, never overwrite unrelated routes, and never fall back to direct networking. If rollback fails, keep a visible fault state and do not claim disconnected/secure.
6. Do not enable full-system mode or set `Connected` until a real TCP+DNS path and external IPv4/IPv6/DNS leak probes succeed.

## Required Windows 10/11 test matrix

- Clean startup and cancellation at each partial startup stage.
- Guardian token failure/expiry, edge handshake rejection, H2 stream reset/GOAWAY and loss of network.
- DNS DoH success, DNS-over-TCP fallback, resolver unavailable, malformed/truncated packet, high concurrency, non-DNS UDP drop.
- IPv4 and IPv6 destination traffic, MTU behavior, application traffic through the TUN, and no direct DNS/IPv6 leak.
- Physical interface/gateway changes, Wi-Fi roam, sleep/wake, adapter removal, process crash and failed route rollback.
- Confirm the edge endpoint remains reachable outside the tunnel; verify no default route is applied before a working data plane exists.

## Non-negotiable constraints

- Do not invent build or live-test results. Do not ship unverified native DLLs.
- Never update routes during preflight; never flush the routing table.
- Never expose tokens in logs or YAML. Keep TLS certificate validation on; no insecure callback.
- Do not claim general UDP/QUIC until the server actually supports that transport.
- Keep the system runtime unregistered and fail-closed until route/DNS transaction and leak tests pass.
- Preserve the Android app as reference. Update `windows/MIGRATION_REPORT.md`, `windows/README_WINDOWS.md`, third-party notices, CI artifacts, feature checklist and version metadata.
- Deliver the next complete Android+Windows ZIP, Windows-only source ZIP, accurate phase report, and next continuation prompt.
