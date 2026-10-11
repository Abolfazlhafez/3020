# Continuation prompt — FoxyVPN Phase 1.9 → Phase 2.0

Continue directly from `FoxyVPN-Android-and-Windows-Phase1.9.zip` / `FoxyVPN-Windows-Source-Phase1.9.zip`. Do not recreate the app. Preserve the Android source and all WPF design, settings, APIs, networking, route transaction, HEV/Wintun and logs.

## User's process requirement

The user explicitly wants all coding/integration finished first, then the comprehensive final test phase. Do not run `dotnet test`, final integration tests or claim success while coding is still underway. Lightweight static syntax/structure inspections and ZIP integrity checks are allowed. Keep a detailed checklist and continue making source changes rather than stopping at a plan. At the final stage, clearly distinguish authored tests from executed results.

## Current architecture and Phase 1.9 state

- Native Windows UI: C# / .NET 8 / WPF, version 1.9.0; motion-led 2027 design, reduced-motion option, themes, auto-start, connect-on-launch.
- API: Firefox Accounts auth/token refresh and Guardian endpoints; DPAPI-protected tokens/settings and redacted logs.
- VPN source chain: custom TLS+ALPN-h2 HTTP/2 CONNECT → local loopback SOCKS5 → HEV 2.17.1/Wintun → journalled IPv4/IPv6 routing, NRPT DNS and Firewall egress guard. Proxy-only mode uses explicit local proxy configuration. General UDP/QUIC remains unsupported by the present CONNECT-only server protocol; only DNS UDP relay is authored and not live-verified.
- New Phase 1.9: WebView2 user-mediated challenge window for approved Firefox/Guardian HTTPS origins, strict cookie import validation, retry-once handling during sign-in, 2FA, access-token refresh, Guardian account refresh and startup/connect flows; stop the background renewal worker on user-verification-required. Connect-on-launch now waits for safe recovery. Per-app split tunneling is still not implemented and is documented as unavailable.
- Reports: `PHASE_1_9_INTERACTIVE_CHALLENGE_AND_STARTUP_REPORT.md`, `windows/MIGRATION_REPORT.md`, `windows/README_WINDOWS.md`; CI workflow: `.github/workflows/windows-build.yml`.

## Coding still to complete before final tests

1. Review every service call path for `FirefoxChallengeRequiredException` and `GuardianChallengeRequiredException`; ensure only one user-mediated retry and no duplicate/recursive retries. Keep challenges explicitly user-mediated; do not port code that fabricates bot-detection metrics or automatically solves/bypasses anti-bot challenges.
2. Review cookie domain scope, expiry, persistence, and challenge-origin allow-list. Keep the separate WebView2 user-data directory by service origin. Never log passwords, tokens or cookie values.
3. Review cancel/dispose paths for HTTP/2 stream/session, SOCKS5 server, HEV worker, route transaction, Firewall guard and background refresh. Ensure cancellation doesn't release a kill switch before routes/DNS are restored.
4. Review upstream-proxy chain modes (HTTP CONNECT and SOCKS5 with authentication) and timeout/cancellation cleanup. Do not fall back to direct internet connections.
5. Review startup auto-connect semantics, elevation requirement, pending-recovery journal logic and connect-on-launch with a saved token/endpoint in both proxy-only and full-system mode.
6. Audit native HEV/Wintun build and packaging workflow, architecture, native loader names, MSYS2 dependencies, licensing and published output paths. Fix scripts/source based on code inspection, without claiming CI success.
7. Inspect UI states and settings for every unavailable feature. Per-app split tunneling is not implemented: either leave it labelled unavailable, or design a genuine safe per-process routing solution; never add a fake toggle. Keep global VPN status false unless each runtime layer confirms healthy.
8. Ensure README, migration report, package manifest and installer/workflow metadata all consistently say the current version, and list any known blockers.

## Final comprehensive validation (only after coding is complete)

On Windows 10 and 11 / Windows CI run `dotnet restore windows/FoxyVPN.sln`, `dotnet build windows/FoxyVPN.sln --configuration Release`, `dotnet test windows/FoxyVPN.sln --configuration Release`, native HEV build and symbol/ABI checks. Fix all failures, repeat until clean, and record exact command results. Then test real Firefox sign-in / 2FA / token expiry, WebView2 manual challenge, live edge TLS+ALPN/HTTP2 CONNECT, SOCKS5 TCP, DNS/DoH fallback, IPv4/IPv6 routing, DNS and traffic leak behavior, route journal rollback, kill-switch, app crashes, Wi-Fi changes, sleep/wake, Firewall disabled, non-admin startup, and safe uninstall. Build and install-test portable x64 and Inno Setup output. Test UDP/QUIC explicitly and report it unsupported unless the upstream supports it. No test is complete until it was executed and its result recorded.

## Deliverables

Deliver a full Android+Windows ZIP, Windows-only ZIP, current migration report, current package manifest, and next continuation prompt. Explicitly state whether build/tests/native VPN have actually run; do not fabricate the installer binary or report “production ready” until all acceptance criteria have evidence.
