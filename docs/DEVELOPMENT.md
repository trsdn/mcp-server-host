# Development

## Prerequisites

- Windows (the providers automate a local emulator; there is no cross-platform path)
- .NET SDK 9.0.x — the version is pinned in `global.json` with `rollForward: latestFeature`
- No emulator needed to build, test or develop

```powershell
dotnet build HostMcp.sln
dotnet test HostMcp.sln
```

Both are green on a machine with no emulator installed. That is a hard requirement, not a
coincidence — see [Testing](#testing).

## Layout

```
HostMcp.sln
Directory.Build.props        # shared compiler + analyzer settings, HostMcpPlatform switch
Directory.Packages.props     # central package version management
global.json                  # pinned SDK
src/
  HostMcp.Core/              # net9.0        abstraction, model, services  (no COM, no P/Invoke)
  HostMcp.ComInterop/        # net9.0-windows PCOMM + EHLLAPI providers
  HostMcp.Generators.Mcp/    # netstandard2.0 Roslyn source generator
  HostMcp.McpServer/         # net9.0-windows stdio MCP server
  HostMcp.CLI/               # net9.0-windows hostmcp CLI
tests/
  HostMcp.Core.Tests/
  HostMcp.ComInterop.Tests/
  HostMcp.McpServer.Tests/
docs/
```

`HostMcp.Core` deliberately has **no dependency on COM or Win32**. Everything that touches an
emulator lives behind `ITerminalProvider` / `ITerminalSession` in `HostMcp.ComInterop`. That split
is what makes the core testable on any runner.

## Adding a tool

The tool surface is generated. Do **not** hand-write MCP tool classes.

1. Add a method to `IHostTerminalService` with a `[Description]` on the method and on every
   parameter. The descriptions are the schema the model sees, so write them for a reader who has
   never seen a 3270 screen.
2. Implement it in `HostTerminalService`. Route anything that changes host state through
   `ReadySynchronizer` — never poll the OIA in a tool.
3. Add a result record to `ToolResults.cs` if the shape is new.
4. Build. The generator emits the `[McpServerTool]` wrapper with a `snake_case` name derived from
   the method name.
5. Document it in `docs/TOOLS.md`.

`HostMcp.McpServer.Tests` asserts that the generated tool count matches the interface and that every
tool and parameter carries a description, so a missing `[Description]` fails the build's test step
rather than shipping a blank schema.

### How the generator works

`src/HostMcp.Generators.Mcp/McpToolGenerator.cs` looks for an interface annotated with
`[McpTool(className, accessorExpression)]`, then emits one static method per interface method that:

- converts the method name to `snake_case`
- copies `[Description]` attributes onto the generated method and parameters
- calls `ToolInvoker.InvokeAsync(name, () => accessor.Method(...))`, which serializes the result or
  renders a structured error

The generated source is not written to disk by default. To inspect it:

```xml
<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
<CompilerGeneratedFilesOutputPath>generated</CompilerGeneratedFilesOutputPath>
```

The generator project must stay `netstandard2.0` and must **not** get a `PlatformTarget` — see
`Directory.Build.props`.

## Testing

Three rules:

1. **No test may require an installed emulator.** CI runs on a bare `windows-latest` runner.
2. Terminal behaviour is tested against `FakeTerminalProvider`, an in-memory presentation space
   that lives in `HostMcp.Core` (not in the test project) so the CLI and demos can use it too.
3. Provider tests assert the *unavailable* path: probing must return a clear reason instead of
   throwing.

Coverage today:

| Area | Where |
| --- | --- |
| Mnemonic parsing, AID detection, round-tripping | `MnemonicParserTests` |
| 1-based ↔ linear offset mapping, bounds | `CoordinateMapperTests` |
| Screen model, field extraction, hidden-field masking | `ScreenModelTests` |
| Central OIA polling, timeout, interval | `ReadySynchronizerTests` |
| In-memory provider semantics | `FakeTerminalProviderTests` |
| Every tool including `batch` | `HostTerminalServiceTests` |
| EHLLAPI escape table, provider probing, ProgIDs, function numbers | `ProviderTests`, `EhllapiMnemonicsTests` |
| Generated tool surface | `GeneratedToolTests` |

```powershell
dotnet test HostMcp.sln
dotnet test tests/HostMcp.Core.Tests --filter FullyQualifiedName~MnemonicParser
```

## Manual verification against a real emulator

The providers are written against the documented PCOMM and EHLLAPI APIs but have not been run
against a live host. When you first try them:

```powershell
dotnet publish src/HostMcp.CLI -c Release -p:HostMcpPlatform=x86 -o out/cli
out/cli/hostmcp.exe providers      # which provider is detected, and why not the others
out/cli/hostmcp.exe sessions
out/cli/hostmcp.exe screen A
out/cli/hostmcp.exe send A "[pf3]"
```

`providers` is the diagnostic to run first: it prints each provider's availability and the exact
reason a probe failed, which is almost always a bitness problem
([docs/PLATFORM.md](PLATFORM.md)).

Because everything is late-bound, mistakes in COM member names surface only here, at runtime. Fix
them in `PcommTerminalSession` / `EhllapiTerminalSession`; nothing else needs to change.

## Code style

`.editorconfig` is authoritative and enforced at build time: `TreatWarningsAsErrors`,
`EnforceCodeStyleInBuild`, `CodeAnalysisTreatWarningsAsErrors`, and the CodeQuality, Performance,
Globalization and Security analyzer categories escalated to error. In practice:

- `StringComparison` and `CultureInfo` must be explicit — use
  `string.Create(CultureInfo.InvariantCulture, $"...")` instead of plain interpolation in messages
- every public member needs an XML doc comment (`GenerateDocumentationFile` is on everywhere)
- unused usings are errors (IDE0005)

Globally suppressed warnings and the reason for each are listed in `Directory.Build.props`.

Comment sparingly, and only to explain *why*. The domain has enough genuinely surprising behaviour
(OIA timing, EHLLAPI's process-global presentation space, PCOMM's bitness) that those comments earn
their place; restating what the code does does not.

## CI

`.github/workflows/ci.yml` on `windows-latest`:

1. restore, build `Release`, `dotnet test` the whole solution
2. publish the MCP server and the CLI for **both** `x86` and `x64` and upload them as artifacts

The publish matrix is not decoration: the x86 path is the one most users need, and it is easy to
break with a stray `PlatformTarget`.

## Security

This repository is public and must never contain host names, LU names, session profiles (`.ws`),
user IDs, or credentials — not in code, not in tests, not in fixtures, not in documentation
examples. Use placeholders such as `USER01` and `USER.DATA(MEMBER)`.
