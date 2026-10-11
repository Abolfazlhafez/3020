# FoxyVPN for Windows

FoxyVPN is a native Windows desktop application built with WPF and .NET 8.

> **Repository scope:** this repository now contains the Windows desktop project only. The Android application source, Gradle project files, APK build instructions, and Android GitHub Actions workflows have been removed.

## Current implementation

The Windows source includes a native desktop UI, Firefox Account sign-in and session handling, Mozilla Guardian API clients, DPAPI-protected local settings/session data, HTTP/2 CONNECT transport, a loopback-only SOCKS5 relay, DNS-only UDP relay logic, HEV/Wintun integration, and Windows route/DNS/firewall lifecycle management.

The project is still under active development. These source components are not, by themselves, proof of a working or safe production VPN. Do not treat the app as production-ready until the Windows build and automated tests pass and the complete tunnel, DNS, IPv4/IPv6 leak, route rollback, crash recovery, and Windows 10/11 lifecycle tests have been verified.

## Build locally

Requirements:

- Windows 10 or Windows 11, x64
- .NET 8 SDK or Visual Studio 2022 with the **.NET desktop development** workload
- Microsoft Edge WebView2 Evergreen Runtime for interactive account/service verification screens

From PowerShell at the repository root:

```powershell
dotnet restore .\windows\FoxyVPN.sln
dotnet build .\windows\FoxyVPN.sln --configuration Release
dotnet test .\windows\FoxyVPN.sln --configuration Release
```

The workflow `.github/workflows/windows-build.yml` builds the Windows app, builds the pinned native HEV tunnel library, verifies the Wintun download, runs tests, and packages the release installer.

## Download a CI package

Open **Actions → Windows desktop build**, select a successful run, then download the `FoxyVPN-Windows-x64-Release` artifact. The ZIP contains the Windows installer and a README.

The compact installer uses the .NET 8 Desktop Runtime already installed on the computer when available. If it is missing, the installer asks before downloading it from Microsoft's official endpoint.

## Privacy and local data

Session tokens and settings are protected with Windows DPAPI for the current Windows user. Application diagnostics are stored under `%LOCALAPPDATA%\FoxyVPN\logs`. Review diagnostic logs before sharing them.

## Credits and licensing

- [HEV SOCKS5 tunnel](https://github.com/heiher/hev-socks5-tunnel)
- [Wintun](https://www.wintun.net/)
- [Mozilla Firefox Accounts](https://accounts.firefox.com/)

This project is licensed under the MIT License. See [LICENSE](LICENSE).
