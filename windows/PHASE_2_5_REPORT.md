# FoxyVPN Phase 2.5 report — DNS response correlation

## Delivered
- Added bounded DNS response correlation for transaction ID, QR/opcode, question count, canonical QNAME, QTYPE and QCLASS.
- Added strict DNS name parsing with compression-pointer, label-boundary and expanded-length validation.
- Rejects mismatched DoH responses; allows the already-implemented authenticated-upstream DNS-over-TCP fallback. Rejects mismatched DNS-over-TCP responses. No direct-network fallback was added.
- Added xUnit test source for expected matching and malformed/mismatched response cases. Tests were deliberately not run before the owner-declared end of coding.
- Aligned active WPF project, Inno Setup and CI installer metadata to `2.5.0`; extended CI artifact collection for Phase 2.5 documents.
- Preserved Android/Kotlin/Gradle sources without intentional changes.

## Validation and limitations
The source and test cases were authored. No `dotnet restore`, `dotnet build`, `dotnet test`, native HEV/Wintun build, live service connection, route/firewall rollback acceptance, DNS/IPv4/IPv6 leak test, portable publish, or installer generation is claimed. Any static XML/YAML/archive checks performed for packaging are not a substitute for those acceptance tests.

## Next phase
Continue the coding audit into DNS-over-HTTPS HTTP response framing/duplicate headers, HTTP/2 frame error scopes and flow control, native HEV/Wintun ABI and shutdown, route/firewall journal recovery, and WebView2/token/autostart lifecycle. Run comprehensive Windows acceptance only when code changes are complete.
