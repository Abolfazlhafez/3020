# FoxyVPN Phase 2.2 — HTTP/2 protocol and stream lifecycle hardening

**Application source version:** 2.2.0  
**Scope:** code and integration source; comprehensive tests intentionally deferred until the owner’s coding checklist is complete.

## Changes in this increment

### Ordered stream initiation

Concurrent `OpenConnectAsync` calls can independently fetch Guardian proxy passes, but a dedicated gate now covers allocation, registration and emission of each stream’s HEADERS frame. This preserves increasing client stream-ID initiation order while allowing response waits and active data streams to remain concurrent. Stream-ID arithmetic now uses `long` internally; exhaustion disables new streams without wrapping an `int` counter or stopping existing streams from draining.

### Frame-write cancellation safety

Cancellation is checked before a frame enters the write critical section. After the first frame byte is emitted, caller-level cancellation no longer interrupts the header/payload pair; session shutdown can still cancel it, and a bounded 15-second frame-write timeout tears down the entire HTTP/2 transport if a write stalls. Any failure during a frame write aborts the shared connection, because a partial frame means it cannot safely be reused.

### Stricter peer stream-state validation

- Repeated GOAWAY frames may reduce but may not increase the last-stream ID.
- RST_STREAM and WINDOW_UPDATE targeting an idle/non-client stream ID fail the HTTP/2 session; events targeting an already-closed client stream are handled according to the appropriate close/ignore path.
- DATA for a closed client stream returns connection-level flow-control credit and emits STREAM_CLOSED; DATA for an idle stream is treated as a protocol error.
- HEADERS for an idle stream are rejected, while a late header block for a closed stream is decoded (to preserve HPACK synchronization) then reset.
- Rejected CONNECT responses are reset by the caller once instead of risking duplicate RST_STREAM frames from both the reader and waiting caller.

### Response header validation

The response decoder now applies a 128-field cap and a 64 KiB decoded field budget; rejects unexpected or out-of-order pseudo-headers, uppercase/non-token field names and disallowed control bytes; rejects duplicate `:status`; and accepts status codes only in a three-digit form. Classic CONNECT remains unchanged: TLS certificate checks and ALPN `h2` remain mandatory, the request uses `:method`, `:authority` and never-indexed `proxy-authorization`, and there is no HTTP/1.1 fallback.

### Test source

Added pure unit-test source for monotonic GOAWAY IDs, idle stream ID classification, response status parsing, pseudo-header ordering, lowercase/token field names and forbidden control bytes. Existing tunnel lifecycle and proxy-handshake tests remain in the project. These tests were **not executed** in this increment, per the project owner's request to postpone comprehensive tests until coding is finished.

## Static checks performed

Static source/package checks were conducted only: XML/XAML/project/manifest parsing, YAML parsing, solution project path and resource path existence, XAML event-handler reference mapping, C# delimiter-balance scan, version metadata reconciliation and ZIP integrity. These checks do not provide compiler/type-binding or live networking evidence. Exact counts are recorded in the package manifest.

## Not verified / remaining

- No `dotnet restore`, `dotnet build`, `dotnet test`, Windows CI run, native HEV/Wintun build/ABI run, live server session, route transaction or leak test was executed.
- HEV/Wintun native integration and full-system Windows 10/11 tunnel remain unverified.
- The service edge’s current CONNECT/TCP contract does not enable general UDP/QUIC. The DNS-only relay is source-authored but not live-verified.
- DNS NRPT/firewall kill-switch recovery and sudden-process-exit recovery still require a Windows acceptance test matrix.
- Per-application split tunneling remains unavailable and is not exposed as a functional control.
- No portable executable or installer binary was generated in this environment.

## Next coding stage

Proceed to `CONTINUATION_PROMPT_PHASE_2_2.md`. Continue code review of the native HEV/Wintun ABI and shutdown, DNS relay bounds and upstream semantics, route/firewall journal recovery after process termination, WebView2/token-refresh lifetime, and packaging/license/CI completeness. Only after the coding checklist is complete should the full Windows restore/build/test, live VPN and no-leak matrix be executed.
