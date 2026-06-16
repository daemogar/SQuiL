# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SQuiL is a C# source generator that converts SQL files into strongly-typed C# code. It processes SQL files with specific naming conventions for variables and generates:
- Data context classes for executing queries
- Request/Response models based on SQL parameters
- Dependency injection extensions
- Enum types for queries and tables

## Editor extensions (VS Code + SSMS 22.6)

This repo ships two editor extensions that provide syntax highlighting and a
writing guide for SQuiL `.squil` files. They share canonical editor assets
via a third folder:

```
SQuiL/
├── SQuiL.Editor.Shared/             ← CANONICAL grammar, language config, guide.html
│   ├── squil.tmLanguage.json
│   ├── language-configuration.json
│   ├── guide.html                   ← SQuiL writing guide TEMPLATE (#if markers), rendered per host, CSS-fallback'd for all IDEs
│   └── README.md
│
├── SQuiL.VSCodeExtension/           ← TypeScript / vscode API
│   ├── src/                         ← all the providers (completion, diagnostics, hover, preview…)
│   ├── scripts/sync-shared.js       ← copies from Editor.Shared into syntaxes/ and root
│   └── package.json                 ← scripts run sync-shared before compile/package
│
└── SQuiL.SsmsExtension/             ← C# / VS SDK 17.x / WebView2 (SSMS 22.6 — VS 2026 shell)
    ├── ContentType/                 ← .squil → "SQL" mapping helper (IsSquilBuffer)
    ├── Classification/              ← syntax overlay (20 SQuiL-specific scopes)
    ├── Completion/                  ← IntelliSense, @-trigger filter, Ctrl+Space backup
    ├── QuickInfo/                   ← async hover info
    ├── Tagging/                     ← IErrorTag squigglies (parser diagnostics + lints)
    ├── Commands/                    ← Preview Generated C#, Build SQuiL Project, Open Guide, Insert Sample Data
    ├── Preview/                     ← C# preview generator
    ├── Parsing/                     ← parser, linter, SQL→C# type map
    ├── SampleData/                  ← INSERT-block generator + row-count dialog
    ├── Guide/                       ← WebView2 tool window
    ├── VSPackage/                   ← VSCT + resx (commands, menu placements)
    ├── Resources/                   ← icons, writing guide, LICENSE
    ├── SQuiL.Bindings.pkgdef        ← binds .squil to SSMS's SQL Query Editor factory
    ├── SQuiLPackage.cs              ← AsyncPackage, background load
    └── source.extension.vsixmanifest ← targets Microsoft.VisualStudio.Ssms [22.0,) amd64
```

### SSMS architecture notes (hard-won — don't relearn)

1. **SSMS's `.sql` editor is the legacy `IVsEditorFactory`**
   `{B5A506EB-11BE-4782-9A18-21265C2CA0B4}` — declared in `SQLEditors.pkgdef`.
   NOT a MEF content-type-based editor. To make `.squil` get F5 / connection
   picker / results pane, bind the extension to that factory via a
   supplementary `.pkgdef` (`SQuiL.Bindings.pkgdef` in this repo). Modern
   MEF content type registration alone is not enough.

2. **The SQL Query Editor pins content type `SQL` on every buffer it opens.**
   SSMS's Execute (F5) command does a **strict equality check** on the
   content type name. Swapping the buffer to a child type (e.g., `squil`
   inheriting from `SQL`) **disables F5**. The right pattern is: leave the
   buffer as `SQL`, subscribe every SQuiL MEF component to `SQL`, and call
   `SQuiLContentTypeDefinition.IsSquilBuffer(buffer)` (which file-extension-
   checks `.squil`) to short-circuit on non-SQuiL buffers. Keeps SSMS's
   commands happy, keeps SQuiL overlays scoped.

3. **VSIX install target for SSMS 22 is `Microsoft.VisualStudio.Ssms [22.0,) amd64`**
   — NOT `Microsoft.VisualStudio.Community`. SSMS's bundled
   `Application/extension.vsixmanifest` is the reference. Use `vswhere
   -products * -all -property installationName` to enumerate. SSMS's
   instanceId for our test machine is `f16a3f6a`; productId is
   `Microsoft.VisualStudio.Product.Ssms`.

4. **`VSIXInstaller.exe` quirks.**
   `/admin` requires elevation we don't have. `/instanceIds:<id>` scopes
   the install to a specific VS/SSMS instance (use vswhere to enumerate).
   The installer lives at:
   `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe`
   but delegates to the VS installer engine, so it picks up all
   VS/SSMS instances on the machine unless you scope with `/instanceIds`.

5. **After many install/uninstall cycles SSMS can land in a stale state.**
   Symptom: `.squil` opens as plain text, no colours, no F5, nothing works.
   Recovery: kill SSMS → `VSIXInstaller /uninstall:<identity-guid>` →
   delete `%LOCALAPPDATA%\Microsoft\SSMS\22.0_f16a3f6a\ComponentModelCache\*`
   → delete `%LOCALAPPDATA%\Microsoft\SSMS\22.0_f16a3f6a\privateregistry.bin`
   → reinstall → `Ssms.exe /setup` → relaunch. Don't skip `/setup` after
   pkgdef changes.

6. **Layered classifiers + colour priority.**
   SSMS's SQL classifier tags `SELECT`/`FROM`/`int`/`varchar`/etc. as
   "keyword". Our classifier overlays SQuiL-specific scopes. By default,
   both fire and SSMS's wins on the SQL keywords. To force a colour for
   a span (e.g., teal for SQL types), set
   `[Order(After = Priority.High)]` on the `ClassificationFormatDefinition`
   — `Priority.Default` is not enough to override SSMS.

7. **`Completion` (legacy) has no SortText.** The dropdown is sorted
   alphabetically by `DisplayText`. If you need a specific item to filter
   in on `@`, its DisplayText must START with `@`. Workaround for "sort
   this item first": rewrite the DisplayText so the desired position
   matches alphabetical order (we lead the sample-data entry with the
   variable name: `@Params_X    ⊕ insert sample data`, which sorts
   adjacent to `@Params_X`).

8. **SSMS's SQL editor right-click menu is NOT third-party-extensible.**
   The context menu is owned by package `{4058755A-8FBE-41C7-BC99-3DBF5C74BA62}`
   (SqlEditorsPackage in `SQLEditors.dll`); its menu IDs are private and
   compiled into the package's `.cto` resource — not published in any
   `vsshlids.h`-equivalent header.  Parenting commands to the standard
   `IDM_VS_CTXT_CODEWIN` group does not surface them in the SQL editor —
   that ID is honoured only by the modern WPF code editor, which is not
   what opens .sql/.squil files.  Use the **Tools menu** (the convention
   for SSMS extensions — SqlQueryStress, ErikEJ's gallery, etc.) and/or
   `.vsct` keyboard bindings.  Don't waste time chasing the SQL editor's
   right-click.

9. **Tool windows from `InitializeAsync` deadlock.**
   Calling `FindToolWindow(create: true)` from package init (even after
   `SwitchToMainThreadAsync`) hangs SSMS startup. First-run auto-open of
   the writing guide currently disabled for this reason. Workaround for
   later: hook `IVsShell::AdviseShellPropertyChanges` on
   `VSSPROPID_Zombie=false` and do tool-window work from that callback.

10. **WebView2 `Loaded` fires multiple times.**
    WPF re-fires `Loaded` every time a control is reattached to the
    visual tree (re-docking a tool window etc.). `EnsureCoreWebView2Async`
    throws on re-call with a different environment. Guard with a
    `_initStarted` flag.

11. **VSSDK build pipeline notes** (so the VSIX actually packages):
    - SDK-style csproj using `<Project Sdk="Microsoft.NET.Sdk">` doesn't
      automatically wire VSSDK targets. Use the split-Sdk pattern:
      `<Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />` at the top,
      `<Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />` near the
      bottom, then explicit `<Import Project="$(VSToolsPath)\VSSDK\Microsoft.VsSDK.targets" />`
      AFTER Sdk.targets. Otherwise VSSDK's extensions to `PrepareForRunDependsOn`
      are wiped and `CreateVsixContainer` never runs.
    - `<AppendTargetFrameworkToIntermediateOutputPath>false</AppendTargetFrameworkToIntermediateOutputPath>`
      + pin `IntermediateOutputPath=obj\$(Configuration)\` so VSSDK
      (parse-time path resolution) and the SDK (target-time path
      resolution) agree on a single `obj\Debug\` folder.
    - `<EmbeddedResource Update="VSPackage\VSPackage.resx">` (not Include)
      — the SDK already auto-includes it.
    - `ProductArchitecture` in the manifest must be `amd64` (enum value);
      `x64` is rejected by the manifest schema even though SSMS's product
      `chip` reports `x64`.
    - The VSIX itself must contain `LICENSE.txt` at archive root; the
      manifest's `<License>` element points at that path.
    - For supplementary pkgdef registrations (e.g. binding .squil to the
      SQL Query Editor factory), drop a `.pkgdef` file at the archive
      root via `<Content Include="…" IncludeInVSIX="true">`. VSIXInstaller
      merges every `.pkgdef` it finds.

### Editing rules

- **Grammar / language-config / guide content** — edit only in
  `SQuiL.Editor.Shared/`. The per-extension copies are overwritten on each
  build's sync step (`npm run sync-shared` for VS Code, MSBuild target
  `SyncSharedEditorAssets` for SSMS/VS). Grammar + language-configuration are
  copied **verbatim** (still byte-identical / hash-match across consumers).
  `guide.html`, however, is a **template** with `<!--#if env-->` markers
  (env tokens `vscode`, `ssms`, `visualstudio`; space-separated list = OR,
  e.g. `<!--#if ssms visualstudio-->`). It is **rendered per environment** by
  `tools/GuideRenderer` — invoked from the `SyncSharedEditorAssets` targets
  (env `ssms` / `visualstudio`) and from `sync-shared.js` (env `vscode`).
  A malformed template (unbalanced / nested / unknown token) **fails the
  build**. The resulting per-extension `guide.html` outputs are **tailored
  and NO LONGER hash-match** the canonical source (or each other).
- **Claude Code plugin** (`plugins/squil/`, marketplace manifest at
  `.claude-plugin/marketplace.json`) — the plugin's
  `skills/squil/squil.tmLanguage.json` is a copy of the Editor.Shared
  grammar with NO build-time sync; re-copy it manually whenever the grammar
  changes. The repo `plugins/squil/skills/squil/SKILL.md` is canonical for
  the published skill; Paul's machine-local `~/.claude/skills/squil/` is a
  personal install — when editing one, mirror the other (hash-verify).
  Consumers install via `/plugin marketplace add daemogar/SQuiL` +
  `/plugin install squil@squil`.
- **Provider logic ported to both surfaces.** `parser.ts` ↔ `SQuiLParser.cs`,
  `previewGenerator.ts` ↔ `SQuiLPreviewGenerator.cs`,
  `sampleDataGenerator.ts` ↔ `SampleDataGenerator.cs`,
  `hoverProvider.ts` ↔ `SQuiLQuickInfoSource.cs`,
  `diagnosticsProvider.ts` ↔ `SQuiLLinter.cs` + `SQuiLErrorTagger.cs`,
  `completionProvider.ts` ↔ `SQuiLCompletionSource.cs`,
  `updateChecker.ts` ↔ `SQuiLUpdateChecker.cs` (+ shared pure logic
  `versionInfo.ts` ↔ `SQuiLVersion.cs`, baked tag `buildInfo.ts` ↔ `BuildInfo.cs`),
  `variableValidator.ts` ↔ `SQuiLVariableValidator.cs` (source generator,
  emits SP0013 undeclared-variable + SP0016 special-placement diagnostics)
  ↔ `SQuiLLinter.LintUndeclaredVariables` (SSMS + VisualStudio extensions —
  the two SQuiLLinter.cs copies differ only by namespace; sync them whole-file).
  Change one side, change the other.

### Variable validity rules (Paul's ruling, 2026-06-11)

- A SQuiL file must be **valid T-SQL as written**: every `@variable` reference
  requires a textually-preceding `Declare` for that exact name. No remapping,
  no implicit specials — `@Debug`/`@EnvironmentName` must be declared too.
- `@Debug` and `@EnvironmentName` must be declared **before the `Use` statement**
  (error) and **preferably first in the header** (warning).
- Enforced at build time by `SQuiLVariableValidator` (SP0013 error / SP0016
  error+warning) and as editor squigglies in all three extensions.
- **GUIDs** in the SSMS extension link C# to the `.vsct`:
  | C# location | .vsct location |
  |---|---|
  | `SQuiLPackageGuids.PackageGuidString`         | `guidSQuiLPackage` |
  | `SQuiLPackageGuids.CmdSetGuidString`          | `guidSQuiLCmdSet` |
  | `SQuiLPackageGuids.GuideToolWindowGuidString` | (used directly via `[Guid(...)]` on tool window) |
  | `SQuiLPackageGuids.CmdIdCheckForUpdates`      | `cmdidCheckForUpdates` |
  If any GUID changes, update both sides.

### SQuiL naming conventions (must follow in snippets, examples, docs)

- **ID always all caps** — never `Id`. Applies to `@Param_UserID`, `UserID`,
  `NewID()`, etc. Sole exception: VS Code API identifiers like
  `document.languageId` are framework-defined and stay as-is.
- **Generated record naming** — table-valued vars produce `<Name>Table`
  records; single-object vars produce `<Name>Object` records. Never
  `<Name>Item` (older legacy term, do not use).
- **Special variables — all OPT-IN.** Nothing is emitted implicitly; a
  special affects the generated code only when the SQL declares it.
  - `@Debug` → `bool Debug` on `*Request` ONLY when declared. (The old
    "@Debug is ALWAYS on Request" rule is REVERSED, and the old `DebugOnly`
    property is REMOVED.)
  - `@SuppressDebug` (replaces `DebugOnly`) → `bool SuppressDebug` on
    `*Request` when declared; REQUIRES `@Debug` to also be declared —
    declaring it alone is a build error (**SP0019**). Gates the auto-debug
    expression `!request.SuppressDebug && (request.Debug || EnvironmentName != "Production")`.
  - `@AsOfDate` → caller-supplied point-in-time; emitted as a NULLABLE typed
    `*Request` property (type follows the SQL type map, e.g. `date` →
    `System.DateOnly?`). Must be declared BARE — an ordinary `@Param_AsOfDate`
    is a normal scalar, not special. When the caller leaves it null, the
    current time is substituted at execution (`request.AsOfDate ?? <Now>`);
    the SQL initializer is ignored at runtime.
  - `@EnvironmentName` → sent as a SQL parameter ONLY when declared; not a
    `*Request` property.
  - `@Error` and `@Errors` appear on `*Response` when declared.
  - **Placement (all four input specials — `@Debug`/`@SuppressDebug`/
    `@EnvironmentName`/`@AsOfDate`):** declaring one after the `Use`
    statement is an error (SP0016); after other header declarations, a
    warning. Prefer first in the header.
  - **Diagnostic IDs:** SP0019 is TAKEN (SuppressDebug-without-Debug). Shape
    detection, when built later, must start at SP0020+.
- **Sample data detection** — the extension detects an existing sample-data
  block by the `Insert Into @Param_…` statement itself; NO comment markers.
  `@Params_` (list) prompts for row count; `@Param_…Table(...)` (single
  object) auto-inserts exactly one row.
- **`@AsOfDate`** — IMPLEMENTED. The source generator's `IsSpecial()` now
  recognizes `Debug`, `SuppressDebug`, `EnvironmentName`, `AsOfDate`, `Error`,
  and `Errors`; `IsInputSpecial()` covers the four input-side specials. See
  the opt-in semantics above and document it as a special variable everywhere.

### SSMS extension testing workflow (SSMS 22.6)

There is no "F5 → experimental hive" loop for the SSMS extension — SSMS 22
doesn't support the experimental-instance pattern the way VS does. The
iteration cycle is build → uninstall → install → restart SSMS:

```powershell
$proj    = 'C:\dev\projects\SQuiL\SQuiL.SsmsExtension\SQuiL.SsmsExtension.csproj'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe'
$install = 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe'
$ssms    = 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe'
$vsix    = 'C:\dev\projects\SQuiL\SQuiL.SsmsExtension\bin\Debug\SQuiL.SsmsExtension.vsix'
$id      = 'SQuiL.SsmsExtension.5b1e9a6e-3c4f-4c1f-9a3e-2a8f8c1e7d20'

Get-Process ssms -ErrorAction SilentlyContinue | Stop-Process -Force
& $msbuild $proj /t:Rebuild /v:m /nologo
& $install /quiet /uninstall:$id
& $install /quiet $vsix
Get-ChildItem "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_f16a3f6a\ComponentModelCache" -File |
  ForEach-Object { Remove-Item $_.FullName -Force }
Start-Process $ssms -ArgumentList "/log"
```

If a build seems to install cleanly but SSMS still shows stale behaviour
(no colours, F5 disabled, etc.), do a **full reset**: also delete
`%LOCALAPPDATA%\Microsoft\SSMS\22.0_f16a3f6a\privateregistry.bin` and run
`Ssms.exe /setup` before relaunching.

**Bump `<Identity Version="…">` in `source.extension.vsixmanifest` on every
build** — VSIXInstaller skips re-installation if it sees the same version
already installed, which silently masks broken builds.

## Building and Testing

### Build Commands
```bash
# Restore dependencies
dotnet restore

# Build the source generator
dotnet build -c Release SQuiL.SourceGenerator/SQuiL.SourceGenerator.csproj

# Build the entire solution
dotnet build SQuiL.sln

# Build a specific project
dotnet build SQuiL.Tests/SQuiL.Tests.csproj
```

### Running Tests
```bash
# Run all tests
dotnet test SQuiL.Tests/SQuiL.Tests.csproj

# Run a specific test
dotnet test SQuiL.Tests/SQuiL.Tests.csproj --filter "FullyQualifiedName~BasicIODeclareTests"
```

Tests use Verify.SourceGenerators for snapshot testing. Each test generates C# code from SQL input and compares it against verified snapshots in subdirectories.

### Example Application
```bash
# Run the example application
dotnet run --project SQuiL.Simple/SQuiL.Application.csproj
```

## Architecture

### Three-Stage Pipeline

1. **Tokenization** (`SQuiL/Tokenizer/SQuiLTokenizer.cs`)
   - Converts SQL text into tokens
   - Handles SQL keywords, identifiers, literals, comments, and special constructs
   - Token types are defined in `TokenType.cs`

2. **Parsing** (`SQuiL/Parser/SQuiLParser.cs`)
   - Transforms tokens into CodeBlocks
   - Identifies variable naming patterns to determine input/output semantics
   - Recognizes DECLARE statements, USE statements, and SQL body

3. **Code Generation** (`SQuiL/Generator/SQuiLGenerator.cs`)
   - Implements `IIncrementalGenerator` for Roslyn source generation
   - Generates C# classes from CodeBlocks
   - Creates data context methods, request/response models, and DI extensions

### SQL Variable Naming Conventions

The parser recognizes specific naming patterns in SQL `DECLARE` statements:

- `@Param_<name>` → Input scalar parameter
- `@Params_<name>` → Input table-valued parameter (list)
- `@Param_<name> table(...)` → Input object parameter
- `@Return_<name>` → Output scalar variable
- `@Returns_<name>` → Output table (list)
- `@Return_<name> table(...)` → Output object
- `@Debug` / `@SuppressDebug` / `@EnvironmentName` / `@AsOfDate` → Input special
  variables (all opt-in; see the special-variables rules above)
- `@Error` / `@Errors` → Error handling variables (on `*Response` when declared)

These conventions determine the signature of generated C# methods.

### Generated Code Structure

For a SQL file `MyQuery.sql`, the generator creates:
- `<Namespace>.<Context>DataContext.MyQueryDataContext.g.cs` - Main method to execute query
- `<Namespace>.<Context>DataContext.MyQueryRequest.g.cs` - Request model from `@Param*` variables
- `<Namespace>.<Context>DataContext.MyQueryResponse.g.cs` - Response model from `@Return*` variables
- `<Namespace>.<Context>DataContext.Constructor.g.cs` - Base class inheritance + `IConfiguration` constructor (emitted only when no constructor is declared on the context class; see Optional Inheritance below)
- Partial classes for custom tables if `[SQuiLTable]` attributes are used

### Key Components

- **SQuiLDefinition** (`SQuiL/Generator/SQuiLDefinition.cs`): Represents a class with `[SQuiLQuery]` or `[SQuiLTable]` attributes
- **SQuiLDataContext** (`SQuiL/Models/SQuiLDataContext.cs`): Model representing a data context with its queries
- **SQuiLBaseDataContext**: Base class for all generated data contexts (provides connection/parameter helpers)
- **CodeBlock/CodeItem** (`SQuiL/Parser/`): Intermediate representation of parsed SQL
- **SQuiLTableMap**: Tracks custom table type mappings from `[SQuiLTable]` attributes

### Test Structure

Tests in `SQuiL.Tests/` follow a pattern:
1. Each test class (e.g., `BasicIODeclareTests.cs`) contains multiple test methods
2. Tests call `TestHelper.Verify()` with C# source and SQL query strings
3. The source generator runs on the inputs
4. Output is verified against snapshots in `<TestClass>/<TestMethod>/` subdirectories
5. Use `dotnet test` to run tests, which will fail if generated code doesn't match snapshots

To accept new snapshots after intentional changes, delete the `.verified.cs` files and re-run tests.

## SQL File Requirements

SQL files must be marked as `AdditionalFiles` in the `.csproj`:
```xml
<ItemGroup>
    <AdditionalFiles Include="**\Queries\*.sql" />
</ItemGroup>
```

SQL files should contain:
1. A `--Name: <QueryName>` comment (optional, for inline test queries)
2. `DECLARE` statements following naming conventions
3. A `USE [DatabaseName];` statement
4. SQL query body

Example:
```sql
Declare @Param_Name varchar(100);
Declare @Return_Count int;
Use MyDatabase;
Set @Return_Count = (Select Count(*) From Users Where Name = @Param_Name);
Select @Return_Count;
```

## Dependencies

Required NuGet packages:
- `Microsoft.CodeAnalysis.CSharp` (4.12.0) - Roslyn APIs for source generation
- `Microsoft.Data.SqlClient` (6.0.1) - SQL Server connectivity
- `Microsoft.Extensions.Configuration` - Configuration/connection string handling
- `Microsoft.Extensions.DependencyInjection` - DI support

### Package structure (single-package distribution)

The `SQuiL.SourceGenerator` NuGet bundles both the generator DLL (into
`analyzers/dotnet/cs`) **and** the `SQuiL.Library.dll` runtime assembly (into
`lib/netstandard2.0`). Because the library DLL is embedded rather than pulled
in via a NuGet dependency, its transitive `PackageReference`s are **not**
resolved for consumers automatically.

**Rule:** any `PackageReference` added to `SQuiL.Library.csproj` that is needed
at consumer runtime (e.g. `Microsoft.Extensions.Configuration`) must be mirrored
in `SQuiL.SourceGenerator.csproj` so it flows through to the consumer. Keep
the two dependency lists in sync.

Test dependencies:
- `xunit` - Test framework
- `Verify.SourceGenerators` - Snapshot testing for generated code
- `Verify.DiffPlex` - Diff visualization

## Debugging

The source generator includes debugger launch code in `SQuiLGenerator.cs`:
```csharp
#if DEBUG
    if (!System.Diagnostics.Debugger.IsAttached)
        System.Diagnostics.Debugger.Launch();
#endif
```

This is commented out by default but can be enabled for debugging generation issues.

## Connection String Configuration

Generated data contexts expect connection strings in `IConfiguration`:
```json
{
  "ConnectionStrings": {
    "SQuiLDatabase": "...",
    "CustomName": "..."
  }
}
```

The `[SQuiLQuery]` attribute accepts an optional `setting` parameter to specify which connection string to use:
```csharp
[SQuiLQuery(QueryFiles.MyQuery, setting: "CustomName")]
public partial class MyDataContext { }
```

### Optional inheritance (`SQuiLBaseDataContext`)

Inheriting `SQuiLBaseDataContext` explicitly is **not required**. When the context class declares no constructor of its own, the generator emits a `<Ctx>.Constructor.g.cs` file that supplies:

```csharp
public partial class MyDataContext : SQuiLBaseDataContext
{
    public MyDataContext(IConfiguration Configuration) : base(Configuration) { }
}
```

Declaring **any** constructor (primary or ordinary) on the class opts out — the generator skips the constructor file, and the hand-written constructor must chain `: base(configuration)`. The class must still be `partial` (diagnostic **SP0006**). The explicit form — `public partial class MyDataContext(IConfiguration Configuration) : SQuiLBaseDataContext(Configuration) { }` — is still valid and compiles unchanged (backward-compatible). **SP0010 is retired** — do not reuse that diagnostic ID.

## Special Handling

- **Identifiers starting with SQL keywords**: The generator adds special handling for cases where an identifier starts with a keyword (see test for USE keyword)
- **DateTimeOffset → DateTime**: The project converts `datetimeoffset` SQL type to `datetime` C# type
- **Binary data**: Supports binary data input/output
- **Blank lines between data**: Adds formatting for better readability in generated code
