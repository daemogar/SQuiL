# Changelog

All notable changes to SQuiL are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
from 1.0.0 onward.

> **Pre-1.0 note:** SQuiL has not yet cut a stable release. Published builds are
> prereleases (`-beta`) and the public API may change without notice until 1.0.0.

## [Unreleased]

### Added
- **`[SQuiLQueryTransaction]` attribute** — a sibling to `[SQuiLQuery]` for
  INSERT/UPDATE/DELETE/MERGE queries that need automatic transaction management.
  Produces the same `Process…Async` / `*Request` / `*Response` /
  `SQuiLResultType` generated surface as `[SQuiLQuery]`, but wraps the SQL
  execution in a C# `DbTransaction`. Parameters:
  - `setting` (default `"SQuiLDatabase"`) — connection-string key.
  - `enabled` (default `true`) — inject `BeginTransaction()`; commit when no
    errors, roll back on any `SqlException`. `enabled:false` to opt out of
    injection when the caller owns the transaction externally.
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
- **Breaking:** the `@Error`/`@Errors` in-SQL error-collection variables. SQL
  errors now surface solely via `SQuiLResultType` (unwrap with
  `result.TryGetValue(out value, out errors)`). The
  `SQuiLError`/`SQuiLException`/`SQuiLAggregateException` types are unchanged.

### Changed
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
- `decimal(p,s)`/`numeric(p,s)` types lost their precision and scale during
  parsing — emitted as bare `decimal` (SQL Server default `decimal(18,0)`,
  truncating fractional values) and silently dropped any columns declared
  after them.
- Null values were being inserted as strings rather than `NULL`.
- Cross-platform path handling so the generator and tests run on Linux.

[Unreleased]: https://github.com/daemogar/SQuiL/commits/master
