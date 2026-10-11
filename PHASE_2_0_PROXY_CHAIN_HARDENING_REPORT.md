# FoxyVPN Phase 2.0 — upstream proxy-chain hardening

## What changed in this coding increment

- Bumped current Windows app metadata to `2.0.0` in the WPF project, default Inno Setup version and Windows CI installer invocation.
- Exposed the existing HTTP CONNECT and SOCKS5 upstream proxy handshake methods as `internal` seams so the production implementation can be exercised using deterministic scripted streams. `Properties/AssemblyInfo.cs` grants `InternalsVisibleTo` only to the test assembly (`FoxyVPN.Tests`).
- Added `UpstreamProxyHandshakeTests.cs` for HTTP CONNECT Basic auth header generation, strict HTTP 200 acceptance, malformed/407/403 status rejection, bracketed IPv6 authority formatting, SOCKS5 no-auth negotiation, RFC 1929 username/password negotiation, unacceptable authentication-method selection, rejected SOCKS5 CONNECT, partial credentials and SOCKS5 credential lengths exceeding the protocol limit.
- Added preflight validation before a TCP socket is opened. SOCKS5 credentials must either both be absent or both be present; when present, username and password must each be 1–255 UTF-8 bytes. HTTP Basic fields are bounded at 1024 UTF-8 bytes, and password-only configuration is rejected.
- Changed temporary upstream credential byte buffers to be cleared from `finally`, including cancellation/I/O failure paths.
- Added a readiness test to ensure incomplete SOCKS5 upstream credentials produce a blocker with a useful explanation.
- Updated the README, migration report, source README, CI artifact list and continuation prompt for version consistency.

## Static checks performed in this increment

- Source was edited in the existing Phase 1.9 project, not recreated from an empty template.
- `.NET` build and test commands were deliberately not run: the user requested coding and integration first, with comprehensive testing only at the end, and this environment has no `dotnet` or MSBuild executable.
- Static XML/XAML/project parsing, event-handler reference checks, C# delimiter inspection and ZIP integrity checks are run during packaging; those checks do not prove C# compilation or protocol interoperability.

## Still not verified

- `dotnet restore`, `dotnet build`, and `dotnet test` have not been executed.
- The native HEV library build, Wintun loading, DLL ABI/symbol behavior, installer build, and launch on Windows 10/11 have not been executed.
- HTTP/2 behavior against the real Guardian edge, upstream HTTP CONNECT/SOCKS5 servers, live Firefox account flow, DNS-over-HTTPS/fallback, route rollback, kill-switch and IPv4/IPv6/DNS leak behavior have not been exercised.
- The edge protocol in the Android reference is TCP CONNECT based. General UDP/QUIC remains unsupported except for the source-authored DNS-only UDP relay; that path is not live-verified.
- Per-application split tunneling remains unavailable and is explicitly documented as not implemented.

## Current implementation status

| Area | Source status | Runtime evidence |
|---|---|---|
| Native WPF desktop and 2027 motion UI | Implemented in source | Not launched here |
| Firefox Accounts / Guardian API | Implemented in source, interactive challenge user-mediated | Not live-verified |
| HTTP/2 CONNECT + local SOCKS5 | Implemented in source | Not interoperably verified |
| Optional upstream HTTP CONNECT / SOCKS5 with auth | Implemented and test seam added | New tests authored, not run |
| HEV / Wintun / route-DNS-firewall orchestration | Integrated in source and CI recipe | Not built or live-verified |
| Final test and installer acceptance | Deferred until coding is complete | Not run / not produced |
