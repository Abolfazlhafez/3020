# Continuation prompt — FoxyVPN Phase 2.1 → Phase 2.2

Continue from `FoxyVPN-Android-and-Windows-Phase2.1.zip` (full Android + Windows) or `FoxyVPN-Windows-Source-Phase2.1.zip`. Work directly on the existing source; do not recreate the project, replace the Android reference app, or discard the Phase 1.x/2.0 work. The current Windows application version is `2.1.0`.

## Mandatory owner workflow

Finish coding/integration before comprehensive final tests. Do not execute `dotnet test`, native acceptance tests, or live leak/integration testing until the coding checklist is honestly complete. Static code review and package/XML/YAML integrity inspection are allowed. Never claim a passing build, connected VPN, no-leak behavior, or generated installer without actual evidence.

## Phase 2.1 changes already made

- HTTP/2 CONNECT stream writes now remain legal after remote `END_STREAM`, while local `END_STREAM` still blocks writes; HTTP/2's two directions are independently half-closed.
- The runtime contract has an optional `IsSafetyHold` property; the engine surfaces safety hold as `Faulted`, blocks Connect until recovery, and allows Disconnect to attempt recovery even when raw data-plane state is already disconnected.
- A cancelled startup is marked `Disconnected` only after `StopAsync` confirms both that forwarding stopped and there is no safety hold; otherwise the state remains `Faulted`.
- Engine and runtime async disposal methods share the same cleanup task for concurrent/repeated callers. Gates are not disposed while pending operations/callbacks may need to unwind.
- New test source covers cleanup failures, safety-hold recovery, rejected connect while held, deterministic cancelled-start cleanup and concurrent disposal. Those tests have not been executed.
- A source-only review also corrected the fake runtime async start helper to use a valid bare return path. No tests were run to validate compilation.

## Remaining coding checklist before final test stage

1. Continue HTTP/2 review: validate stream-state transitions on RST_STREAM/GOAWAY, DATA after END_STREAM, peer SETTINGS semantics, HPACK/header limits, stream-ID exhaustion, flow-control windows, cancellation and all reader/keepalive failure paths. Do not break the Android server's classic CONNECT contract or ALPN `h2`.
2. Review SOCKS5 TCP and DNS-only UDP association lifetime. General UDP/QUIC must continue to fail closed unless the real edge protocol supports it. DoH and DNS-over-TCP must use the authenticated upstream, preserve DNS IDs and validate response bounds.
3. Review HTTP and SOCKS5 upstream proxy chaining for cancellation/timeout, credential cleanup, IPv6 authority, malformed responses and strict no-direct-fallback behavior.
4. Review HEV/Wintun runtime loader, exact native ABI and dependencies, native worker shutdown, temporary config cleanup and interface discovery. Keep full-system connection disabled if either address family cannot be safely routed.
5. Audit route/DNS/Firewall journal transactions and abnormal shutdown. The kill-switch must remain until routes/metrics/DNS are restored and verified. Do not delete a journal or remove rules if cleanup is uncertain.
6. Finish WebView2 verification cookie scoping, token refresh, app launch/exit behavior and auto-start. Per-app split tunneling is still unavailable; keep it documented rather than adding a fake toggle.
7. Reconcile all docs, reports, version metadata, manifest and Windows CI packaging at every change. Ensure native license files and source distribution notices are preserved.
8. When, and only when, the coding checklist is complete, run final Windows CI restore/build/test, HEV/Wintun x64 ABI checks, portable publish and Inno Setup. Fix all failures and rerun until green.

## Required final acceptance

- Windows 10 and Windows 11 solution build and automated tests; capture actual logs.
- Real Firefox sign-in/2FA/challenge and Guardian pass refresh; upstream proxy modes.
- Live HTTP/2 ALPN + CONNECT, SOCKS5 TCP, DNS/DoH fallback and token expiry.
- Native HEV/Wintun ABI/load, full IPv4/IPv6 route setup, DNS NRPT, edge endpoint bypass and firewall guard.
- Cancellation, abrupt process termination, Wi-Fi change, sleep/resume, disabled Firewall, stale journal recovery, non-admin behavior and uninstall/reboot recovery.
- DNS/IPv4/IPv6 leak check and confirmation there is no direct fallback. General UDP/QUIC must be reported unsupported while edge only offers TCP CONNECT.
- Portable ZIP/folder and installer are generated and installation tested before being called deliverables.

## Deliverables

Create the next full project ZIP, Windows source ZIP, phase report, migration report, manifest and continuation prompt. Explicitly state which tests/build ran (if any), remaining limitations, and whether actual Windows artifacts were generated.
