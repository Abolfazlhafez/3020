# Continuation prompt — FoxyVPN Phase 2.2 → Phase 2.3

Continue from `FoxyVPN-Android-and-Windows-Phase2.2.zip` (full Android + Windows) or `FoxyVPN-Windows-Source-Phase2.2.zip`. Work directly on this source; never recreate the project, remove original Android features, or replace real services with mock data. Current Windows source version: `2.2.0`.

## Owner workflow rule

The owner wants coding and integration completed before the comprehensive final test pass. Do not run `dotnet test`, live service tests, route/leak acceptance tests, or installer acceptance until the coding checklist is complete. Static source review and file integrity checks are allowed. Never claim a successful build, live VPN, leak-free state or generated installer without captured evidence.

## Phase 2.2 changes already authored

- A new `Http2ProtocolRules` helper centralizes GOAWAY monotonicity, idle-client-stream classification, response status syntax and response-header validation.
- `WindowsHttp2ConnectSession` serializes allocation/registration/HEADERS emission with `_openStreamGate`, keeps ID arithmetic in `long`, and stops accepting new streams on identifier exhaustion.
- Frame writes use a session-scoped timeout and no longer honor caller cancellation after a frame starts; a write failure aborts the shared HTTP/2 session so a partially emitted frame is never reused.
- GOAWAY, RST_STREAM, WINDOW_UPDATE, DATA and HEADERS handling is stricter for idle/closed stream IDs. Late closed-stream header blocks still pass through HPACK decode to preserve decoder synchronization.
- HTTP/2 response headers have decoded byte/count budgets and pseudo-header/name/value validation; CONNECT rejection no longer risks two reset frames.
- `Http2ProtocolRulesTests.cs` adds test source, not yet executed.

## Remaining coding checklist before final test stage

1. Continue protocol review against RFC 9113: unknown frame flags, connection-vs-stream error scope, RST_STREAM races, SETTINGS changes while streams are open, padding/window accounting, HPACK decoder state and header validation. Preserve the Android classic-CONNECT contract and strict ALPN `h2`.
2. Review response-header listener and frame-processing behavior when a peer sends malformed headers or connection close at each byte boundary. Ensure every session failure fails all streams and leaves no hung waiters.
3. Audit SOCKS5 TCP/UDP DNS relay lifetimes, datagram destination/source validation, DNS IDs/response length/truncation, DoH cancellation and DNS-over-TCP fallback over the authenticated CONNECT stream. General UDP/QUIC must remain explicitly unsupported while the edge offers TCP CONNECT only.
4. Review upstream HTTP CONNECT and SOCKS5 chaining for fragmented reads, proxy auth, IPv6 targets, cancellation during handshake, credential lifetime and strict no-direct-fallback.
5. Inspect HEV/Wintun native ABI, x64 loader search path, dependent DLLs, config file ACLs and cleanup. Ensure native shutdown can be called once and awaited without a hung worker.
6. Inspect route/NRPT/Firewall transaction journaling: crash during each write step, reboot recovery, stale adapter IDs, multiple network profiles, admin elevation, disabled firewall and rollback verification. Do not remove the egress guard or journal when cleanup is uncertain.
7. Audit WebView2 origin/profile isolation, challenge completion, token refresh cancellation, auto-start, window exit and log redaction.
8. Reconcile version metadata, package manifest, migration report, third-party license notices, CI artifact paths, and the installer/native dependency bundle. Keep source Android files intact.
9. When the coding checklist is complete, run Windows CI `restore`, `build`, `test`, HEV/Wintun ABI checks, portable publish and Inno Setup; fix all failures and rerun to green.

## Final acceptance requirements

- Actual Windows 10/11 restore/build/test logs and test report.
- Live Firefox sign-in/challenge, Guardian pass refresh, HTTP/2 ALPN and CONNECT, upstream proxies and SOCKS5 TCP.
- HEV/Wintun native load and real packet forwarding, route and DNS installation/rollback, edge endpoint bypass and firewall guard.
- DNS/IPv4/IPv6 leak testing; verify no direct fallback after server loss, sleep/resume, Wi-Fi change, crash, disabled firewall or admin failure.
- Test recovery after abrupt process termination and reboot. General UDP/QUIC and per-app split tunneling remain documented as unsupported unless actual edge/native support is implemented.
- Generate and install-test the portable build and installer before claiming them ready.

## Deliverables

Create the next full Android+Windows ZIP, Windows-source ZIP, phase report, updated migration report, manifest, checksums and continuation prompt. Explicitly say which build/tests ran and which remain pending.
