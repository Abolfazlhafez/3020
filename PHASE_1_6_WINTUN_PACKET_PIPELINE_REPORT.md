# FoxyVPN Phase 1.6 — Wintun packet pipeline foundation

## What changed

- Added `WindowsWintunPacketDevice`: dynamically binds to an app-side `wintun.dll`; uses the official open/create adapter, start/end session, packet receive/release, allocation/send and read-wait-event APIs; serializes lifecycle, validates packet sizes, and avoids freeing native handles if the receive loop does not stop safely.
- Added `WindowsIpPacketInspector`: bounds-checked IPv4/IPv6 packet parser with protocol/port metadata, IPv6 extension-header walk, fragment handling, and DNS port detection. It does not mutate or forward packets.
- Added `IWindowsTunPacketProcessor` and `WindowsTunPacketPipeline`: the contract needed to connect raw packets to a userspace TCP/UDP/DNS stack; validates packets in both directions and rolls back partially started components.
- Added unit-test source for packet parsing, malformed/truncated packets, DNS port detection, and pipeline startup/stop/rollback.
- Bumped Windows app/installer/CI version metadata to `1.6.0`.

## What this does not mean

The TUN pipeline has no production packet processor. No full TCP/IP stack, TCP state machine, UDP relay, fake DNS mapping, DNS-over-HTTPS, Wintun interface addressing, system routing, endpoint-route bypass, kill-switch, or route/DNS rollback is implemented. Wintun DLL is not bundled and its Authenticode signature is not verified by this code. No Windows binary or installer was built here.

The native tunnel engine remains unregistered, so the UI must not report system VPN connected. Proxy-only mode remains a separate feature and must not be described as system-wide VPN.

## Static checks performed

- Source files and unit-test files were added to the existing SDK-style projects (automatic compile inclusion).
- Windows app and Inno Setup CI version metadata changed to `1.6.0`.
- The existing Windows CI workflow still runs restore, Release build, tests, self-contained publish and Inno Setup installer build.
- XML/XAML/project/workflow files are checked separately before packaging.

## Not verified in this environment

`.NET SDK`/MSBuild are not installed, so C# compilation, test execution, Wintun DLL loading, driver/adapter operations, route behavior and live network tests remain unverified.

## Next critical milestones

1. Run Windows CI and correct all compiler/test failures.
2. Decide whether to port/pin HEV `tun2socks` for Windows or use another compatible L3 userspace stack. Preserve the Android behavior: `10.8.0.2/32`, MTU 8500, MAPDNS `198.18.0.2` with synthetic range `100.64.0.0/10` when custom DNS is not configured, TCP and UDP SOCKS relay, upstream H2 streams, proxy-pass renewal, reconnect/failover, IPv6 fail-closed semantics and app exclusions.
3. Add Windows interface configuration and a route/DNS transaction only after the packet processor can forward real traffic. Always install/retain a host route to the selected edge outside the tunnel; record exactly which routes were created; rollback them in reverse order on every failure or disconnect. Never flush the whole routing table.
4. Add a traffic-leak test matrix (DNS, IPv4, IPv6, sleep/wake, adapter loss, upstream loss, app exit, failed startup) before setting a connected state.
