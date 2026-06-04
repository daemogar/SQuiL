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
- Single-file, self-elevating `install.cmd` installer for the SSMS extension.
- Project onboarding documentation (`README`, `CONTRIBUTING`, this changelog,
  `.editorconfig`).

### Changed
- `@`-variables declared after the `USE` statement no longer require the
  `@Param_` / `@Return_` naming prefix.

### Fixed
- Null values were being inserted as strings rather than `NULL`.

[Unreleased]: https://github.com/daemogar/SQuiL/commits/master
