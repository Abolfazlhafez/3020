# Continuation prompt — FoxyVPN native Windows port (Phase 1.2 → Phase 1.3)

Continue directly from the attached `FoxyVPN-Android-and-Windows-Phase1.2.zip`. Do not recreate the project from scratch and do not discard any existing changes.

## Goal
Finish the native Windows desktop port of FoxyVPN. The current visual style is a modern, motion-led 2027 desktop control center implemented in native C#/.NET 8/WPF. Keep the design coherent and polish it further if useful, but never let animation conceal missing VPN functionality.

## Current source state
- Original Android Kotlin/Jetpack Compose project is kept in the repository.
- Native Windows solution: `windows/FoxyVPN.sln`.
- Project: `windows/FoxyVPN.Windows/FoxyVPN.Windows.csproj` targeting `net8.0-windows`, WPF enabled.
- Windows screens: Overview/Home, Locations, Firefox account, Account usage, Settings, Diagnostics.
- Source includes Firefox Accounts login/email verification/token handling, Mozilla live server catalogue retrieval, Guardian API client, DPAPI-protected Windows settings/session storage, favorite servers, TCP reachability check, timed server refresh, diagnostic ZIP export, Windows sign-in auto-start and start-minimized support.
- Phase 1.2 added page fade/slide transitions, animated hover feedback on buttons/cards, a home orbit/pulse/glow visual, dynamic light/dark/system resources, and a persisted `ReduceMotion` option. The home data-plane status stays explicitly offline.
- Project/installer version is 1.1.0.

## First tasks — do these before feature work
1. On Windows 10/11 or a Windows CI runner, run:
   `dotnet restore windows/FoxyVPN.sln`
   `dotnet build windows/FoxyVPN.sln --configuration Release --no-restore`
   `dotnet test windows/FoxyVPN.sln --configuration Release --no-build`
2. Fix all compile, XAML compiler, analyzer, and test errors. Record exact command output and exit codes; do not describe source-level validation as a successful build.
3. Launch the app and check 100%, 150%, and 200% DPI; resize/minimize/maximize; use keyboard-only navigation; test Light/Dark/System and Reduce motion; ensure animations stop when Home is not visible and ensure reduced motion turns off ambient loops and page sliding.
4. Run the existing GitHub Actions workflow and download the actual portable/installer artifacts. Do not state an EXE/installer exists unless the artifact was actually produced.

## Most important unfinished work
The Windows VPN data plane is **not implemented**. The existing connect button must continue to reject connection attempts truthfully. Do not simulate a connected state or route production traffic through incomplete components.

Inspect the Android implementation and map these elements individually:
- `VpnService` lifecycle, TUN setup and Android route rules
- `hev-socks5-tunnel` / native build, SOCKS5 TCP + UDP forwarding
- custom HTTP/2 transport/session multiplexing, backpressure, keepalive, retry and cancellation
- proxy-pass provisioning and refresh logic
- DNS resolver / DNS-over-HTTPS relay
- per-app/split-routing exclusions
- traffic counters, connection timer and external-IP/exit verification
- route restoration and fail-closed / kill-switch behavior
- permission, service, notification and shutdown semantics

Choose a supported Windows TUN adapter/driver and routing strategy only after reviewing dependencies, redistribution terms and Windows 10/11 compatibility. Encapsulate the data plane behind interfaces so the UI cannot fake connectivity. Implement connection states such as `Disconnected`, `Preparing`, `Connecting`, `Connected`, `Disconnecting`, `Failed`; enter `Connected` only after a real tunnel is established and a health check passes. Ensure route install is transactional and rolled back on failure/shutdown. Add unit tests plus real integration tests that verify traffic traverses the tunnel and that routes recover after disconnect or crash.

## Design / motion constraints
- Keep native WPF controls; do not convert the app into an emulator or WebView wrapper.
- Keep the fast, tasteful 2027 visual direction: dark-space hero, restrained neon accents, orbital/pulse animation, smooth eased navigation and interactive card depth.
- Avoid constant heavy blur, excessive motion, or animations that imply security states not verified by the engine.
- Respect the saved Reduce motion preference, keyboard focus, contrast, DPI and system theme.
- Keep motion decorative and isolate it from networking state.

## Release requirements
- Preserve the Android project unless absolutely necessary and explain changes.
- Keep a checklist in `windows/MIGRATION_REPORT.md`, marking tasks complete only after actual verification.
- Update `windows/README_WINDOWS.md`, migration report, CI workflow and continuation prompt each phase.
- Produce an updated full-project ZIP and Windows-source ZIP, with a phase report and a fresh continuation prompt.
- Include standalone portable publish output and installer only when a Windows build actually creates them.
- State exactly what was built/tested and list limitations. Final acceptance requires a successful Windows build, real launch, passing tests, and end-to-end VPN traffic verification; UI polish alone does not meet acceptance.

## Current known blocker
The authoring environment that produced Phase 1.2 did not contain `dotnet`, MSBuild, or a Windows WPF runtime. Thus build/launch/test and animation performance are not yet confirmed. The VPN tunnel/traffic-routing implementation also remains outstanding.
