# Phase 2.6 — DoH HTTP Response Validation

`ReadDohResponseAsync` now delegates header acceptance to `DohHttpResponseRules`. The parser is intentionally conservative: only HTTP/1.0 or HTTP/1.1 status 200 is accepted; exactly one valid `Content-Length` (12–4096) and exactly one `Content-Type: application/dns-message` are required; `Transfer-Encoding`, duplicate sensitive fields, malformed names, obsolete line folding, and prohibited control/non-ASCII bytes are rejected. This avoids accepting ambiguous response framing. The DNS body is still separately correlated to the original query by `DnsMessageRules`.

Automated test source has been added, but tests have not been executed yet by design. Runtime correctness must be confirmed during final Windows CI and end-to-end acceptance testing.

Additional hardening: HTTP header line endings must be CRLF pairs; bare carriage returns and line feeds are rejected. Corresponding test source was added but not executed.
