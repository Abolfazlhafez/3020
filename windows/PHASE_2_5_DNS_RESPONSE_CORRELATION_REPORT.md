# Phase 2.5 — DNS response correlation hardening

## Changes made

- Added `DnsMessageRules` for query/response correlation based on the transaction ID, QR bit, opcode, question count, and canonical question tuples (name, record type, record class).
- DNS names are expanded with explicit message bounds, RFC label-length checks, a maximum expanded name length of 255 octets, and rejection of forward/self-referential compression pointers. ASCII name comparison is case-insensitive.
- DoH answers are accepted only when they match the original question. A malformed or mismatched DoH answer proceeds to the existing DNS-over-TCP transport through authenticated HTTP/2 CONNECT. A malformed or mismatched TCP answer is dropped.
- Replaced the relay's prior ID-and-QR-only check with the full query correlation rule.
- Added xUnit test source for success, transaction ID mismatch, QR mismatch, opcode mismatch, question name/type/class mismatch, truncated questions, and malformed compression pointers.
- Bumped active Windows project/installer/CI metadata to `2.5.0` and added the new phase report, manifest, and continuation prompt to the CI artifact allowlist.
- No Android project files were intentionally changed.

## Verification status

This is an authored source change. Unit tests, .NET restore/build, native HEV/Wintun build, end-to-end VPN, route/firewall rollback, DNS/IPv4/IPv6 leakage, portable publish, and installer generation were not run. Archive/XML/YAML/source consistency checks are packaging checks only and do not establish compilation or operational safety.

## Safety limitations retained

The relay does not introduce direct DNS or direct Internet fallback. General UDP/QUIC and per-application split tunneling remain unsupported unless an actual compatible transport/native implementation is added and accepted by end-to-end tests.
