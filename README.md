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
- Three providers ship today — `SQuiL.SqlServer`, `SQuiL.Sqlite`, and
  `SQuiL.Postgres` — behind the same `ISqlDialect` seam; the dialect is
  inferred from the referenced provider package (see `[SQuiLDialect]` below
  and the **SQLite** / **PostgreSQL** sections).

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

The dialect is inferred from the provider package you reference
(`SQuiL.SqlServer` here). Add `[SQuiLDialect(SQuiLDialect.SqlServer)]` to a
context class to make that explicit, or to disambiguate when a project
references more than one provider (see the **SQLite** section).

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

## SQLite

SQuiL ships a native **SQLite** provider alongside SQL Server. Everything the
generator emits — `Process<Query>Async`, the `<Query>Request`/`<Query>Response`
records, the row records in `<Ctx>.Models`, and the `AddSQuiL()` DI extension —
is **identical in shape** across dialects; only the SQL the generator scaffolds,
the runtime base class, and the ADO.NET plumbing differ.

### Install — reference both packages

```bash
dotnet add package SQuiL.Core
dotnet add package SQuiL.Sqlite
```

Same reference-both model as SQL Server (and as EF Core's provider packages):
`SQuiL.Core` carries the generator (a NuGet analyzer) plus the provider-neutral
runtime types; `SQuiL.Sqlite` carries the SQLite provider — `SqliteDataContext`
and the `Microsoft.Data.Sqlite` plumbing. Both are required, because an
analyzer does not flow through a transitive dependency.

### Dialect selection

The dialect is **inferred from the provider package** the project references:
reference `SQuiL.Sqlite` and contexts target SQLite; reference `SQuiL.SqlServer`
and they target SQL Server. If a project references **more than one** provider,
disambiguate each context explicitly with `[SQuiLDialect]`:

```csharp
[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQuery(QueryFiles.ImportPeople)]
public partial class ImportPeopleDataContext { }
```

Resolution order: an explicit `[SQuiLDialect(...)]` wins; otherwise the single
referenced provider decides; otherwise SQL Server is the default. Referencing
2+ providers with no `[SQuiLDialect]` on a context is build error **SP0039**
(ambiguous). A context whose resolved provider package isn't referenced at all
is **SP0038**.

### Authoring model — `Create Temp Table` declarations

A SQLite `.squil` stays **valid SQLite as written** (just as a SQL Server file
stays valid T-SQL). SQLite has no `Declare @x table(...)` or `Use [Db];`, so the
header is a leading run of native `Create Temp Table` statements. The
direction/cardinality convention carries over onto the **table name**:

| Temp-table name | Role | C# surface |
|---|---|---|
| `Params_<N>` | input list | `List<<Ctx>.Models.<N>>? = []` on `*Request` |
| `Param_<N>` (multi-column) | input object | `<Ctx>.Models.<N>?` on `*Request` |
| `Param_<N>` (single-column) | input scalar | typed property on `*Request` |
| `Returns_<N>` | output list | `List<<Ctx>.Models.<N>>?` on `*Response` (no `= []`) |
| `Return_<N>` (multi-column) | output object | `<Ctx>.Models.<N>?` on `*Response` |
| `Return_<N>` (single-column) | output scalar | typed property on `*Response` |

Rules:

- **Params (inputs) must be declared before any Returns (outputs)** — an
  out-of-order declaration is build error **SP0040** in SQLite (a warning in
  SQL Server).
- A **single-column** temp table collapses to a **scalar**.
- Columns use **native SQLite types** (`INTEGER`, `TEXT`, `REAL`, `BLOB`,
  `NUMERIC`/`DECIMAL`, `BOOLEAN`, `DATE`/`DATETIME`, `GUID`); `Primary Key`,
  `null`/`not null`, and `default` are ordinary SQLite column constraints. The
  nullability and nested-object-key rules carry over unchanged.
- The **body boundary** (the role `Use [Db];` plays in T-SQL) is positional: the
  first statement that is neither a `Create Temp Table` nor a population of a
  declared **param** temp table. Everything from there down is emitted verbatim.
- Sample `Insert Into <ParamTable> … Values …` DML in the header is **stripped**
  at generation and replaced by the `json_each` input shred — exactly like the
  SQL Server pipeline strips `Insert Into @Var` and substitutes `OPENJSON`. The
  file runs as-is in a SQLite shell; generation swaps only sample-inserts ↔
  shred.

### Worked example

Author writes (valid, runnable SQLite):

```sql
--Name: ImportPeople
Create Temp Table Params_Person (PersonID INTEGER Primary Key, Name TEXT, Age INTEGER);
Create Temp Table Return_Count (Value INTEGER);
Create Temp Table Returns_Imported (PersonID INTEGER, Status TEXT);

-- sample data (stripped at generation, replaced by the json_each shred)
Insert Into Params_Person (PersonID, Name, Age) Values (1, 'Ada', 36), (2, 'Alan', 41);

-- ↓ first non-temp / non-param-population statement = body boundary (replaces `Use`)
Insert Into Returns_Imported (PersonID, Status) Select PersonID, 'ok' From Params_Person;
Insert Into Return_Count (Value) Select Count(*) From Returns_Imported;
Select Value From Return_Count;
Select PersonID, Status From Returns_Imported;
```

Register the context (dialect inferred from `SQuiL.Sqlite`, or pinned
explicitly):

```csharp
[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQuery(QueryFiles.ImportPeople)]
public partial class ImportPeopleDataContext { }
```

SQuiL generates the same surface it would for SQL Server:

```csharp
public partial record ImportPeopleRequest
{
    public List<TestCase.Models.Person>? Person { get; set; } = [];
}

public partial record ImportPeopleResponse
{
    public long Count { get; set; }                       // INTEGER → long
    public List<TestCase.Models.Imported>? Imported { get; set; }
}

// Method on ImportPeopleDataContext (: SqliteDataContext):
public Task<SQuiLResultType<ImportPeopleResponse>> ProcessImportPeopleAsync(
    ImportPeopleRequest request, CancellationToken cancellationToken = default) { /* generated */ }
```

Inputs marshal through `json_each`/`json_extract` (SQLite's `OPENJSON`
analogue); `byte[]`/`BLOB` values round-trip through a hex string and `unhex`.
Note SQLite's native affinities: `INTEGER` → `long` and `REAL` → `double`
(distinct from T-SQL's `int` → `int` / `real` → `float`) — the SQLite dialect
owns its whole type map.

### Editors

The VS Code and Visual Studio extensions are dialect-aware: they discover a
`.squil` file's dialect from the owning `.csproj`'s SQuiL provider
`PackageReference` (with `[SQuiLDialect]` as the override in multi-provider
projects). SSMS opens `.squil` standalone with no project, so it defaults to the
SQL Server vocabulary (SSMS is a SQL Server tool).

## PostgreSQL

SQuiL also ships a native **PostgreSQL** provider. Like SQLite, PostgreSQL has
no `Declare @x table(...)`/`Use [Db];` syntax, so it reuses the same
**`Create Temp Table`** authoring model (temp-table name carries
direction/cardinality, single-column ⇒ scalar). Everything the generator
emits — `Process<Query>Async`, `<Query>Request`/`<Query>Response`, the row
records in `<Ctx>.Models`, and `AddSQuiL()` — is identical in shape to SQL
Server and SQLite; only the emitted SQL, the runtime base class, and the
ADO.NET plumbing differ.

### Install — reference both packages

```bash
dotnet add package SQuiL.Core
dotnet add package SQuiL.Postgres
```

Same reference-both model as SQL Server and SQLite: `SQuiL.Core` carries the
generator (a NuGet analyzer) plus the provider-neutral runtime types;
`SQuiL.Postgres` carries `PostgresDataContext` and the `Npgsql` plumbing.
`SQuiL.Postgres` multi-targets `netstandard2.0` (Npgsql 6.0.x) and `net10.0`
(Npgsql 10.x), so it can be consumed from either TFM. Both packages are
required — an analyzer never flows through a transitive dependency.

The dialect is inferred from the referenced provider package, or pin it
explicitly:

```csharp
[SQuiLDialect(SQuiLDialect.Postgres)]
[SQuiLQuery(QueryFiles.ImportPeople)]
public partial class ImportPeopleDataContext { }
```

`SQuiLDialect.Postgres` is dialect id **2**. Resolution/ambiguity rules
(SP0038/SP0039) are unchanged from the SQL Server/SQLite model.

### Authoring model — same `Create Temp Table` header as SQLite

A PostgreSQL `.squil` stays **valid PostgreSQL as written**. As with SQLite,
`Params_<N>`/`Param_<N>` name input tables/objects, `Returns_<N>`/`Return_<N>`
name output tables/objects, and a single-column temp table collapses to a
scalar. Params must be declared before returns — out of order is build error
**SP0040** (an error for PostgreSQL, same as SQLite, because the temp-table
header order is structural).

Columns use native PostgreSQL types — the full map is in [CLAUDE.md](CLAUDE.md).
The short version:

| PostgreSQL type(s) | C# |
|---|---|
| `int4`/`int`/`integer` | `int` |
| `int8`/`bigint` | `long` |
| `int2`/`smallint` | `short` |
| `text`/`varchar`/`character varying`/`char`/`bpchar`/`json`/`jsonb` | `string` |
| `bytea` | `byte[]` |
| `uuid` | `System.Guid` |
| `boolean`/`bool` | `bool` |
| `timestamp` | `System.DateTime` |
| `timestamptz` | `System.DateTimeOffset` |
| `date` | `System.DateOnly` |
| `time` | `System.TimeOnly` |
| `numeric`/`decimal`/`money` | `decimal` |
| `real`/`float4` | `float` |
| `double precision`/`float8` | `double` |

### Worked example

Author writes (valid, runnable PostgreSQL):

```sql
--Name: ImportPeople
Create Temp Table Params_Person (PersonID int4 Primary Key, Name text, Photo bytea);
Create Temp Table Returns_Imported (PersonID int4, Name text);

-- sample data (stripped at generation, replaced by the json_to_recordset shred)
Insert Into Params_Person (PersonID, Name, Photo) Values (1, 'Ada', decode('00AB', 'hex'));

Insert Into Returns_Imported (PersonID, Name) Select PersonID, Name From Params_Person;
Select PersonID, Name From Returns_Imported;
```

Inputs marshal through `json_to_recordset(@__json_...) AS x("Col" pgtype, ...)`
— the typed, OPENJSON-style analogue (unlike SQLite's untyped `json_each`).
`bytea` columns round-trip as hex text, decoded in the shred with
`decode(x."Col", 'hex')`.

**Identifier casing (Option B).** PostgreSQL folds unquoted identifiers to
lowercase. SQuiL emits everything bare — the temp-table DDL, the insert
target, and the author's whole body — exactly as you'd write it by hand; the
only place SQuiL quotes an identifier is the `json_to_recordset` `AS`
column-list, which must match the JSON payload's PascalCase keys. Result
routing is case-insensitive on the PostgreSQL side, so a build-time key like
`PersonID` still matches the lowercased `personid` column PostgreSQL's reader
reports.

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
