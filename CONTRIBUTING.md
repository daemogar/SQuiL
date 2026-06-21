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
| `SQuiL.Tests` | Verify snapshot tests of the generator. |
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

The test suite uses [Verify](https://github.com/VerifyTests/Verify) snapshot
tests: each test runs the generator over a SQL/C# input and compares the output
against a committed `.verified.cs` snapshot.

```bash
dotnet test SQuiL.Tests/SQuiL.Tests.csproj
dotnet test SQuiL.Tests/SQuiL.Tests.csproj --filter "FullyQualifiedName~BasicIODeclareTests"
```

When you intentionally change generator output, the affected snapshot tests will
fail with a diff. To accept the new output as the baseline, delete the relevant
`.verified.cs` files and re-run the tests (Verify writes the new snapshot), then
review the diff before committing. `.received.*` files are intermediate output
and are git-ignored — never commit them.

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

`SQuiL.SourceGenerator` is distributed as a single package that bundles the
`SQuiL.Library` runtime DLL. Because the library DLL is embedded rather than
referenced as a NuGet dependency, its transitive `PackageReference`s are **not**
resolved automatically for consumers.

**Rule:** any `PackageReference` added to `SQuiL.Library.csproj` that a consumer
needs at runtime (e.g. `Microsoft.Extensions.Configuration`) must be mirrored in
`SQuiL.SourceGenerator.csproj` so it flows through. Keep the two dependency
lists in sync.

## License

By contributing, you agree that your contributions are licensed under the
project's **GNU Affero General Public License v3.0** (see [LICENSE.txt](LICENSE.txt)).
