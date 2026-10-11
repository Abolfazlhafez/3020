# Continuation prompt — FoxyVPN Phase 2.5 → Phase 2.6

Continue from `FoxyVPN-Android-and-Windows-Phase2.5.zip` or `FoxyVPN-Windows-Source-Phase2.5.zip`. Preserve Android sources; the active Windows source version is `2.5.0`.

The Phase 2.5 delta adds `DnsMessageRules`, correlates DNS responses to the original query (transaction ID, QR/opcode, question count, canonical QNAME/QTYPE/QCLASS), rejects malformed DNS name encoding and adds xUnit test source. Mismatched DoH answers may proceed to DNS-over-TCP through the authenticated HTTP/2 CONNECT tunnel; mismatched TCP answers are dropped. No direct DNS/Internet fallback is allowed.

Do not run the comprehensive test suite until the coding audit is complete, as explicitly requested. Continue source work:

1. Audit `ReadDohResponseAsync` for HTTP response framing, duplicate/conflicting Content-Length and Content-Type, malformed headers, premature EOF, timeout/cancellation and oversized body behavior. Add test seams and test source where feasible.
2. Recheck DNS query validation, valid compressed names, response RCODE/TC handling, source-address association and disposal/awaiting of tracked relay tasks; maintain bounded work and no direct-network fallback.
3. Review HTTP/2 RFC 9113 frame-type/flag rules, connection-vs-stream errors, SETTINGS/window arithmetic, flow control, HPACK state and failure propagation to every waiter.
4. Re-audit upstream HTTP CONNECT/SOCKS5 partial reads, authentication, IPv6 authorities, cancellation and secret zeroing; never silently fall back around an upstream proxy.
5. Verify HEV/Wintun ABI, x64 native DLL dependencies, ACLs, native shutdown bounds, route/NRPT/firewall journaling, crash/reboot recovery and persistent egress blocking.
6. Review WebView2 isolation, anti-bot challenge handling, token renewal, autostart and log redaction; reconcile CI artifact paths, notices, manifests and installer/portable metadata.
7. Bump version metadata for the next phase, keep Android source unchanged, create complete + Windows-source ZIPs, checksums, manifest, report and next continuation prompt.

After the coding audit is complete, run Windows CI restore/build/test, HEV/Wintun ABI/native build, portable publish and Inno Setup. Final acceptance must include Windows 10/11 live login/challenge/Guardian refresh, HTTP/2 ALPN h2/CONNECT, optional upstream proxy chain, real HEV/Wintun packet forwarding, DNS/route/firewall rollback, IPv4/IPv6/DNS leak checks, abrupt exit/reboot recovery, and install/uninstall. Report logs and failures honestly; do not claim tests passed when not run. General UDP/QUIC and per-app split tunneling remain unsupported unless implemented and verified.
