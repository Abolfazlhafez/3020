<div align="center">

# FoxyVPN - Unofficial Firefox VPN client

<p align="center">
  <img src="https://i.ibb.co/8D9KdmNk/6a07263d16c0.png" alt="FoxyVPN Logo" width="150"/>
</p>

![Android](https://img.shields.io/badge/Android-212121?style=for-the-badge&logo=android&logoColor=white)
![Kotlin](https://img.shields.io/badge/Kotlin-212121?style=for-the-badge&logo=kotlin&logoColor=white)
![Compose](https://img.shields.io/badge/Jetpack%20Compose-212121?style=for-the-badge&logo=android&logoColor=white)
![License](https://img.shields.io/badge/License_MIT-212121?style=for-the-badge)


*FoxyVPN signs in with a Firefox account, obtains a proxy pass from Mozilla's
Guardian service, and carries the whole device's traffic through Firefox VPN's
Fastly edge over an HTTP/2 tunnel. It is a native Kotlin app with a
Jetpack Compose (Material 3) UI.*

</div>

<br>


## Windows desktop port (Phase 2.2 — HTTP/2 protocol hardening)

A native WPF/.NET 8 desktop application lives under [`windows/`](windows/), source version **2.2.0**. It preserves the Android reference project and includes the motion-led 2027 dashboard, Reduce Motion, Windows auto-start and connect-on-launch options, Firefox Account/Guardian clients, protected local state, HTTP/2 CONNECT, loopback SOCKS5, DNS-only UDP relay, optional upstream HTTP CONNECT/SOCKS5 chaining, and the pinned HEV/Wintun native build workflow. The system-tunnel source orchestrates HTTP/2 edge → localhost SOCKS5 → HEV/Wintun → journaled IPv4/IPv6 routes, DNS NRPT, and a persistent Windows Firewall egress guard. Network/suspend lifecycle monitoring, proxy-pass renewal, stable interface identity, cross-process route serialization and rollback journal are present in source.

Phase 1.9 introduced a **user-mediated** WebView2 verification window for Firefox Accounts/Guardian when an API reports HTTP 406. Eligible secure cookies are validated against an explicit service allow-list before being imported into the matching API client's in-memory `CookieContainer`, and the failed operation is retried at most once. This is not an automatic challenge solver or bypass. The token-refresh and connect-on-launch paths now use the same explicit one-retry interaction. Per-application split tunneling is still unavailable and is labelled as such rather than exposed as a misleading toggle.

**Important:** this is still an unverified source milestone, not a verified production VPN. This authoring environment has no .NET SDK/MSBuild, so managed build/tests and the native HEV Windows build have not run here. No successful live connection, no-leak result, or installer binary is claimed. The current edge protocol uses TCP CONNECT only; general UDP/QUIC remains unsupported, and DNS-only UDP relay is source-authored but not live-verified. See [`windows/MIGRATION_REPORT.md`](windows/MIGRATION_REPORT.md), [`PHASE_1_9_INTERACTIVE_CHALLENGE_AND_STARTUP_REPORT.md`](PHASE_1_9_INTERACTIVE_CHALLENGE_AND_STARTUP_REPORT.md), and [`CONTINUATION_PROMPT_PHASE_1_9.md`](CONTINUATION_PROMPT_PHASE_1_9.md). The Android app remains the functional reference implementation.

## Phase 2.2 current coding increment

- HTTP/2 client stream allocation and HEADERS emission are serialized so concurrent opens cannot send stream initiations out of numeric order. Stream ID arithmetic uses `long` and safely drains rather than wrapping at exhaustion.
- A frame that has started writing is not interrupted by a request-level cancellation token. A bounded write timeout aborts the entire shared session on a stalled/partial frame instead of reusing a potentially corrupted transport.
- GOAWAY IDs are monotonic; idle stream IDs in RST_STREAM/WINDOW_UPDATE/DATA/HEADERS are handled strictly, while late data/headers on closed client streams follow dedicated close paths.
- Response header decoding now caps decoded bytes and field count and validates pseudo-header ordering, lowercase/token names, control bytes, duplicate status and three-digit status syntax.
- Added test source for these pure protocol rules. Per the project owner's workflow, comprehensive tests remain deferred until coding is complete.

These are source changes only. No `dotnet restore/build/test`, native HEV build, live VPN connection, route recovery test, DNS/IPv4/IPv6 leak test, or installer generation was executed in this increment. See `PHASE_2_2_HTTP2_PROTOCOL_HARDENING_REPORT.md` and `CONTINUATION_PROMPT_PHASE_2_2.md`.

## Quick Settings tile

FoxyVPN includes an Android Quick Settings tile. After installing the app, open the notification shade, tap **Edit** (the pencil icon), and add **FoxyVPN** to your Quick Settings tiles. Tap it to connect or disconnect without opening the app after Android's VPN permission has been granted. The first connection may open FoxyVPN so Android can show its required VPN-consent screen. The tile has three distinct looks: **Off** (grey, crossed-out shield), **Connecting** (grey, animated shield whose dots fill in; tap to cancel) and **On** (coloured, shield with a check and the country name). A failed attempt shows "Failed · tap to retry"; when signed out it shows "Sign in to connect" and opens the app.

## 🔎 Features

- **System-wide VPN**
- **Firefox account sign-in**
- **Multiple Servers**
- **Split tunneling**
- **Private DNS by default**
- **Encrypted DNS (DNS over HTTPS)**
- **Upstream proxy chaining**
- **Proxy-only mode**
- **Dark / light / follow-system theme (Settings > Appearance)**
- **Connect on launch**
- **Exit verification**
- **In-app logs**

<br>

## 📸 Screenshots
<div align="center">
  <img src="https://i.ibb.co/hx3Gh7d9/91728322b4ad.jpg" alt="Screenshot 1" width="30%">
  <img src="https://i.ibb.co/zVFc07CD/b6b8e29ec4ac.jpg" alt="Screenshot 2" width="30%">
  <img src="https://i.ibb.co/ZzP5mgfq/dc50132fb3cb.jpg" alt="Screenshot 3" width="30%">
</div>

<br>

## 🚀 How to use?

1. **Get a Firefox account.** If you do not have one, create it for free at
   [accounts.firefox.com](https://accounts.firefox.com/signup).
2. **Install the APK** from the [Releases](https://github.com/vauth/foxyvpn/releases)
   page.
3. **Sign in** with that account's email and password.
4. **Pick a location** and tap the power button.

> [!NOTE]
> **No subscription is required.** FoxyVPN runs on the free **50 GB of monthly
VPN traffic Mozilla includes with a Firefox account** for browser users, which
is the same allowance the Firefox browser's built-in VPN uses. Once the
allowance is used up for the month, connections stop working until it resets.

<br>

## 🗃 Requirements

- Android 8.0 (API 26) or newer
- JDK 17
- Android SDK 35 and NDK `26.1.10909125`
- `git` on `PATH`

<br>

## ⚙️ Build
* #### Clone Repository:
```bash
git clone https://github.com/vauth/foxyvpn.git
cd foxyvpn
   ```
* #### Build and Install:
```bash
gradle :app:assembleRelease --no-daemon
```

<br>

### 📝 To-Do List
The project is on test phase so not much yet.
- [x] Improve stability.
- [x] Better Logo.
- [ ] HTTP/3 implementation (Not Planned Yet).

<br>

## ✍️ Acknowledgements

- [firefox-vpn-client](https://github.com/UjuiUjuMandan/firefox-vpn-client) - the Go reference client this app is a port of
- [hev-socks5-tunnel](https://github.com/heiher/hev-socks5-tunnel) - the native tun2socks engine
- [Conscrypt](https://github.com/google/conscrypt) - ALPN-capable TLS on every supported API level

<br>

## 🛠 Contributing
Contributions are welcome! Feel free to submit a pull request or report an issue.

<br>

## 📄 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Design: simple, iOS-inspired

The UI is a flat, minimal system modelled on iOS rather than a decorative one: a grouped background with white (light) or near-black (dark) cards, inset grouped lists, large titles, coloured icon tiles, green switches, a segmented control, and iOS-style push/pop screen transitions. There are no gradients, blobs or blur; the one accent is system blue, with green / orange / red reserved for connection state.

The building blocks live in `ui/theme/IosKit.kt` (`IosScreen`, `IosSection`, `IosRow`, `IosSwitchRow`, `IosSegmented`, `IosButton`, `IosTextField`); colours are in `Color.kt`, type scale in `Type.kt`. Dynamic Material You colours are intentionally off so the app keeps one identity.

The look is an Android Compose interpretation of iOS conventions; it does not embed any Apple assets.

### New options

- **Appearance** (Settings): System / Light / Dark. System follows Android, including its day/night schedule.
- **Connect on launch** (Settings): starts a connection when the app is opened fresh (not on rotation, and not when the Quick Settings tile already asked to connect). Only runs when a signed-in session exists.
- **Confirm before disconnecting** (Settings > General): asks "Disconnect?" when you tap the power button while connected. Cancelling a connect in progress is always instant.
- **Haptic feedback** (Settings > General): turns the power-button vibration on or off (on by default).
- **Data this month** card on Home: remaining allowance with a progress bar (turns red above 90% used). Hidden if the allowance cannot be loaded.
- **Connection timer** on Home: shows how long the current connection has been up (h:mm:ss), kept in `FoxyVpnService.connectedSinceMs`.
- Power button springs slightly on press (no ripple, like iOS); the notification's collapsed line now includes live speed.


## Phase 2.0 current coding increment

- Upstream HTTP CONNECT and SOCKS5 handshake entry points are now internal test seams rather than private inline-only methods. This preserves the same production call path while allowing deterministic protocol tests without network access.
- Added scripted-stream tests for HTTP CONNECT Basic authentication, strict successful-status handling, IPv6 CONNECT authority formatting, SOCKS5 no-auth handshake, RFC 1929 username/password negotiation, empty-password rejection, incorrect method selection, and upstream CONNECT failures. Tests are authored but intentionally deferred until the final validation stage, per the project workflow.
- SOCKS5 credential byte arrays are zeroed in `finally` so cancellation/I/O failures do not bypass cleanup. RFC 1929 username and password are both required to be 1–255 UTF-8 bytes.
- Current application metadata is `2.0.0`. This phase does not claim that upstream proxy handshakes, HTTP/2 interoperability, HEV/Wintun, route rollback, DNS leak protection or the installer have been executed.
