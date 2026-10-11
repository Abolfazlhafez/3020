# FoxyVPN Windows source bundle — Phase 2.5

The **full Android+Windows archive** preserves the original Android/Kotlin/Gradle project. This Windows-source archive focuses on the native WPF/.NET 8 port under `windows/`, its xUnit test source, Visual Studio solution, Inno Setup script, third-party notices and Windows CI workflow under `.github/`.

## Build on Windows 10/11

Install Visual Studio 2022 with the .NET desktop workload and .NET 8 SDK. From the extracted archive root, after the final code phase is complete, run the requested end-of-project verification:

```powershell
dotnet restore .\windows\FoxyVPN.sln
dotnet build .\windows\FoxyVPN.sln --configuration Release --no-restore
dotnet test .\windows\FoxyVPN.sln --configuration Release --logger "trx;LogFileName=foxyvpn-tests.trx"
```

The GitHub Actions workflow also builds pinned HEV 2.17.1 via MSYS2, verifies exported native C symbols and x64 PE format, verifies the official Wintun 0.14.1 archive hash and Authenticode publisher, publishes a self-contained portable x64 app, and runs Inno Setup. The workflow has not yet been run for Phase 2.5 in this authoring environment.

## Phase 1.8 VPN code status

The source now wires the full-system runtime in order: authenticated TLS/ALPN `h2` and HTTP/2 CONNECT; upstream HTTP CONNECT or SOCKS5 chaining (optional); loopback SOCKS5; HEV 2.17.1 with a Windows-owned Wintun adapter; then an `ActiveStore` route/DNS transaction. That transaction uses a named cross-process semaphore, stable adapter IDs in its journal, original physical gateway and upstream endpoint bypass routes, IPv4/IPv6 split-default routes, reversible interface metrics, a root NRPT fake-DNS rule, and persistent Windows Firewall block rules for public physical egress. Firewall rules are removed only after route and interface-metric restore is verified. Startup fails closed if Windows Firewall profiles are disabled.

This is **not yet verified as a production VPN**. The managed solution and native DLLs have not been built or executed here. The edge protocol still supports TCP CONNECT only, so non-DNS UDP/QUIC is deliberately dropped. Phase 1.9 adds user-mediated service verification. Remaining acceptance work includes a genuine Windows per-app routing design (not implemented) and final real Windows CI plus end-to-end DNS/IPv4/IPv6/leak/rollback tests. Network-change/suspend-resume handling now monitors active sessions and refreshes physical-interface guard coverage, but it intentionally marks an unhealthy old upstream as faulted rather than transparently reconnecting on uncertain routes. The source ZIP does not include a verified native HEV DLL; CI builds it and bundles the signed Wintun runtime. Do not claim successful compilation, working live service or installer until those jobs have run.

See `PHASE_1_8_CONNECTIVITY_AND_RENEWAL_REPORT.md`, `CONTINUATION_PROMPT_PHASE_1_8.md`, `PHASE_1_7_FULL_TUNNEL_ORCHESTRATION_REPORT.md`, `CONTINUATION_PROMPT_PHASE_1_7.md`, `windows/MIGRATION_REPORT.md`, `windows/README_WINDOWS.md`, and `windows/THIRD_PARTY_NOTICES.md`.


## Phase 1.9 interactive verification (preserved)

This source adds a WebView2 WPF window for user-completed Firefox Accounts/Guardian service verification when an API returns HTTP 406. The browser is limited to HTTPS Firefox/Mozilla domains for top-level and pop-up navigation; only eligible secure, non-expired cookies from explicitly approved service origins can be imported to the correct API client's in-memory cookie jar. Sign-in, 2FA, token refresh, Guardian account refresh and connection startup retry only once after the user completes verification. Automatic Fastly solving/bypass is intentionally not implemented. WebView2 Evergreen Runtime is required for this interactive window.

Version metadata was `2.2.0` in the interactive-verification phase. Current Windows version metadata is `2.5.0` and is aligned across the project, installer and CI invocation. No build, tests, native build, runtime integration or installer generation was executed in this authoring environment. Tests remain deferred until the end-of-coding validation stage. Per-app split tunneling remains unavailable and is documented explicitly.


## Phase 2.0 upstream proxy hardening

HTTP CONNECT and SOCKS5 upstream negotiation have a test seam in `WindowsHttp2ConnectSession`, with deterministic test cases in `FoxyVPN.Tests/UpstreamProxyHandshakeTests.cs`. Password/username octet arrays are cleaned in `finally`; SOCKS5 RFC 1929 rejects empty/oversized username or password values. These tests are authored but have not been executed, as the project owner requested comprehensive validation only after coding is complete.

Version metadata is `2.0.0`. The actual Windows build, tests, native HEV artifact creation, live service connection, DNS/IPv4/IPv6 leak validation and installer output remain unverified. General non-DNS UDP/QUIC and per-app split tunneling remain unavailable.


## Phase 2.1 lifecycle hardening

Version metadata is `2.1.0`. The HTTP/2 CONNECT duplex stream no longer blocks client-to-edge writes merely because the edge sent `END_STREAM` in the reverse direction; the local half-close remains independent. The tunnel engine surfaces a runtime safety hold as `Faulted`, refuses to reconnect over uncertain route ownership, and continues to allow explicit disconnect/recovery even when data flow is already down. Startup only becomes `Disconnected` after cancellation when teardown was verified clean. Both the engine and system runtime share an idempotent async disposal task to avoid duplicate-cleanup and disposed-semaphore races. New xUnit test source covers the safety-hold, cancellation and concurrent disposal cases. These tests have not been run.

**Validation state:** this phase changed code and authored tests only. No managed build, xUnit execution, native HEV/Wintun build, live Windows tunnel, leak/rollback acceptance, portable build, or installer generation is claimed.


## Phase 2.2 HTTP/2 protocol hardening

The session now serializes client stream initiation and HEADERS emission, uses a non-wrapping stream-ID counter, makes frame writes bounded and atomic from the shared-session perspective, validates monotonic GOAWAY and idle/closed stream transitions, and bounds/validates decoded response headers. New `Http2ProtocolRulesTests` are authored but remain unexecuted until the final test stage. No compiler, live connection, native tunnel, route rollback, DNS/IPv4/IPv6 leak or installer acceptance result is claimed.


## Phase 2.4 informational HTTP/2 responses

The Windows HTTP/2 CONNECT session now waits for a final response after legal informational response headers (100–199 except 101). The CI installer invocation has been aligned at version `2.4.0`; current phase reports and prompts are included in the bundle. New unit-test source remains unexecuted until the final coding phase. No actual build, live VPN, leak test or installer output is claimed.


## Phase 2.5 DNS response correlation

DNS response handling now matches the transaction ID, QR/opcode, question count and canonical question name/type/class against the originating query. Malformed or mismatched responses are dropped; a bad DoH response can fall back to DNS-over-TCP through the authenticated upstream, never to direct networking. Test source is included but not run. Active Windows metadata is `2.5.0`. No build or live VPN acceptance is claimed.


Phase 2.6 adds strict, bounded DoH HTTP response-header parsing and tests source for ambiguous/malformed framing. Tests are deferred until coding is complete; no build or runtime VPN acceptance is claimed. Active Windows metadata is 2.6.0.
