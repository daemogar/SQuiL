# Changelog

All notable changes to SQuiL are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
from 1.0.0 onward.

> **Pre-1.0 note:** SQuiL has not yet cut a stable release. Published builds are
> prereleases (`-beta`) and the public API may change without notice until 1.0.0.

## [Unreleased]

### Added
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
