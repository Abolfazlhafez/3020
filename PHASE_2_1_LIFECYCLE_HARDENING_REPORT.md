# FoxyVPN Phase 2.1 — Tunnel lifecycle hardening

**Application source version:** 2.1.0  
**Scope:** coding/integration hardening. Comprehensive tests intentionally deferred by the project owner.

## Changes in this increment

### HTTP/2 CONNECT half-close semantics

`WindowsHttp2ConnectSession.SendDataAsync` no longer rejects writes merely because the peer has ended its response direction (`END_STREAM`). HTTP/2 directions are independently half-closed: client data can continue until the local direction is ended. Previously the check treated `RemoteEnded` as a reason to block client writes, potentially truncating valid TCP half-close workflows carried by the SOCKS5 relay. Local `END_STREAM` still prevents additional writes.

### Tunnel safety hold and recovery state

`IWindowsTunnelRuntime` now exposes a default `IsSafetyHold` property so runtimes can signal that route/firewall ownership or rollback cannot be proven safe. `WindowsTunnelEngine.State` presents `Faulted` during a safety hold even if the raw engine enum is still `Disconnected`. New connection attempts are rejected until recovery; explicit Disconnect remains available to call runtime cleanup when data-plane connectivity has already stopped.

When startup fails, the engine calls runtime cleanup without caller cancellation and checks the result. A cancelled startup is considered `Disconnected` only if the runtime confirms it is not connected and not in safety hold. If cleanup fails, the original startup exception is preserved while state stays `Faulted`.

### Idempotent async disposal

`WindowsTunnelEngine` and `WindowsSystemTunnelRuntime` now share a single asynchronous disposal task across multiple callers. This prevents simultaneous dispose calls from racing cleanup and avoids disposing synchronization primitives while waiters or event-driven callbacks may still unwind. Event subscriptions are removed and environment monitoring cancelled; small managed gates are left to be collected with the objects rather than racing late continuations.

### Test source

Added/updated xUnit source for: startup cleanup failure remaining faulted; explicit recovery from safety hold while raw engine state is disconnected; rejecting a new Connect while held; cancellation with verified teardown; and concurrent DisposeAsync callers waiting for the same cleanup task. These tests are source-authored and have **not** been executed.

## Static source/package checks completed

- Parsed 31 XML/XAML/project/manifest files: no parse errors.
- Verified both project paths listed in `windows/FoxyVPN.sln` exist.
- Checked 30 XAML event-handler references: no missing code-behind method names found.
- Ran a lexer-based delimiter scan over 55 C# files: no unmatched braces, parentheses, or brackets were detected. This is a structural heuristic, not a compiler.
- Parsed `.github/workflows/windows-build.yml` and confirmed its `build` job is present.
- Checked project resource paths: no missing explicit icon/manifest/resource files found.
- Confirmed project, Inno Setup and CI installer metadata all reference version `2.1.0`.
- Corrected the fake runtime's `async Task StartAsync` test helper to use a bare `return` path rather than returning `Task.CompletedTask` from an async method.

These checks do not validate C# type binding, package resolution, runtime behavior, native ABI, or network interoperability.

## Not verified / remaining

- `dotnet restore`, `dotnet build`, and `dotnet test` have not run. The authoring environment is Linux and does not have the .NET SDK/MSBuild.
- HEV/Wintun native build/load and x64 ABI not verified.
- No live Firefox/Guardian connection or HTTP/2 edge tunnel verified.
- Full-system route/DNS/Firewall, rollback and IPv4/IPv6/DNS leak behavior remain to be checked on Windows 10/11.
- General UDP/QUIC remains unsupported with a TCP-CONNECT-only edge. DNS-only UDP relay is source-authored but not live-verified. Per-application split tunneling remains unavailable.
- No portable Windows binary or installer was generated in this environment.

## Next coding stage

Continue with `CONTINUATION_PROMPT_PHASE_2_1.md` toward Phase 2.2. Review HTTP/2 state transitions and protocol conformance, native runtime lifecycle, DNS relay behavior, route journal rollback and shutdown recovery. Defer the comprehensive test matrix until that coding checklist is complete.
