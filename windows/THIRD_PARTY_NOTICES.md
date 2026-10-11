# Third-party notices

## hpack 1.5.1

The Windows HTTP/2 transport uses the NuGet package `hpack` 1.5.1 for HPACK response-header decoding.

- Project/package: https://www.nuget.org/packages/hpack/1.5.1
- License: Apache License 2.0
- Source repository: linked from the NuGet package page.

The FoxyVPN code in this project is not a replacement for a complete, independently audited HTTP/2 stack. Validate protocol interoperability and review dependency/license notices before production release.


## HEV socks5 tunnel 2.17.1 (native Windows tun2socks)

- Project: https://github.com/heiher/hev-socks5-tunnel
- Pinned source tag: `2.17.1` (the same upstream version pinned by the Android project)
- License: MIT. The CI artifact includes the upstream `LICENSE` as `HEV-LICENSE.txt`.
- Usage: the C API is dynamically loaded; no native binary is committed into this source archive.

## Wintun 0.14.1 prebuilt runtime

- Official distribution: https://www.wintun.net/builds/wintun-0.14.1.zip
- The published archive SHA-256 is checked in CI before extraction. CI also requires a valid Authenticode signature identifying WireGuard LLC.
- The official `prebuilt-binaries-license.txt` is shipped next to the application as `WINTUN-PREBUILT-LICENSE.txt`. The DLL is copied unmodified and is used only through the official Wintun-based HEV integration.
