# FoxyVPN Windows — Phase 1.7 Full-Tunnel Orchestration Report

## Scope

This phase follows the user's stated workflow: finish coding and integration first, then run the comprehensive test/acceptance suite at the end. Lightweight static checks are used only to catch structural issues while coding; no managed/native build or live VPN result is claimed.

## Implemented in source

### Ordered full-system data plane

`WindowsSystemTunnelRuntime` is registered in `AppServices` through `WindowsTunnelEngine`. Startup ordering is:

1. Require an authenticated Firefox Account session, an explicitly selected server, an elevated process and the bundled native runtime.
2. Obtain the current Guardian proxy pass and establish TLS with certificate validation plus mandatory ALPN `h2` against the selected edge (or configured edge IP while preserving TLS host/SNI).
3. Establish the local loopback-only SOCKS5 TCP/DNS relay. If configured, chain through HTTP CONNECT or SOCKS5 upstream proxy with optional credentials; failure does not downgrade to a direct connection.
4. Start HEV `2.17.1` through its C API, allowing HEV to create the configured Wintun interface, and wait for both IPv4 and IPv6 interface indices plus stable interface identity.
5. Install a reversible system route/DNS transaction only after forwarding layers are alive. `Connected` is reported only while the registered runtime confirms the HTTP/2, HEV/Wintun and route transaction layers are all active.

Disconnect and startup-failure cleanup reverse the dependencies. If route rollback cannot be verified, the runtime enters a safety hold, leaves the forwarding process alive when possible, keeps the recovery journal and does not remove the persistent public-egress guard.

### Route/DNS transaction and recovery

`WindowsSystemRouteTransaction` now:

- Uses a named cross-process semaphore in addition to the in-process async semaphore, preventing two app processes from concurrently applying routes/firewall rules.
- Confirms the transport's physical local address maps to an active uplink and verifies that Windows Defender Firewall profiles are enabled before changing routes.
- Adds a host route to the physical gateway and an explicit host bypass for the actual HTTP/2 edge or upstream proxy endpoint before installing broader routes.
- Adds IPv4 and IPv6 split-default routes on Wintun without replacing the user's pre-existing default routes, sets tunnel interface metrics to a controlled value and journals prior `AutomaticMetric`/`InterfaceMetric` values.
- Adds a unique root NRPT rule pointing to HEV's fake-DNS address `198.18.0.2`; it refuses to take over a conflicting root DNS policy.
- Journals route prefix, next hop, route metric, stable adapter ID, interface metrics, DNS-rule comment/intent and every persistent firewall rule name under `%ProgramData%\\FoxyVPN\\runtime` before each mutation.
- Verifies the exact interface identity and route attributes before deleting routes. It does not flush the route table. If current interface identity no longer matches, it refuses uncertain deletion and retains the journal/guard.
- Installs persistent outbound Windows Firewall block rules on active non-loopback interfaces other than FoxyVPN's own Wintun interface. Public IPv4/IPv6 ranges are blocked on physical interfaces. The active upstream socket is the narrow exception: its exact remote TCP port is left reachable; UDP, ICMP and other TCP ports to that same address are blocked. Private, loopback, link-local and specified reserved ranges remain excluded for local-network operation.
- Removes firewall rules last, only after route deletion and restoration of prior interface metrics have been verified. If recovery is incomplete, it keeps the firewall rule names in the journal so an elevated restart can retry.

`WindowsKillSwitchAddressRangeBuilder` is a separate pure helper computing the IPv4/IPv6 CIDR complement for block rules. xUnit source now covers public destinations, private/link-local exceptions, the exact upstream address exception and CIDR non-overlap.

### Runtime health and UI

- `WindowsTunnelEngine.State` now reflects a missing runtime confirmation as `Faulted`, rather than exposing a stale `Connected` state after the native worker/transport drops.
- `HomeView` refreshes the visible runtime badge every two seconds while loaded, and stops this timer when unloaded.
- Diagnostic ZIP summary no longer describes the proxy as unimplemented; it reports current preflight, connection and proxy-only state while omitting tokens, passwords and saved endpoint hostnames from the summary.
- Prior feature set remains in source: 2027 motion treatment, Reduce Motion, Windows auto-start/start minimized, favorites, TCP reachability diagnostic, timed server catalog refresh, diagnostics ZIP, themes, account/server/settings UI and explicit proxy-only mode.

## Protocol and known limitations

- The current Guardian edge contract exposed by Android is HTTP/2 CONNECT for TCP streams. General UDP and QUIC are intentionally dropped; the DNS-only SOCKS5 UDP relay tries DoH over a validated, authenticated CONNECT stream and then DNS-over-TCP through that same upstream. HEV fake-IP mapping is used for system DNS. No direct Internet fallback is implemented.
- This is source integration, not proof of actual traffic forwarding. The handwritten HTTP/2 engine, HEV Windows ABI, native DLL dependency bundle, Wintun creation, NRPT behavior, firewall rule syntax/ordering, DNS mapping and route rollback still require final Windows CI and real endpoint acceptance.
- Windows Firewall block rules cannot enforce a kill switch if a user or external process disables the firewall service/profiles *after* activation. Phase 1.7 checks all profiles before mutation, but runtime firewall-toggle and service-stop behavior is still part of the required final security test. It is not represented as already solved.
- Anti-bot challenge parity (Fastly/Guardian/Firefox Accounts HTTP 406 challenge) and Windows per-application split tunneling remain incomplete. Those are explicit next coding priorities where a safe native Windows equivalent exists.
- General UDP/QUIC cannot be ported without compatible server support. Do not disguise drops as success.

## Source-level verification done while coding

- Wrote targeted tests for IPv4/IPv6 kill-switch CIDR construction and for runtime loss presenting as `Faulted`.
- Static XML/XAML, project-reference, event-handler and source-delimiter checks are performed in the authoring environment before packaging.

## Final verification deliberately deferred

Per the user's preference, comprehensive tests are reserved until code completion. No `dotnet restore`, `dotnet build`, `dotnet test`, GitHub Actions run, native library invocation, live Guardian/edge session, real Windows route mutation, DNS/IPv6 leak probe, portable build or installer creation has been executed here. This environment has no .NET SDK/MSBuild. The final acceptance criteria are **not met yet**.

## Phase 1.8 next coding priorities

1. Continue code parity before the final test run: inspect and port the Android anti-bot challenge path; determine a safe Windows alternative for per-application split tunneling; and add network-change/sleep-wake recovery orchestration that never removes the kill switch before safe route cleanup.
2. Review transport lifetime/token refresh and server failover semantics against the Android code; do not add fake UDP support.
3. Once coding is declared complete, run the full Windows CI, managed unit tests, native HEV build/load/start/stop, proxy protocol tests, and a controlled Windows 10/11 network test matrix for IPv4, IPv6, DNS, crash recovery, changed uplink, and firewall failure. Fix everything before emitting an installer.
