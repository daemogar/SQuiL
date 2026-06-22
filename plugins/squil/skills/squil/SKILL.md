---
name: squil
description: Use this skill whenever the user is working with SQuiL — the C# source generator at https://github.com/daemogar/SQuiL that turns .squil/.sql query files into strongly-typed C# data contexts. Trigger on any mention of SQuiL or any .squil file; on SQuiLBaseDataContext, SQuiLResultType, [SQuiLQuery], [SQuiLTable], SQuiLException, SQuiLAggregateException, AddSQuiL, Process…Async; on .sql files paired with a C# project that uses AdditionalFiles for queries; on the @-prefix naming conventions (@Param_, @Params_, @Return_, @Returns_, @Debug, @SuppressDebug, @AsOfDate, @EnvironmentName); or whenever the user asks to author SQuiL query files, set up a .csproj for SQuiL, register a SQuiL data context, or write wrappers around generated Process…Async methods. Trigger even when the user does not say "SQuiL" by name — if their .csproj references SQuiL.SourceGenerator or SQuiL.Library, this skill applies. Prefer this skill over generic "C# / SQL" guidance for any project that uses SQuiL.
---

# SQuiL skill

SQuiL is a C# source generator that converts SQuiL query files into strongly-typed C# code: a partial data context, a request/response pair per query, row classes for table-valued parameters and result tables, a `QueryFiles` enum, a `TableType` enum, and DI helpers. The author writes plain T-SQL with specific `@`-prefixed naming; the generator reads those names to figure out parameter shapes and result shapes.

**File extension:** `.squil` is the canonical extension for SQuiL query files — use it in everything you author (new files, examples, docs). The generator also accepts plain `.sql`, so existing `.sql` query files are fine and do not need renaming; the editor extensions' tooling (highlighting, linting, preview), however, keys off `.squil`.

This skill covers three things:

1. **Authoring `.squil` files** that the generator can parse.
2. **Wiring up a `.csproj`** to consume SQuiL.
3. **Using the generated code** through a recommended file layout — one partial data context that registers queries, plus per-query partial files that wrap the generated `Process…Async` methods so callers never touch the generated surface directly.

This skill does not cover contributing to SQuiL itself (the source generator's parser/tokenizer/codegen). For that, work against the live repo and its snapshot tests.

## Syntax highlighting

The canonical TextMate grammar for SQuiL ships with this skill at `squil.tmLanguage.json` (scope `source.squil`, same file the VS Code extension bundles). Use it whenever syntax-aware handling of `.squil` content is needed:

- **Rendering SQuiL in markdown** — fence SQuiL code blocks with ` ```sql ` (closest supported language; SQuiL is a T-SQL superset by convention, so SQL highlighting is correct for everything except the `@`-prefix semantics).
- **Tokenizing/classifying SQuiL text** (building editor support, HTML rendering, docs generation) — read `squil.tmLanguage.json` from this skill's directory and apply its patterns; the SQuiL-specific scopes are `variable.parameter.input.squil` (`@Param_`/`@Params_`), `support.variable.return.squil` (`@Return_`/`@Returns_`), `variable.other.special.squil` (`@Debug`, `@EnvironmentName`), and `meta.annotation.name.squil` (the `--Name:` header).

---

## 1. Authoring `.squil` files

A SQuiL-parsable `.squil` file has three sections, in order:

1. A leading `Declare` block — declares input parameters, output scalars, and output table types.
2. A `Use <Database>;` line — names the database the connection should target.
3. The actual SQL body — sets values, executes queries, populates declared tables, and finishes with `Select` statements that surface the outputs.

Adding new `Declare` statements *after* the `Use` is rejected by the parser. If a generator error mentions "use statement fails when adding declare," that is almost always the cause.

### Variable naming conventions

The `@`-prefix is how the generator distinguishes inputs, outputs, and special variables. Casing of the prefix is not significant (`@Param_` and `@param_` both work) but PascalCase after the prefix is the convention and is what the generated C# member names mirror.

| Prefix | Meaning | What the generator emits |
|---|---|---|
| `@Param_<Name>` | Input scalar parameter | Field `<Name>` on the request model |
| `@Params_<Name>` | Input table-valued parameter | Collection field `<Name>` on the request model, plus a row class |
| `@Return_<Name>` | Output scalar variable | Field `<Name>` on the response model |
| `@Returns_<Name>` | Output table | Collection field `<Name>` on the response model, plus a row class |
| `@Debug` | Diagnostic toggle (opt-in) | When declared, a `bool Debug` on the request model |
| `@SuppressDebug` | Suppresses the auto-debug expression (opt-in) | When declared, a `bool SuppressDebug` on the request model |
| `@AsOfDate` | Caller-supplied point-in-time (opt-in) | When declared *bare*, a **nullable** typed property on the request model |
| `@EnvironmentName` | Bound to current environment at runtime (opt-in) | Sent as a SQL parameter when declared; not a request/response property |

A scalar uses a primitive declared type (`int`, `varchar(10)`, `datetime`, `bit`, …). A table uses `Declare @Returns_<Name> table(...)` or `Declare @Params_<Name> table(...)` with column definitions; each column becomes a property on a generated row record.

### Column types and defaults

The declared SQL type maps to a C# type: `int`/`bigint`/`smallint`/`tinyint` → the matching integer type, `bit` → `bool`, `varchar`/`nvarchar`/`char`/`text` → `string`, `decimal(p,s)`/`numeric(p,s)` → `decimal` (precision and scale are preserved), `float` → `double`, `real` → `float`, `date` → `System.DateOnly`, `datetime`/`datetime2`/`smalldatetime` → `System.DateTime`, `datetimeoffset` → `System.DateTimeOffset`, `uniqueidentifier` → `System.Guid`, `varbinary`/`binary`/`image` → `byte[]`. A nullable SQL column produces a nullable C# type.

A table column may carry a SQL `default` in **any** position. The generator produces a **hybrid record**: non-defaulted columns become positional constructor parameters (SQL relative order preserved), and defaulted columns become `{ get; init; } = <value>` properties. For example:

```sql
Declare @Params_Rows table(RowID int, Amount decimal(18,2) default 1.5, Qty int, Note varchar(50) default 'hello');
```

generates:

```csharp
public partial record RowsTable(int RowID, int Qty)
{
    public decimal Amount { get; init; } = 1.5m;
    public string Note { get; init; } = "hello";
}
```

Construct with `new RowsTable(1, 5)` or `new RowsTable(1, 5) { Amount = 2.5m, Note = "x" }`. Tables with no defaults are unchanged (plain positional record). The editor extensions parse `default` values and emit this same hybrid shape in their C# preview. Default values are mapped per-type via `Token.CSharpValue` (decimal gets an `m` suffix, strings are quoted).

### Special variables (opt-in)

The four input-side special variables only affect the generated C# when you **declare them** in the header — nothing is emitted implicitly. They all share the same placement rules: declaring one after the `Use` statement is a build error (SP0016); declaring one after other header declarations is a warning. Put them first in the header.

- **`@Debug`** — declare it (`Declare @Debug bit = 0;`) to get a `bool Debug` on the request model and to read the value in the SQL body. (Previously `@Debug` was *always* on the request along with a `DebugOnly` property; both behaviors are gone — `@Debug` is now opt-in and `DebugOnly` no longer exists.)
- **`@SuppressDebug`** — declare it to get a `bool SuppressDebug` on the request model. It **requires `@Debug` to also be declared**; declaring it alone is a build error (**SP0019**). It replaces the old `DebugOnly` property and gates the auto-debug expression SQuiL sends: `!request.SuppressDebug && (request.Debug || EnvironmentName != "Production")`.
- **`@AsOfDate`** — declared *bare* (`Declare @AsOfDate date = '2008-10-01';`), it becomes a **nullable** typed property on the request model whose type follows the SQL type map (e.g. `date` → `System.DateOnly?`). When the caller leaves it `null`, the current time is substituted at execution; the SQL initializer is ignored at runtime. An ordinary `@Param_AsOfDate` (with the prefix) is *not* special — it is a normal input scalar.
- **`@EnvironmentName`** — declare it to have the current environment sent as a SQL parameter for the body to read; it is not emitted as a request property.

### Minimal example — single scalar return

```sql
Declare @Return_Scaler int;

Use UnitTesting;

Set @Return_Scaler = 42;

Select @Return_Scaler;
```

### Example with input param and two return tables

```sql
Declare  @Param_PersonID varchar(10),
         @Debug bit = 1;

Declare  @Returns_Participation table(
    SectionID    varchar(20),
    PersonID     varchar(10),
    ProfessorID  varchar(10),
    TermCode     varchar(10),
    CompletedDate datetime
);

Declare  @Returns_Overrides table(
    SectionID  varchar(20),
    TermCode   varchar(10),
    CourseCode varchar(20),
    BeginDate  datetime,
    EndDate    datetime
);

Use DataRepositoryTest;

Insert Into @Returns_Participation
Select ... From ... Where PersonID = @Param_PersonID;

Insert Into @Returns_Overrides
Select ... From ...;

Select * From @Returns_Participation;
Select * From @Returns_Overrides;
```

The trailing `Select * From @Returns_…` statements are how rows actually leave the query — the generator wires the result-set ordering to the response object, so emit one terminal `Select` per declared `@Returns_…` table, in the order they appear on the response.

### Common authoring mistakes

- **Skipping `Use <DB>`.** Required, and it must come after the declares but before any data-modifying SQL.
- **Adding new `Declare` statements after the `Use`.** Belongs in the leading block.
- **Mixing return tables and ad-hoc scalar `Select` results.** Every result-set the SQL emits should correspond to a declared `@Return_/@Returns_` variable, in matching order.
- **Reusing a name across input and output with different columns.** Table variables that share a base name (`@Param_Foo table(...)` + `@Return_Foo table(...)`, in one file or across query files) share a single generated record, so their column lists must be identical — same names, types, nullability, and order (sizes may differ). A mismatch is build error SP0017; either align the columns or rename one variable. When in doubt, just use distinct names.
- **Referencing an `@variable` that was never declared.** A SQuiL file must be valid T-SQL *as written* — every `@variable` reference (including the specials like `@Debug` and `@EnvironmentName`) needs a textually-preceding `Declare` for that exact name. An undeclared reference is build error SP0013; the generator never invents or remaps names.
- **Forgetting to PascalCase the suffix.** `@Param_personid` works but generates `personid` as a C# field, which fights every other naming convention in the project.

---

## 2. Wiring up a `.csproj`

A consuming project needs three things: `AdditionalFiles` so the generator sees the query files, a reference to the source generator marked as an analyzer, and a project/package reference to `SQuiL.Library` for the runtime base classes.

### Reference template

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- The generator picks up every .squil (and legacy .sql) under any
         Queries/ folder. Adjust the globs if the project keeps queries
         elsewhere. -->
    <AdditionalFiles Include="**\Queries\*.squil" />
    <AdditionalFiles Include="**\Queries\*.sql" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.1" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.7" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.7" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SQuiL.Library\SQuiL.Library.csproj" />
    <ProjectReference Include="..\SQuiL.SourceGenerator\SQuiL.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

The `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"` attributes on the source-generator reference are not optional. Without them MSBuild treats the generator as a regular runtime dependency, the generation never runs, and every `[SQuiLQuery]` lights up red because nothing was emitted.

If the user installs SQuiL from NuGet rather than building from source, replace the two `ProjectReference` lines with `PackageReference` entries (one for `SQuiL.Library`, one for the analyzer package). The analyzer package must also carry the analyzer attributes — that follows the standard analyzer NuGet convention.

### Connection strings and environment

`SQuiLBaseDataContext` reads connection strings from `IConfiguration` with the standard `ConnectionStrings:<Name>` key. Each `[SQuiLQuery(..., setting: "Name")]` attribute names which connection-string key that query uses, so the project's configuration needs at least one connection string per distinct `setting:` value used in the data context.

```csharp
ConfigurationBuilder builder = new();
builder.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:ExampleOne"] = "Data Source=...;Initial Catalog=...;Integrated Security=True;...",
});
```

`SQuiLBaseDataContext` resolves the current environment from the config key `EnvironmentName`, falling back to the env var `EnvironmentVariable`, then to `"Development"`. Queries that bind `@EnvironmentName` see this value at runtime.

---

## 3. Using the generated code — recommended layout

The generator emits a partial data-context class plus per-query types. For a query whose `QueryFiles` enum member is `<QueryName>`, you get:

- a method `Process<QueryName>Async(<QueryName>Request request, CancellationToken)` on the data context;
- a request record `<QueryName>Request` and a response record `<QueryName>Response` (note: **no `Process` prefix** on the model types — only the method has it);
- row records for table-valued variables, named `<Name>Table` (table-valued) or `<Name>Object` (single-object) — never `<Name>Row`/`<Name>Item`;
- the method returns `Task<SQuiLResultType<<QueryName>Response>>` (or the non-generic `Task<SQuiLResultType>` when the query declares no `@Return*`). `SQuiLResultType<T>` is a result wrapper, **not** the response itself — unwrap it with `result.TryGetValue(out var value, out var errors)`. Errors are *returned in the result*, not thrown.

`<QueryName>` is the enum member: folder name + file name, both PascalCased and joined. So `Queries/Example1.squil` → `QueriesExample1` → method `ProcessQueriesExample1Async`, types `QueriesExample1Request` / `QueriesExample1Response`.

**Don't call those generated methods from the rest of the app.** They are a low-level surface — every call site would have to remember the right request shape, unwrap the result, and apply the right cancellation pattern, and any rename in the SQL would break consumers.

Instead, use this layout:

```
MyProject/
├── MyProject.csproj
├── MyDataContext.cs              # the [SQuiLQuery] / [SQuiLTable] attributes live here
├── Queries/
│   ├── Example1.squil            # generator input
│   ├── Example1.cs               # hand-written wrappers — partial of MyDataContext
│   ├── Example2.squil
│   └── Example2.cs
└── .filenesting.json             # nests *.squil under matching *.cs in Solution Explorer
```

### `MyDataContext.cs` — the registry

This file is the single source of truth for which queries exist and which connection-string each uses. The class is `partial` so per-query files can extend it.

```csharp
using SQuiL;

namespace MyProject;

[SQuiLQuery(QueryFiles.QueriesExample1, setting: "ExampleOne")]
[SQuiLQuery(QueryFiles.QueriesExample2, setting: "ExampleTwo")]
public partial class MyDataContext { }

// SQuiL auto-supplies : SQuiLBaseDataContext and an IConfiguration constructor
// when no constructor is declared. Declare any constructor to opt out
// (it must chain : base(configuration)).

[SQuiLTable(TableType.Participation)]
[SQuiLTable(TableType.Overrides)]
public partial class Table { }
```

The `QueryFiles` enum is generated from the query-file paths: `Queries/Example1.squil` becomes `QueryFiles.QueriesExample1` (folder name + file name, both PascalCased, joined). The `TableType` enum is generated from `@Returns_<Name>` and `@Params_<Name>` table declarations — one entry per distinct table name across all query files. Add a `[SQuiLTable]` for each table the project actually uses so the row record is emitted.

Two rules govern `[SQuiLTable]` types:

- **Every SQL declaration that feeds one record must declare identical columns** (same names, types, nullability, and order; sizes may differ). This applies both to table variables that share a name and to different names mapped onto one class via multiple `[SQuiLTable]` attributes. Mismatches are build error SP0017.
- **Never give a `[SQuiLTable]` partial record a primary constructor** (build error SP0018) — the generator emits the parameter list, and C# allows only one partial declaration to have one. Customize with a body instead:

```csharp
// ✔ add members in a body
[SQuiLTable(TableType.Terms)]
public partial record TermTable { }

// ✘ SP0018 — the generator owns the parameter list
// [SQuiLTable(TableType.Terms)]
// public partial record TermTable(string TermCode);
```

### `<queryfilename>.cs` — per-query wrappers

Each `.squil` gets a sibling `.cs` that adds wrapper methods to `MyDataContext` via `partial`. These wrappers are the public API: callers call `GetParticipationForPersonAsync(personId)`, not `ProcessQueriesExample2Async(new QueriesExample2Request { … })` followed by unwrapping a `SQuiLResultType`. The wrapper is also where the result is unwrapped — decide there what an error *means* (return a default, throw, log), so call sites never see `SQuiLResultType`.

```csharp
using SQuiL;

namespace MyProject;

public partial class MyDataContext
{
    public async Task<int> GetExample1ScalerAsync(CancellationToken ct = default)
    {
        var result = await ProcessQueriesExample1Async(
            new QueriesExample1Request(),
            ct);

        if (!result.TryGetValue(out var response, out var errors))
            throw new SQuiLAggregateException(errors);

        return response.Scaler;
    }
}
```

```csharp
using SQuiL;

namespace MyProject;

public partial class MyDataContext
{
    public async Task<ParticipationResult> GetParticipationForPersonAsync(
        string personId,
        CancellationToken ct = default)
    {
        var result = await ProcessQueriesExample2Async(
            new QueriesExample2Request { PersonID = personId },
            ct);

        if (!result.TryGetValue(out var response, out var errors))
            throw new SQuiLAggregateException(errors);

        return new ParticipationResult(
            response.Participation ?? [],
            response.Overrides ?? []);
    }
}

public record ParticipationResult(
    IReadOnlyList<ParticipationTable> Participation,
    IReadOnlyList<OverridesTable> Overrides);
```

`response.Participation` / `response.Overrides` are `List<ParticipationTable>?` / `List<OverridesTable>?` (the generated response initializes them to `[]`, but they are typed nullable). Throwing `SQuiLAggregateException` here is one choice; the wrapper could equally return an empty/sentinel result or surface the `errors` list directly.

Why this layout matters:

- **One place to register queries.** Adding a new query is two steps (drop the `.squil`, add an attribute on `MyDataContext`) — not a hunt across the codebase.
- **Authoring files travel together.** SQL + its wrapper live in the same folder. Renaming a `@Param_` and the call-site update is one folder, not two.
- **Stable consumer API.** Callers see hand-named methods describing intent (`GetParticipationForPersonAsync`), not generator-named methods describing mechanics (`ProcessQueriesExample2Async`). The wrapper absorbs SQL-shape changes without breaking consumers.
- **Error handling has a home.** The `SQuiLResultType` is unwrapped in the wrapper, where there's enough context to know what an error *means* — turn it into a thrown `SQuiLAggregateException`, a default, or a domain result; call sites only see the translated outcome.
- **Cancellation and instrumentation in one spot.** Logging, metrics, retries, timeouts: applied per-query in the wrapper, not sprinkled at every call site.

### `.filenesting.json` — IDE polish

Visual Studio reads `.filenesting.json` to nest related files in Solution Explorer. Drop this at the project root so each `Example1.squil` shows up as a child of `Example1.cs`:

```json
{
  "help": "https://go.microsoft.com/fwlink/?linkid=866610",
  "dependentFileProviders": {
    "add": {
      "fileToFile": {
        "add": {
          "*.squil": [ "$(fileNameWithoutExtension).cs" ],
          "*.sql": [ "$(fileNameWithoutExtension).cs" ]
        }
      }
    }
  }
}
```

This is purely cosmetic — it doesn't affect the generator or the build — but it keeps the file tree readable when there are many queries.

### DI registration

The generator emits an `AddSQuiL()` extension method (in the
`Microsoft.Extensions.DependencyInjection` namespace) that registers every
generated data context as a singleton. Prefer it — new contexts come along
automatically as queries are added:

```csharp
services.AddSingleton<IConfiguration>(configuration);
services.AddSQuiL();
```

You still need `IConfiguration` registered (or otherwise injectable) so the
context's constructor can resolve connection strings. `AddSQuiL()` is idempotent
(guarded by an `IsLoaded` flag).

### Error handling

A query's result comes back as `SQuiLResultType<TResponse>` (or non-generic
`SQuiLResultType`). Inspect it with `TryGetValue`:

```csharp
if (result.TryGetValue(out var response, out var errors))
{
    // success — use `response`
}
else
{
    // failure — `errors` is an IReadOnlyList<SQuiLError>
}
```

`SQuiL.Library` provides three related types:

- `SQuiLError` — the per-error record (SQL error number, severity, state, line, procedure, message); this is what `errors` holds.
- `SQuiLException` — wraps a single `SQuiLError`; build one with `error.AsSQuiLException()`.
- `SQuiLAggregateException` — wraps an `IReadOnlyList<SQuiLError>`; `new SQuiLAggregateException(errors)`.

The generated code **does not throw** these — it returns the errors in the
result. Whether to throw (`SQuiLException`/`SQuiLAggregateException`), return a
default, or surface a domain-specific outcome is the wrapper's decision, made
where there's enough context to know what an error from *this* query means.

---

## When to read the upstream repo

This skill encodes conventions that are stable across SQuiL versions. Read the live repo (https://github.com/daemogar/SQuiL) when:

- The user reports a generator error whose phrasing doesn't match anything here — the parser's messages are more specific than this skill can be, and `SQuiL.Tests/` snapshots are the source of truth for what the generator emits.
- A generated symbol has a different name than what's documented here — newer SQuiL versions may rename things.
- The user is debugging the generator output itself or working inside `SQuiL.SourceGenerator/`.

For everyday authoring, project setup, and consumer-side code, the conventions above are sufficient.
