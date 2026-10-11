# FoxyVPN Windows — Phase 1.3 Readiness and lifecycle foundation

## Implemented in source

- Added a structured tunnel readiness report covering Windows baseline, Firefox Account session, selected endpoint, side-by-side `wintun.dll` presence, HTTP/2 CONNECT, SOCKS5 relay integration, and route/DNS lifecycle.
- The Wintun preflight only checks for the side-by-side runtime file and never loads native code, installs drivers, alters routes, or claims the adapter is operational.
- Replaced the permanent one-method tunnel stub with a serialized, cancellation-aware connect/disconnect lifecycle (`Disconnected`, `Starting`, `Connected`, `Stopping`, `Faulted`) behind `IWindowsTunnelRuntime`.
- The engine reports `Connected` only after an injected runtime explicitly reports `IsConnected == true`; when no runtime is registered, it fails closed and states that no connection or route change was started.
- The Home action now opens the readiness report and logs each passed/blocked item rather than presenting a connection-looking success UI.
- Added unit test source for readiness blockers, missing runtime fail-closed behavior, confirmed connected state, runtime startup failure, and disconnect transitions.
- Version metadata is now 1.3.0 in the Windows project, desktop shell, Inno Setup default, and CI installer invocation.

## Static validation status

- XML/XAML static parse: 30/30 files passed. XAML event handler scan: 29/29 references matched a code-behind method. Project-local explicit paths: passed.
- .NET restore/build/test: not run; this environment does not have `dotnet` or MSBuild.
- Windows UI run, Wintun installation/runtime, real HTTP/2 CONNECT interoperability, end-to-end traffic, routing rollback, and leak-proof disconnect: not run / not implemented in this phase.

## Known critical blockers

The following checks intentionally remain blocked: HTTP/2 CONNECT upstream stream multiplexing and lifecycle, SOCKS5 TCP/UDP/DNS integration, Windows TUN packet I/O, route and DNS transaction/rollback, proxy-pass renewal during an active tunnel, full exit verification, and split-tunnel parity. Phase 1.3 is a tested-by-design foundation in source, not a working Windows VPN release. No fake connected state, simulated traffic, or system route changes were added.

## Next phase priority

1. Build on a Windows runner and fix any compile/XAML/test errors before feature work.
2. Select and validate a supported Windows TUN implementation and packaging/license strategy.
3. Port the Android HTTP/2 CONNECT session to Windows with protocol conformance/integration tests before wiring packets to it.
4. Port local SOCKS5 TCP/UDP and DNS relay, then implement route transaction and rollback.
5. Only after those parts work, wire a runtime into `WindowsTunnelEngine` and run real traffic/leak/restore tests.
