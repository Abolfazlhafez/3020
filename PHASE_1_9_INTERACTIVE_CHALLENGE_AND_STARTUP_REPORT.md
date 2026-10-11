# FoxyVPN Phase 1.9 — interactive verification and startup flow report

## Scope completed in source

Phase 1.9 focuses on finishing the human verification path and the connect-on-launch startup contract rather than changing the VPN protocol. The Android automatic Fastly challenge solver was **not** ported. Windows now opens a user-mediated WebView2 window for approved HTTPS Firefox Accounts / Guardian origins; the user completes any verification and explicitly clicks to return to FoxyVPN.

Implemented source changes:

- Added `InteractiveChallengeWindow.xaml` / `.xaml.cs` and `InteractiveChallengeDialogFlow.cs` using Microsoft WebView2 WPF package `1.0.4258.31`. DevTools are disabled; navigation and pop-ups require HTTPS Firefox/Mozilla service domains. Browser data is isolated per approved service origin.
- Added `InteractiveChallengeCookieBridge` with explicit approved origin hosts, cookie count/name/value/path limits, secure-cookie requirement, expiry checks, control-character rejection and cookie-domain scoping to the challenged origin. Cookies are imported into the matching API client's `CookieContainer` in memory only; no cookie values or bearer tokens are logged.
- Added handling at Firefox Account sign-in, verification-code submission, access-token refresh in Account, Guardian account refresh, proxy-only connect, system-tunnel connect and connect-on-launch. Each operation retries once after user approval; repeated 406 responses are surfaced rather than retried indefinitely.
- The periodic Guardian proxy-pass renewal loop stops with an explicit log on human-verification-required responses instead of repeatedly hitting the challenge endpoint.
- Connect-on-launch waits for prior route recovery to finish; it skips automatic startup if safe recovery requires elevation that the current process does not have.
- Corrected the settings/reporting state so per-application split tunneling is identified as unavailable. No UI toggle claims it works.
- Added unit-test source for null/malformed cookie snapshots; no tests were run in this coding phase by design.

## Security / behavior notes

This is an interactive user flow, not an anti-bot solver or bypass. The cookie bridge accepts cookies only from allow-listed HTTPS origins and validates each cookie before import. The WebView2 runtime must be installed on Windows to use the challenge window. Because service anti-bot behavior can change, live compatibility must be verified during final testing; the origin page may not always present a usable challenge. In that case the request fails closed and the user gets a clear message.

Per-application split tunneling remains unimplemented. Windows Firewall application rules alone do not make a process use a different route table, so a misleading toggle is deliberately not exposed. Current upstream protocol supports TCP CONNECT; general UDP/QUIC remains unsupported. The presence of route/HEV source is not evidence of zero-leak operation.

## Coding checks and final-stage plan

The authoring environment has no .NET SDK/MSBuild. In line with the user's explicit process preference, comprehensive tests are deferred until coding is complete. Before final handoff, perform only static checks (XML/XAML/project structure, event-handler names, solution path validity) and archive integrity checks. Do not treat them as a substitute for executing the final plan below.

## Final-stage checklist (not yet executed)

- [ ] Restore and Release-build the WPF solution on Windows 10/11 / Windows CI; fix every compiler error.
- [ ] Execute all xUnit tests; fix failures and re-run.
- [ ] Build pinned HEV native library, confirm ABI/symbols and load the verified Wintun DLL.
- [ ] Test Firefox sign-in, email verification, expired-token refresh, Guardian challenge and cookie isolation against real responses.
- [ ] Test HTTP/2 ALPN/certificate validation, CONNECT stream flow control, local SOCKS5 TCP relay and DNS fallback against a compatible live endpoint.
- [ ] Verify routes and DNS install/rollback in every startup/disconnect failure path; kill-switch behavior under interface changes, firewall changes, sleep/wake and process crash.
- [ ] Run IPv4, IPv6 and DNS leak tests; measure unsupported UDP/QUIC behavior.
- [ ] Build and install-test portable x64 and Inno Setup artifacts; verify WebView2 runtime prerequisite and clean uninstall.
- [ ] Only after evidence from all above, decide whether production-ready/full-system VPN criteria are met.

## Current status

Source milestone only. No build/test/live connectivity/no-leak result or installer executable is claimed.
