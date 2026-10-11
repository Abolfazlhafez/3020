# FoxyVPN Phase 2.4 report — HTTP/2 informational responses and CI version alignment

## Delivered
- HTTP/2 CONNECT response handling now recognizes informational statuses (100–199 except 101) and waits for the final response instead of incorrectly rejecting the CONNECT stream.
- An informational response carrying `END_STREAM` is rejected as a protocol error; a successful CONNECT still requires a final 2xx response.
- Added pure unit-test source for informational status classification, including 100, 103, 199, 101, 200, 299, and invalid status 0.
- Aligned the Windows project, Inno Setup default, and GitHub Actions installer argument to version `2.4.0`. The previous CI command was still pinned to `2.2.0`.
- Added the latest Windows phase reports, continuation prompt, and manifest to the Windows CI artifact allowlist.
- Android/Kotlin/Gradle sources were preserved without intentional changes.

## Verification and limitations
The source changes and unit-test cases are authored, but `dotnet restore`, `dotnet build`, and `dotnet test` were deliberately not run because the owner requested that comprehensive testing wait until coding is complete. Static XML/YAML/source consistency checks and ZIP integrity checks are packaging checks only; they do not prove compilation, runtime VPN operation, native ABI compatibility, leak-free routing, or installer acceptance.

## Still pending
Continue the coding audit through SETTINGS/flow-control error scope, SOCKS5 DNS relay and DoH lifecycle, native HEV/Wintun integration, route/firewall recovery, WebView2 isolation, and packaging. Run full Windows 10/11 build/test/native/live-tunnel/rollback/leak/installer acceptance only after coding is complete. General UDP/QUIC and per-application split tunneling remain unsupported.
