# Copilot instructions for mcp-server-host

MCP server that automates IBM 3270/5250 terminal emulators on Windows through PCOMM COM/OLE
automation (primary) and EHLLAPI P/Invoke (fallback).

## Ground rules

- **This repository is public.** Never commit host names, LU names, session profiles (`.ws`), user
  IDs, passwords or any site-specific configuration — not in code, tests, fixtures or docs. Use
  placeholders like `USER01`, `USER.DATA(MEMBER)`.
- **Windows-only by design.** Do not add cross-platform shims for the providers.
- **No test may require an installed emulator.** CI is a bare `windows-latest` runner. Test
  terminal behaviour against `FakeTerminalProvider`; test real providers only on their
  "unavailable" path.
- `dotnet build HostMcp.sln` and `dotnet test HostMcp.sln` must stay green.

## Architecture rules

- `HostMcp.Core` must not reference COM or Win32. Everything emulator-specific lives behind
  `ITerminalProvider` / `ITerminalSession` in `HostMcp.ComInterop`.
- **`IHostTerminalService` is the single source of truth for the MCP tool surface.** Tools are
  generated from it by `HostMcp.Generators.Mcp`. Never hand-write an MCP tool class.
- Every interface method and parameter needs a `[Description]`; it becomes the schema the model
  sees. A test enforces this.
- **Never poll the OIA in a tool.** Host readiness is handled centrally by `ReadySynchronizer`.
  Route any state-changing operation through it.
- Providers must be constructible without side effects. COM objects and native libraries may only
  be touched inside `IsAvailableAsync` / `ConnectAsync`, so the server starts on a machine with no
  emulator.
- A missing emulator is a tool result, not a crash: it surfaces as
  `{"success": false, "error_kind": "no_provider"}` via `ToolInvoker`.

## Build conventions

- .NET 9, C# 13, nullable enabled, central package management (`Directory.Packages.props`).
- `TreatWarningsAsErrors` plus analyzer categories escalated to error in `.editorconfig`. Explicit
  `StringComparison` / `CultureInfo` everywhere; XML docs on all public members; no unused usings.
- Bitness is controlled by `-p:HostMcpPlatform=x86|x64`. The source generator project is excluded
  from it (analyzers load into the 64-bit compiler).
- Add new package versions to `Directory.Packages.props`, never inline in a `.csproj`.

## Comments

Explain *why*, not *what*. The domain has real surprises worth documenting — OIA timing, EHLLAPI's
process-global presentation space, PCOMM's 32-bit COM registration. Restating the code is noise.
