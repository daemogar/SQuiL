# Contributing to SQuiL

Thanks for your interest in improving SQuiL! This guide covers how to build,
test, and work within the codebase.

## Prerequisites

- **.NET SDK 10.0** or later.
- Windows is required only for the editor-extension projects (SSMS / Visual
  Studio); the generator, runtime library, and tests build cross-platform.

## Repository layout

| Project | Purpose |
|---|---|
| `SQuiL.SourceGenerator` | The incremental Roslyn generator (tokenizer → parser → emitter). Also the published NuGet package, which bundles the runtime library. |
| `SQuiL.Library` | Runtime types used by generated code (`SQuiLBaseDataContext`, attributes, exceptions). Targets `netstandard2.0`. |
| `SQuiL.Tests` | Snapshot tests of the generator (SGSnap). |
| `SQuiL.Simple` | A runnable example application. |
| `SQuiL.VSCodeExtension` | VS Code editor support for `.squil` files. |
| `SQuiL.SsmsExtension` | SSMS 22.6 editor support. |
| `SQuiL.VisualStudioExtension` | Visual Studio 2026 editor support. |
| `SQuiL.Editor.Shared` | Canonical grammar / language-config / writing-guide shared by the editors. |

## Architecture

SQuiL is a three-stage incremental Roslyn generator:

1. **Tokenization** (`SQuiL.SourceGenerator/SQuiL/Tokenizer/SQuiLTokenizer.cs`)
   turns SQL text into tokens. Token kinds are dialect-neutral (`TYPE_INT`,
   `TYPE_STRING`, …); the SQL-Server-specific C# type mapping lives in
   `Token.cs`.
2. **Parsing** (`SQuiL.SourceGenerator/SQuiL/Parser/SQuiLParser.cs`) turns tokens
   into `CodeBlock`s, using the `@Param*` / `@Return*` naming patterns and the
   `USE` statement to determine each variable's input/output role.
3. **Code generation** (`SQuiL.SourceGenerator/SQuiL/Generator/SQuiLGenerator.cs`)
   is an `IIncrementalGenerator` that emits the request/response models, the
   data-context method, and the DI extensions from the parsed `CodeBlock`s.

At runtime, generated contexts derive from `SQuiLBaseDataContext`
(`SQuiL.Library/SQuiLBaseDataContext.cs`), which provides the ADO.NET
connection and parameter helpers. A query executes as a single batch: declare
the table variables, expand list parameters inline, run the body, then select
each return set.

The editor extensions (VS Code, SSMS, Visual Studio) share one canonical
grammar, language configuration, and writing guide from `SQuiL.Editor.Shared`;
each extension's copies are regenerated from there at build time.

## Build

```bash
dotnet restore
dotnet build SQuiL.SourceGenerator/SQuiL.SourceGenerator.csproj -c Release
dotnet build SQuiL.sln            # whole solution
```

## Test

The test suite uses [SGSnap](https://github.com/daemogar/sgsnap) snapshot
tests: each test runs the generator over a SQL/C# input and compares the output
against committed `.verified.cs` / `.verified.txt` snapshots.

```bash
dotnet test SQuiL.Tests/SQuiL.Tests.csproj
dotnet test SQuiL.Tests/SQuiL.Tests.csproj --filter "FullyQualifiedName~BasicIODeclareTests"
```

When you intentionally change generator output, the affected snapshot tests will
fail with a diff and write a `.received.*` file beside each changed snapshot. To
accept the new output, rename each `.received.*` to `.verified.*` (the failure
message prints the exact command), or run the tests once with
`SGSNAP_ACCEPT=1` to accept every change, then review the diff before
committing. `.received.*` files are intermediate output and are git-ignored —
never commit them. `SGSNAP_CHECKONLY=1` compares without writing anything.

## Conventions

- **`ID` is always upper-case** when it stands alone or ends an identifier:
  `@Param_UserID`, `UserID`, `NewID()` — never `Id`.
- **Generated record naming:** table-valued variables produce `<Name>Table`
  records; single-object variables produce `<Name>Object` records.
- **Special variables (all opt-in):** the four input specials — `@Debug`,
  `@SuppressDebug`, `@EnvironmentName`, `@AsOfDate` — only affect the generated
  code when the SQL declares them; nothing is emitted implicitly. `@Debug` →
  `bool Debug` on the request when declared (the old always-on behavior and the
  `DebugOnly` property are gone). `@SuppressDebug` → `bool SuppressDebug` on the
  request; requires `@Debug` (else build error SP0019); replaces `DebugOnly`.
  `@AsOfDate` (declared bare) → a nullable typed request property (e.g. `date` →
  `System.DateOnly?`); null at the call site means current-time-at-execution.
  `@EnvironmentName` → sent as a SQL parameter when declared, not a property.
  (`@Error`/`@Errors` were removed — errors surface via `SQuiLResultType` only.)
- Match the surrounding code style (tabs for indentation; see `.editorconfig`).

## Packaging note (important)

SQuiL ships as `SQuiL.Core` plus one package per provider (`SQuiL.SqlServer`,
`SQuiL.Sqlite`, `SQuiL.Postgres`). `SQuiL.Core` packs the generator DLL (built
from `SQuiL.SourceGenerator`) into `analyzers/dotnet/cs` and its own runtime DLL
into `lib/`. Each provider package depends on `SQuiL.Core` through a normal
`ProjectReference`.

- Runtime `PackageReference`s on `SQuiL.Core` and the provider projects are real
  NuGet dependencies and flow to consumers on their own. There is nothing to
  mirror by hand.
- The analyzer is the one manual piece: `SQuiL.Core.csproj` packs
  `SQuiL.SourceGenerator.dll` with explicit `<None Pack="true" PackagePath="analyzers/dotnet/cs">`
  entries (the `.dll` and its `.pdb`). Update them if the generator's target
  framework or output path changes.
- The generator runs in every project downstream of a SQuiL reference, so
  everything it emits must be safe in a project that only references a SQuiL
  library. CLAUDE.md, "Package structure", has the details.

## License

By contributing, you agree that your contributions are licensed under the
project's **GNU Affero General Public License v3.0** (see [LICENSE.txt](LICENSE.txt)).
