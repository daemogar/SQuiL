# Changelog

All notable changes to SQuiL are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
from 1.0.0 onward.

> **Pre-1.0 note:** SQuiL has not yet cut a stable release. Published builds are
> prereleases (`-beta`) and the public API may change without notice until 1.0.0.

## [Unreleased]

### Added
- SP0046 (build error): two referenced SQuiL projects whose names reduce to the same
  `AddSQuiL<AssemblyName>()` (for example `A.B` and `AB`). Rename one.
- **`SQuiL-Install.cmd` installs the VS Code extension too.** It now covers all
  three editors: it installs `SQuiL-VSCode.vsix` into every VS Code and VS Code
  Insiders found, as the signed-in user and before elevating (VS Code
  extensions are per-user, so an elevated run under other admin credentials
  would land in the wrong profile). It asks for UAC only when SSMS 22+ or
  Visual Studio 2026 is present, so a VS Code-only machine gets no prompt and no
  longer fails for lack of `vswhere`.
- **`[SQuiLQueryTransaction]` attribute** — a sibling to `[SQuiLQuery]` for
  INSERT/UPDATE/DELETE/MERGE queries that need automatic transaction management.
  Produces the same `Process…Async` / `*Request` / `*Response` /
  `SQuiLResultType` generated surface as `[SQuiLQuery]`, but wraps the SQL
  execution in a C# `DbTransaction`. Parameters:
  - `setting` (default `"SQuiLDatabase"`) — connection-string key.
  - `enabled` (default `true`) — inject `BeginTransaction()`; commits when
    `errors.Count == 0`, rolls back on any SQL exception or missing-return-set
    error. `enabled:false` to opt out of injection when the caller owns the
    transaction externally.
  - `debugRollback` (default `true`) — when the query declares `@Debug` and
    the debug expression is true, roll back instead of commit while still
    returning the response that was read (dry-run semantics). Set `false` to
    always commit even in debug mode.
- **One-to-one mapping enforcement** — each query file maps to exactly one data
  context; duplicate registrations are build error SP0027. A class may carry
  `[SQuiLQuery]` or `[SQuiLQueryTransaction]` attributes but not both (SP0029).
- **New diagnostics SP0023–SP0029:**
  - SP0023 (Warning) — `[SQuiLQuery]` / `enabled:false` body has a persistent mutation; suggests `[SQuiLQueryTransaction]`.
  - SP0024 (Warning) — `[SQuiLQueryTransaction]` (enabled) wraps a provably read-only body; suggests `[SQuiLQuery]`.
  - SP0025 (Error) — `[SQuiLQueryTransaction(enabled:true)]` body has its own `Begin Tran` (double-transaction).
  - SP0026 (editor-only Hint) — `debugRollback` is set but no `@Debug` is declared (no effect).
  - SP0027 (Error) — a query file is registered by more than one data context.
  - SP0028 (editor-only Warning) — a `.squil` file no data context registers (orphan).
  - SP0029 (Error) — both `[SQuiLQuery]` and `[SQuiLQueryTransaction]` on one class.
- Editor extensions for `.squil` files: Visual Studio Code, SQL Server
  Management Studio 22.6, and Visual Studio 2026 (syntax highlighting,
  IntelliSense, hover info, linting, generated-C# preview).
- Update checker in all three editor extensions — compares the installed
  build against GitHub releases and offers the download link.
- Build-time variable validation: diagnostic `SP0013` (error) for references
  to undeclared `@` variables, and `SP0016` for misplaced
  `@Debug`/`@EnvironmentName` declarations (error after `USE`, warning when
  not first in the header). The same checks appear as squigglies in all
  three editor extensions.
- "Variable Rules" section in the writing guide documenting the
  declare-before-use requirement.
- Official (stable) releases can now be cut from the Actions tab
  (`workflow_dispatch` on the publish workflow) — same pipeline as the
  per-push betas, but without the `-beta` suffix and not marked prerelease.
- Single-file, self-elevating `install.cmd` installer for the SSMS extension.
- Project onboarding documentation (`README`, `CONTRIBUTING`, this changelog,
  `.editorconfig`).

### Removed
- **Breaking:** the generated `SQuiLExtensions.IsLoaded` property. It was a
  process-wide guard that made `AddSQuiL()` a silent no-op for every service
  collection after the first, so a second host (a test host, a
  `WebApplicationFactory`) got no SQuiL registrations at all. `AddSQuiL()` is
  now idempotent per collection via `TryAddSingleton`, and a context you have
  already registered yourself is no longer overridden.
- **Breaking:** the `@Error`/`@Errors` in-SQL error-collection variables. SQL
  errors now surface solely via `SQuiLResultType` (unwrap with
  `result.TryGetValue(out value, out errors)`). The
  `SQuiLError`/`SQuiLException`/`SQuiLAggregateException` types are unchanged.

### Changed
- **Breaking:** the generated `SQuiLQueryAttribute`, `SQuiLQueryTransactionAttribute`,
  `SQuiLTableAttribute`, `QueryFiles` and `TableType` are now `internal` and
  `[Embedded]`, so they are private to the project that owns the query files.
  Code in another project that names them, even through `InternalsVisibleTo`,
  no longer compiles. This removes the `CS0436` warnings every referencing
  project used to get.
- **Breaking:** `AddSQuiL()` is now generated `internal` in every project that has a data
  context or references a SQuiL project, and it registers every context that project can see:
  its own and every referenced SQuiL project's. Each project with contexts also gets a public
  `services.AddSQuiL<AssemblyName>()` (`Checklist.API` → `AddSQuiLChecklistAPI()`) that registers
  only its own. The public `SQuiLExtensions` type is gone, so code that called
  `SQuiLExtensions.AddSQuiL(services)` from another project no longer compiles; call
  `services.AddSQuiL()` instead. A project built with an older SQuiL isn't picked up and can
  still clash, so build every SQuiL project in a solution with the same version.
- **Breaking:** nested objects now follow declaration order. The
  earlier-declared table contains the later one, no matter which side owns
  the `Primary Key`.
  - If a file declares a child table (the one carrying the key column) before
    its parent (the one whose `Primary Key` it is), the parent now nests
    inside the child. It becomes a single object, and the child record drops
    its key column.
  - Previously the child nested inside the parent as a list.
  - A table linked from two containers is now resolved as a shared lookup or
    as a many-to-many junction.
  - To keep the old shape, move the parent's `Declare` above the child's. The
    editor hint `SP0045` shows which way each link nests.
- **Release assets renamed.** Each editor now publishes one adjacent pair —
  `SQuiL-SSMS.vsix` / `SQuiL-SSMS-INSTALL.md`, `SQuiL-VisualStudio.vsix` /
  `SQuiL-VisualStudio-INSTALL.md`, `SQuiL-VSCode.vsix` /
  `SQuiL-VSCode-INSTALL.md` — and the one-click installer is `SQuiL-Install.cmd`.
  The VS Code package no longer carries `package.json`'s version in its name
  (it was stuck at `squil-editor-0.1.0.vsix` regardless of the release), and
  the VS Code extension now reports the real release version instead of a
  permanent `0.1.0`.
  The previous `.vsix` names are still published as duplicates for a transition
  window so extensions installed before this change keep detecting updates;
  builds from this release forward accept either name.
- **Breaking:** a SQuiL file must be valid T-SQL. Referencing an `@` variable
  without a textually-preceding `DECLARE` now fails the build (`SP0013`).
  This applies to every variable — `@Debug` and `@EnvironmentName` included —
  and there is no name remapping: `@PersonID` is not shorthand for
  `@Param_PersonID`.
- **Breaking:** `@Debug` and `@EnvironmentName` must be declared before the
  `USE` statement (error) and should be declared before any other header
  declaration (warning).
- Pull requests no longer trigger the publish workflow (they were creating
  releases and pushing NuGet packages from unmerged code). PRs are validated
  by the build workflow, which now runs the full test suite again.
- `@`-variables declared after the `USE` statement no longer require the
  `@Param_` / `@Return_` naming prefix.

### Fixed
- String parameters now bind as their declared type. Every `@Param_` string was sent as
  `SqlDbType.VarChar`, so an `nvarchar`/`nchar`/`ntext` value lost any character outside the
  database collation's code page (`Łódź 中` arrived as `Lodz ?`), with no error. `char` and
  `text` now bind as `Char` and `Text` too. A `text`/`ntext` parameter no longer generates code
  that fails to compile. The C# type is unchanged: SQL `char(n)` is still `string`.
- A project that references a SQuiL library through a `ProjectReference`, and
  has no query files and no data context of its own, no longer gets a second
  public `AddSQuiL()`. Its `services.AddSQuiL()` call was ambiguous (`CS0121`) and
  needed an MSBuild target that stripped the analyzer. Such a project also no
  longer gets the SP0005/SP0007/SP0008/SP0009 diagnostics.
- Two SQuiL projects with their own data contexts, where one references the other or both are
  referenced by one host, no longer collide on `AddSQuiL()` (`CS0121`).
- A data context in a namespace starting with `Extensions.` or `DependencyInjection.` no longer
  breaks the generated DI registration (`CS0234`); context types are now written `global::`.
- An unrelated record that merely shared a generated type's name, in any
  namespace and even when not `partial`, was mistaken for the user's partial of
  it. A row record such as `<Ctx>.Models.Institution` was then never emitted, so
  the request or response failed with `CS0234` and no SQuiL diagnostic. A
  same-named `internal` record failed generation with a false SP1001. Only a
  top-level `partial` record in the namespace the generated type lands in now
  merges with it.
- `decimal(p,s)`/`numeric(p,s)` types lost their precision and scale during
  parsing — emitted as bare `decimal` (SQL Server default `decimal(18,0)`,
  truncating fractional values) and silently dropped any columns declared
  after them.
- Null values were being inserted as strings rather than `NULL`.
- Cross-platform path handling so the generator and tests run on Linux.

[Unreleased]: https://github.com/daemogar/SQuiL/commits/master
