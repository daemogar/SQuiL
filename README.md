# SQuiL

**SQuiL** is a C# source generator that turns SQL files into strongly-typed C#
data-access code. You write a `.squil` file using a small set of
variable-naming conventions; SQuiL generates the request/response models, a data
context that executes the query, and the dependency-injection wiring — all at
compile time, with no runtime reflection.

> **File extension:** `.squil` is the canonical extension and is what the
> editor extensions key off. Plain `.sql` works too — the generator accepts
> both — so existing `.sql` query files don't need renaming.

```
your-query.squil  ──▶  SQuiL source generator  ──▶  strongly-typed C#
                                                     ├─ <Query>Request   (from @Param* vars)
                                                     ├─ <Query>Response  (from @Return* vars)
                                                     ├─ <Context>DataContext.<Query>(…)
                                                     └─ AddSQuiL…() DI extension
```

## Why

- **No string-typed SQL scattered through C#.** The SQL lives in its own file;
  the C# surface is generated and type-checked.
- **Parameters and results are inferred from the SQL itself** via naming
  conventions — no hand-written DTOs to keep in sync.
- **Incremental Roslyn generator.** Generation happens in the compiler; there is
  nothing to run or scaffold.

## Requirements

- .NET SDK 10.0 or later to build a consuming project.
- The generator and runtime libraries target `netstandard2.0`, so generated
  code runs anywhere the provider package's ADO.NET client is supported.
- `SQuiL.SqlServer` is the only provider that ships today; multi-database
  support adds providers behind the same `ISqlDialect` seam (see
  `[SQuiLDialect]` below).

## Install

```bash
dotnet add package SQuiL.Core
dotnet add package SQuiL.SqlServer
```

Reference **both** packages:

- `SQuiL.Core` — the source generator (packed as a NuGet analyzer) plus the
  provider-neutral runtime types (`SQuiLResultType`, `SQuiLError`,
  `SQuiLBaseDataContext`, the `[SQuiLQuery]`/`[SQuiLTable]`/`[SQuiLDialect]`
  attributes).
- `SQuiL.SqlServer` — the SQL Server provider: `SqlServerDataContext` and the
  `Microsoft.Data.SqlClient` plumbing generated code executes against.

Both are required because NuGet does **not** flow a package's analyzer through
a transitive dependency — only a *direct* `PackageReference` activates it (the
same reason EF Core's provider packages require the main package alongside
them). Referencing `SQuiL.SqlServer` alone gets you the runtime but no code
generation.

By default a data context targets SQL Server. Add
`[SQuiLDialect(SQuiLDialect.SqlServer)]` to a context class to make that
explicit, or to pick a different provider once more ship.

Mark your query files as `AdditionalFiles` so the generator can see them:

```xml
<ItemGroup>
    <AdditionalFiles Include="**\Queries\*.squil" />
    <AdditionalFiles Include="**\Queries\*.sql" />
</ItemGroup>
```

## Quick example

`Queries/GetUser.squil`:

```sql
Declare @Param_UserID int;
Declare @Return_Name varchar(100);
Use MyDatabase;
Set @Return_Name = (Select Name From Users Where UserID = @Param_UserID);
Select @Return_Name;
```

Declare a data context and point it at the query:

```csharp
[SQuiLQuery(QueryFiles.GetUser)]
public partial class MyDataContext { }
```

SQuiL generates `GetUserRequest`, `GetUserResponse`, the
`MyDataContext.GetUser(…)` method, and an `AddSQuiL…()` extension for DI.
It also auto-supplies a base-class inheritance — the resolved dialect's
runtime base (`SqlServerDataContext` by default, itself derived from the
provider-neutral `SQuiLBaseDataContext`) — and an `IConfiguration` constructor
(into a generated `Constructor.g.cs` file) when the class declares no constructor
of its own — declaring any constructor opts out (it must chain `: base(configuration)`).
The connection string is read from `IConfiguration` (default name
`SQuiLDatabase`, overridable per query via `[SQuiLQuery(…, setting: "Name")]`).

## Variable-naming conventions

SQuiL reads the `DECLARE` statements to decide each variable's role:

| Declaration | Role |
|---|---|
| `@Param_<name>` | input scalar parameter |
| `@Params_<name>` | input table-valued parameter (list) |
| `@Param_<name> table(…)` | input object parameter |
| `@Return_<name>` | output scalar |
| `@Returns_<name>` | output table (list) |
| `@Return_<name> table(…)` | output object |
| `@Debug`, `@SuppressDebug`, `@EnvironmentName`, `@AsOfDate` | special input variables (all opt-in — emitted only when declared) |

Table-valued and single-object variables both generate `<Name>` records (no
`Table`/`Object` suffix). Auto-generated row records are emitted into a `.Models`
sub-namespace of the consuming context by default (override with
`[SQuiLQuery(..., Namespace: "Dto")]` or `Namespace: ""` for top-level). Note the
casing rule: an identifier ending in **ID** is always written `ID` (e.g.
`@Param_UserID` → `UserID`), never `Id`.

## Editor support

Syntax highlighting, IntelliSense, hover info, linting, and a generated-C#
preview for `.squil` files are available for:

- **Visual Studio Code** — `SQuiL.VSCodeExtension`
- **SQL Server Management Studio 22.6** — `SQuiL.SsmsExtension`
- **Visual Studio 2026** — `SQuiL.VisualStudioExtension`

## Claude Code plugin

This repo doubles as a [Claude Code](https://claude.com/claude-code) plugin
marketplace. The `squil` plugin teaches Claude how to author `.squil` files,
wire up a consuming `.csproj`, and wrap the generated data contexts — and
ships the canonical SQuiL TextMate grammar. Install it with:

```
/plugin marketplace add daemogar/SQuiL
/plugin install squil@squil
```

## Documentation

See [CONTRIBUTING.md](CONTRIBUTING.md) for building, testing, project layout,
and a tour of the architecture.

## License

SQuiL is licensed under the **GNU Affero General Public License v3.0** — see
[LICENSE.txt](LICENSE.txt).
