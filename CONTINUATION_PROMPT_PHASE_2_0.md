# Continuation prompt — FoxyVPN Phase 2.0 → Phase 2.1

Continue from `FoxyVPN-Android-and-Windows-Phase2.0.zip` and `FoxyVPN-Windows-Source-Phase2.0.zip`. Work directly on these files; do not recreate the project. Preserve the original Android source, existing WPF UI and all prior code.

## User's explicit workflow requirement

Finish coding and integration before starting comprehensive final tests. Do **not** run `dotnet test`, the final integration test matrix, native acceptance tests or live leak tests while coding remains in progress. Do not imply tests passed. Lightweight static inspections and package integrity checks are allowed. Keep progressing through the source in manageable increments and record what changed.

## Current version and important new source

- Current app version: `2.0.0` in `windows/FoxyVPN.Windows/FoxyVPN.Windows.csproj`, `windows/installer/FoxyVPN.iss`, and `.github/workflows/windows-build.yml`.
- Phase 2.0 adds `windows/FoxyVPN.Tests/UpstreamProxyHandshakeTests.cs` and internal test seams in `WindowsHttp2ConnectSession` for HTTP CONNECT and SOCKS5 upstream negotiation.
- SOCKS5 authentication requires both username/password (1–255 UTF-8 bytes each); HTTP Basic fields have a 1024-byte UTF-8 limit; password-only upstream configuration is rejected before opening TCP. Credential byte arrays are cleared in `finally` blocks.
- `TunnelReadinessService` reports incomplete upstream credentials before an attempted connection.
- Tests were authored but deliberately not executed. .NET SDK/MSBuild is unavailable in the authoring environment.

## Coding checklist before final testing

1. Review `WindowsHttp2ConnectSession` for HTTP/2 RFC conformance, cancellation races, stream cleanup, flow-control invariants, GOAWAY handling, header decoder limits, and concurrent stream teardown. Fix source issues; do not run the test suite yet.
2. Review upstream HTTP CONNECT and SOCKS5 chain protocol and credential handling for all errors/cancellation cases. Ensure proxy failures never cause a direct TCP fallback. Add deterministic test source for any new bug found.
3. Review SOCKS5 local server and DNS-only UDP relay: bind and client endpoint scoping, UDP ASSOCIATE lifetime, DNS message lengths/IDs, DoH response parsing, DNS-over-TCP fallback, limits and cancellation. General UDP/QUIC must remain documented as unsupported unless a real upstream datagram protocol exists.
4. Review HEV/Wintun native build, calling convention, exported function names, Windows runtime loader dependencies and CI output filenames. Update scripts/source when justified by code evidence; do not claim the native library built until CI has run.
5. Review route transaction rollback/journalling, gateway/edge bypass, interface identity, DNS NRPT and Windows Firewall egress blocking. Keep a fail-closed recovery state if route/DNS rollback cannot be verified. Do not silently remove the kill-switch while the route table may still point at the tunnel.
6. Audit startup/auto-start and graceful close when an active route journal remains. If safe cleanup fails, preserve the journal and keep the forwarding runtime alive when required for safety.
7. Per-process split tunneling remains unimplemented. Do not add a fake switch; either implement a genuine Windows mechanism with robust leak protection and tests after coding is complete, or keep the limitation explicit.
8. Review remaining feature gaps and update the UI, reports, README, `windows/MIGRATION_REPORT.md`, package manifest and installer/CI metadata consistently.
9. Only when the coding checklist is complete, move to final testing. Then execute Windows CI: restore/build/test, native HEV build and exported-symbol/ABI checks, portable publish and Inno Setup. Capture the actual logs and fix/re-run every failure.

## Required final validation after coding is complete

- Restore/build/test the Visual Studio solution on a Windows runner.
- Validate HEV/Wintun load and ABI, real Firefox sign-in/2FA/challenge, live h2 ALPN + CONNECT, optional HTTP and SOCKS5 upstream proxy modes, SOCKS5 TCP relay, DNS/DoH fallback, and proxy pass expiry/renewal.
- Test full-system IPv4/IPv6 routes, DNS path, edge bypass, Firewall disabled, route journal rollback, process crash, connect cancellation, Wi-Fi handover, sleep/resume, non-admin launch, uninstall and recovery after reboot.
- Verify no direct fallback or DNS/IPv4/IPv6 leakage; explicitly confirm that general UDP/QUIC is rejected unless a compatible edge protocol is implemented.
- Build and install-test the portable x64 app and Inno Setup installer. Record exact commands/results; no claim of working VPN or production readiness without evidence.

## Deliverables

Return the complete Android+Windows ZIP, Windows-only ZIP, updated migration report, package manifest and next continuation prompt. Explicitly state which build/tests ran, which did not, what remains unsupported, and whether an actual installer was generated.
