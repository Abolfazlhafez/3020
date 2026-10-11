# Continuation prompt — FoxyVPN Phase 2.4 → Phase 2.5

Continue from `FoxyVPN-Android-and-Windows-Phase2.4.zip` or `FoxyVPN-Windows-Source-Phase2.4.zip`. Keep the Android project intact. The Windows source version is `2.4.0`.

## Owner workflow
Finish coding/integration before comprehensive testing. Do not run `dotnet test`, live service, route/leak, native runtime or installer acceptance until the remaining coding audit is complete. Static review and archive integrity checks are allowed. Never claim build or live VPN success without logs/evidence.

## Phase 2.4 changes
- HTTP/2 CONNECT now waits through legal informational response headers (100–199 except 101) for the final response and rejects informational responses carrying `END_STREAM`.
- Added status-classifier unit-test source, not executed.
- Corrected the Windows CI installer version argument from stale `2.2.0` to `2.4.0`, aligned with project and installer metadata, and added Phase 2.3/2.4 docs to the CI artifact allowlist.

## Phase 2.5 coding priorities
1. Continue the HTTP/2 audit: stream-vs-connection error scope, SETTINGS update atomicity, receive/send flow-control accounting, closed-stream behavior, response trailers and HPACK decoder behavior.
2. Audit `WindowsSocks5UdpDnsRelay`: parse the entire SOCKS5 UDP header strictly, validate source endpoints and domain/address encodings, bound DNS packets, preserve DNS transaction IDs correctly, handle truncation, TCP fallback and DoH cancellation without stale relay tasks.
3. Audit upstream HTTP CONNECT/SOCKS5 proxy negotiation with fragmented responses, IPv6 authority formatting, authentication edge cases, cancellation and secret lifetime; never add direct fallback when a proxy fails.
4. Audit HEV/Wintun ABI and x64 DLL/dependency packaging, config ACLs, and bounded native shutdown.
5. Audit route/NRPT/firewall journaling on crash/reboot, physical adapter changes, disabled firewall, elevation failures and verified rollback. Keep the egress safety guard whenever cleanup is uncertain.
6. Audit WebView2 origin/profile isolation, challenge completion, token renewal cancellation, startup, exit and log redaction.
7. Keep manifests, third-party notices, CI artifact paths, portable packaging and installer metadata synchronized. Preserve Android sources.
8. After coding is complete, run Windows CI restore/build/tests, HEV/Wintun ABI checks, portable publish and Inno Setup, then fix and rerun any failures.

## Final acceptance checklist
Capture actual Windows 10/11 build and test logs; verify sign-in/challenge/Guardian refresh, ALPN `h2` + CONNECT, HTTP/SOCKS5 upstream chaining, HEV/Wintun packet forwarding, route/DNS transactions and rollback, endpoint bypass/firewall, DNS/IPv4/IPv6 leak checks, crash/reboot recovery, and portable/installer install-uninstall. Generic UDP/QUIC and per-app split tunneling remain unsupported unless implemented with actual server/native support.
