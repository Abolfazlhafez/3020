# FoxyVPN Windows — Phase 1.2 Motion Design Report

## What changed

- Version set to **1.1.0** in the Windows project and installer workflow.
- Refreshed the native WPF workspace into a polished 2027-inspired control center: grouped navigation, compact status chip, large dashboard title, gradient hero and card-based information hierarchy.
- Added a home privacy-core visual with pulse rings, slow orbit motion, and a soft ambient glow. Animation loops run only while Home is loaded and are stopped on unload.
- Added eased fade/slide transitions between sections.
- Added subtle button scale/press response and card hover elevation/accent outline.
- Added persisted **Settings → General → Reduce motion** option. Enabling it skips page slides and ambient animation loops.
- Kept the visible “Windows tunnel engine pending” state. Animations never imply that a real VPN connection exists.
- Updated Inno Setup default version and the Windows CI installer version argument to 1.1.0.

## Files updated

- `windows/FoxyVPN.Windows/App.xaml`
- `windows/FoxyVPN.Windows/MainWindow.xaml`
- `windows/FoxyVPN.Windows/MainWindow.xaml.cs`
- `windows/FoxyVPN.Windows/Views/HomeView.xaml`
- `windows/FoxyVPN.Windows/Views/HomeView.xaml.cs`
- `windows/FoxyVPN.Windows/Views/SettingsView.xaml`
- `windows/FoxyVPN.Windows/Views/SettingsView.xaml.cs`
- `windows/FoxyVPN.Windows/Models/AppSettings.cs`
- `windows/FoxyVPN.Windows/Services/ThemeManager.cs`
- `windows/FoxyVPN.Windows/FoxyVPN.Windows.csproj`
- `windows/installer/FoxyVPN.iss`
- `.github/workflows/windows-build.yml`

## Validation status

- XML well-formedness for WPF XAML / project / installer-adjacent XML files: checked in the authoring environment.
- XAML event-handler names and static-resource keys: source-level cross-check performed.
- ZIP content and manifest paths: checked after packaging.
- `dotnet build`, automated tests, actual WPF launch, DPI checks, light/dark screenshots, and frame-time measurements: **not run** because this environment does not provide the .NET SDK/MSBuild/Windows WPF runtime.
- Real VPN data plane: **still not implemented**. The current Windows build is not a working VPN tunnel.

## Next phase priority

1. Build the solution and run all tests on Windows / Windows CI; repair any compiler or XAML compile errors first.
2. Validate animation in light/dark themes, resizing, 100–200% DPI, keyboard/focus, and Reduce motion. Check that views stop all animation on unload and do not leak timers/event handlers.
3. Map the Android transport stack in detail: `VpnService`, TUN/tun2socks, custom HTTP/2 session, DNS relay/DoH, proxy-pass refresh, route handling, and fail-closed behavior.
4. Choose a supported Windows TUN approach and implement a real isolated data plane with safe route install/rollback, kill-switch behavior, cancellation, retries, and integration tests. Do not claim VPN parity until end-to-end traffic tests pass.
5. Produce and test a self-contained portable build and installer through the Windows CI artifact output.
