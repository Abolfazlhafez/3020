# Continue FoxyVPN Windows from Phase 1.8 → Phase 1.9

Continue from `FoxyVPN-Android-and-Windows-Phase1.8.zip`. Preserve the original Android source and all Windows source already written. Do not recreate the application from scratch.

## User workflow preference

The user explicitly wants **all coding and integration completed first, comprehensive tests at the end**. Do not start the final test campaign yet. Keep only lightweight structural checks during coding. Be honest that those checks are not a compiler and do not claim the VPN works until the complete Windows test campaign passes.

## Work already in source

- Native C#/.NET 8 WPF desktop UI, 2027 motion treatment, Reduce Motion, themes, auto-start/start-minimized, Firefox account screens, server catalog/favorites, logs and diagnostic ZIP.
- HTTP/2 CONNECT over TLS with validation and ALPN `h2`, Guardian proxy-pass injection and optional upstream HTTP CONNECT/SOCKS5 chaining; loopback-only SOCKS5 and DNS-only UDP ASSOCIATE/DoH-with-DNS-over-TCP fallback. General UDP/QUIC is not provided by the known edge protocol and remains dropped.
- HEV `tun2socks` C API lifecycle and pinned native build pipeline/Wintun verification in Windows CI.
- Registered full-system runtime order: H2 transport → loopback SOCKS5 → HEV/Wintun → journaled route/DNS/Firewall transaction. Route transaction has stable interface IDs, a cross-process semaphore, host bypass routes for the original gateway/edge, split IPv4/IPv6 route source, reversible metrics, NRPT ownership, persistent public-egress block rules and startup recovery behavior.
- New in Phase 1.8: Windows network address/availability and suspend/resume event monitoring. It expands persistent firewall guard coverage to newly active/renamed physical interfaces, verifies that the current upstream local address remains present and health-checks the tunnel layers. On uncertain health it marks the session faulted and retains routes, journal and guard; it deliberately does not reconnect across uncertain routing.
- New Guardian pass renewal loop: refresh around half lifetime, clamp to 15 seconds–30 minutes, two-minute fallback for unknown expiry, retry after 30 seconds, no token logging, future CONNECT streams use the latest shared token. A pure scheduling helper and xUnit test source were added.
- At elevated app startup, a pending route journal triggers automatic recovery. Non-elevated startup warns and asks the user to relaunch as administrator.

## Continue coding only; do not run final tests yet

1. Inspect the Kotlin Android `FastlyChallengeSolver`, `FxaAuthRepository` and `GuardianClient` in full. Do not fake HTTP 406 success, weaken TLS, log secrets, or implement a CAPTCHA/anti-bot bypass that is not appropriate for an account flow. Design a supported **user-mediated** challenge flow for Windows, with bounded retries and a clear explanation when the service requires a challenge.
2. Investigate real Windows per-application split tunneling (Windows Filtering Platform/AppID or another supported native design). Windows Firewall `-Program` alone does not select a routing table. Implement only if selected processes can be directed through Wintun without public egress leakage; otherwise keep the setting disabled and document the functional gap honestly.
3. Review `WindowsSystemRouteTransaction.RefreshPhysicalEgressGuardAsync` and its PowerShell rule syntax/ownership under concurrent network changes, adapter rename/removal, Firewall profile/service transitions and interrupted cleanup. Keep every mutation journaled before executing it. Never remove the kill switch until routes and metrics are proven restored.
4. Review the background Guardian renewal worker for retry, shutdown, token expiry and app exit races. Preserve current streams during renewal, use the provider's updated cached token for future CONNECTs, and never log its value.
5. Review DNS/NRPT behavior and safe rollback if an adapter disappears, the host wakes from sleep, the edge address changes, or the user switches Wi-Fi/Ethernet. Prefer a faulted safety hold and explicit recovery over an unsafe transparent reconnect.
6. Update all reports, version/versioned installer args, Windows CI artifact paths, package manifest and continuation prompt. Keep Phase 1.8 source as the baseline.

## Final test campaign — only after coding is declared complete

On Windows 10 and Windows 11 x64 with .NET 8/MSBuild and MSYS2, run:

```powershell
dotnet restore .\windows\FoxyVPN.sln
dotnet build .\windows\FoxyVPN.sln --configuration Release --no-restore
dotnet test .\windows\FoxyVPN.sln --configuration Release --no-build --logger "trx;LogFileName=foxyvpn-tests.trx"
```

Then execute `.github/workflows/windows-build.yml`; verify the HEV shared library is a valid x64 Windows PE with required exports/dependencies, validate the official Wintun package signature/hash, and run live full-system acceptance tests for Guardian/H2 CONNECT, upstream proxy authentication, proxy-pass expiry/renewal, SOCKS5 TCP and DNS-only UDP, HEV start/stop, route and NRPT mutations, kill switch, Firewall disabled/external stop, crash recovery, IPv4/IPv6/DNS leaks, Wi-Fi/Ethernet switch, sleep/resume, user disconnect, upgrade/uninstall, auto-start and Reduce Motion. Fix all failures, rerun the full suite, then produce and test a portable executable and installer.

## Non-negotiable honesty and safety

- Never claim compiler/test/real-tunnel success without recorded output.
- Never claim zero leakage before real Windows tests.
- Never delete user routes or unowned firewall/DNS rules. Do not flush routes or overwrite unrelated DNS policy.
- Leave the persistent Firewall guard/journal in place when route rollback is incomplete.
- Do not silently bypass an upstream proxy, disable TLS validation, log tokens/passwords, or claim general UDP/QUIC support.
- Deliver the next full Android+Windows ZIP, Windows-only source ZIP, report and continuation prompt when this coding increment is complete.
