# FoxyVPN Phase 2.3 — HTTP/2 response status validation

## Change summary
- Tightened HTTP/2 `:status` validation: only three ASCII digits representing 100–599 are accepted, and status 101 is rejected because HTTP/2 does not permit protocol switching via 101.
- Added unit-test cases for valid upper-bound status 599 and rejected values 101, 600 and 999.
- Bumped Windows project and installer metadata to 2.3.0 and updated the migration/readme references where applicable.

## Scope and limits
This is a narrow source-hardening step, not a claim that the complete VPN works. It does not implement generic UDP/QUIC or per-application split tunneling. The server-side transport still needs a compatible live endpoint for real VPN use.

## Verification status
- Source change and test cases authored.
- Static package checks may be run when creating the archive.
- `dotnet build`, `dotnet test`, live HTTP/2/CONNECT checks, native HEV/Wintun loading, route rollback, leak testing, and installer acceptance were not run in this environment. The owner requested that comprehensive tests wait until coding is complete.
