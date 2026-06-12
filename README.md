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
- The generator and runtime library target `netstandard2.0`, so generated code
  runs anywhere `Microsoft.Data.SqlClient` is supported.
- SQL Server is the current target database (multi-database support is on the
  roadmap).

## Install

```bash
dotnet add package SQuiL.SourceGenerator
```

The `SQuiL.SourceGenerator` package bundles both the generator and the
`SQuiL.Library` runtime types, so it is the only reference you need.

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
public partial class MyDataContext : SQuiLBaseDataContext { }
```

SQuiL generates `GetUserRequest`, `GetUserResponse`, the
`MyDataContext.GetUser(…)` method, and an `AddSQuiL…()` extension for DI. The
connection string is read from `IConfiguration` (default name
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
| `@Debug`, `@EnvironmentName` | special request variables |
| `@Error`, `@Errors` | error-handling response variables |

Table-valued variables generate `<Name>Table` records; single-object variables
generate `<Name>Object` records. Note the casing rule: an identifier ending in
**ID** is always written `ID` (e.g. `@Param_UserID` → `UserID`), never `Id`.

## Editor support

Syntax highlighting, IntelliSense, hover info, linting, and a generated-C#
preview for `.squil` files are available for:

- **Visual Studio Code** — `SQuiL.VSCodeExtension`
- **SQL Server Management Studio 22.6** — `SQuiL.SsmsExtension`
- **Visual Studio 2026** — `SQuiL.VisualStudioExtension`

## Documentation

See [CONTRIBUTING.md](CONTRIBUTING.md) for building, testing, project layout,
and a tour of the architecture.

## License

SQuiL is licensed under the **GNU Affero General Public License v3.0** — see
[LICENSE.txt](LICENSE.txt).
