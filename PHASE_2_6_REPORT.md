# FoxyVPN Phase 2.6 Report — Strict DoH HTTP Response Parsing

## Changes
- Added `DohHttpResponseRules` to validate DoH HTTP/1.x response headers before accepting a DNS body.
- Requires a successful `200` status, exactly one bounded `Content-Length`, exactly one `application/dns-message` content type, and rejects transfer encoding, malformed field lines, obsolete folded fields, invalid token names, non-ASCII/control bytes and duplicate security-sensitive headers.
- Connected the parser to the SOCKS5 DNS relay. A malformed or ambiguous DoH response is rejected and the existing DNS-over-TCP-over-authenticated-CONNECT fallback may be attempted; no direct Internet socket was introduced.
- Added xUnit source covering valid headers, duplicate lengths/content types, wrong MIME type, transfer encoding, malformed lines, status and length boundaries.
- Aligned WPF, Inno Setup and CI installer version metadata to `2.6.0`.

## Validation performed in this authoring environment
- Static source inspection and packaging checks only.
- XML/XAML/project files and YAML syntax are checked as part of packaging validation.
- ZIP archive integrity and SHA-256 files are checked.

## Not performed
- No `dotnet restore`, `dotnet build`, `dotnet test`, native HEV/Wintun build or ABI test was run.
- No live authentication, real HTTP/2 CONNECT/DoH exchange, DNS leak test, route/firewall rollback test, portable publish or installer acceptance test was run.
- This report does not claim a production-ready or runtime-verified VPN.

Additional hardening: HTTP header line endings must be CRLF pairs; bare carriage returns and line feeds are rejected. Corresponding test source was added but not executed.
