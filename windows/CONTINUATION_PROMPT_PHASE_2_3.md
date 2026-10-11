# Continuation prompt — FoxyVPN Phase 2.3 → Phase 2.4

Continue from `FoxyVPN-Android-and-Windows-Phase2.3.zip` or `FoxyVPN-Windows-Source-Phase2.3.zip`. Preserve Android functionality and existing architecture; work directly on the supplied files. Windows version is 2.3.0.

## Owner workflow
The owner wants coding/integration completed before comprehensive testing. Do not run `dotnet test`, live service tests, route/leak acceptance, or installer acceptance until the coding checklist is complete. Static review and archive integrity checks are allowed. Never claim successful build, live VPN, leak-free operation, or an installer unless evidence exists.

## Phase 2.3 change
`Http2ProtocolRules.ParseResponseStatus` now accepts only 100–599 and rejects 101, which is forbidden in HTTP/2. Unit tests were added for 599, 101, 600, and 999. Windows version metadata is 2.3.0.

## Coding checklist
1. Review HTTP/2 frame validation against RFC 9113: unknown flags, error scope, SETTINGS updates, flow-control and padding accounting, HPACK state, and all waiters on session failure.
2. Audit SOCKS5 TCP and DNS UDP relay lifecycle, destination/source validation, DNS truncation and ID handling, DoH cancellation and DNS-over-TCP fallback.
3. Audit HTTP CONNECT/SOCKS5 upstream chaining for fragmented reads, auth, IPv6, cancellation, secret lifetime, and no direct fallback.
4. Verify HEV/Wintun ABI, x64 DLL loading and dependency placement, config ACLs, and bounded native shutdown.
5. Audit route/NRPT/firewall journaling for crash/reboot recovery, adapter changes, disabled firewall, elevation failure, and rollback verification. Keep egress guard if cleanup is uncertain.
6. Audit WebView2 origin/profile isolation, challenge completion, token renewal cancellation, autostart, exit handling, and log redaction.
7. Reconcile manifests, third-party notices, CI artifact paths, portable packaging, installer scripts and version metadata; keep Android sources intact.
8. Only after coding is complete, run Windows CI restore/build/test, HEV/Wintun ABI checks, portable publish and Inno Setup; fix failures and rerun.

## Final acceptance
Capture real Windows 10/11 build/test logs; verify live sign-in/challenge, Guardian refresh, ALPN h2/CONNECT, upstream proxy and SOCKS5 TCP, real HEV/Wintun packet forwarding, route/DNS transaction and rollback, endpoint bypass, firewall guard, IPv4/IPv6/DNS leak checks, abrupt termination/reboot recovery, portable build and installer install/uninstall. General UDP/QUIC and per-app split tunneling remain unsupported unless actual upstream/native support is implemented.
