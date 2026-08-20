# HostMcp.Core

Emulator independent core library for IBM 3270/5250 host terminal automation.

Contains:

- `ITerminalSession` / `ITerminalProvider` — the emulator abstraction.
- `ScreenModel`, `ScreenField`, `OiaStatus`, `CoordinateMapper` — the structured screen model with
  1-based row/column coordinates, as used by EHLLAPI and PCOMM.
- `MnemonicParser` — parses keystroke strings such as `USER01[tab]SECRET[enter]`.
- `ReadySynchronizer` — the single place where host readiness (OIA keyboard lock) is awaited.
- `FakeTerminalProvider` — an in-memory terminal so tests and CI run without an emulator.
- `IHostTerminalService` — the single source of truth for the MCP tool surface.

See the [repository README](https://github.com/trsdn/mcp-server-host) for the full picture.
