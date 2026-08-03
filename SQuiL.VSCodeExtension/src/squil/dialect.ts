/**
 * SQuiL Editor Dialect Discovery
 *
 * Determines whether the .csproj owning a .squil file targets SQL Server,
 * SQLite, or PostgreSQL, so editor features (type vocabulary, completion,
 * hover, preview) can present the right SQL type keywords. Discovery reads
 * the `<PackageReference>` elements of the owning .csproj — the same package
 * ids the source generator's `DialectRegistry.ProviderPackageId` uses
 * ("SQuiL.SqlServer" / "SQuiL.Sqlite" / "SQuiL.Postgres").
 *
 * Port: SQuiL.SsmsExtension/Parsing/SQuiLDialect.cs and
 *       SQuiL.VisualStudioExtension/Parsing/SQuiLDialect.cs —
 *       change one side, change all.
 */

export type EditorDialect = 'sqlite' | 'sqlserver' | 'postgres';

const SQLITE_REF_RE = /<PackageReference\s+Include\s*=\s*"SQuiL\.Sqlite"/i;
const SQLSERVER_REF_RE = /<PackageReference\s+Include\s*=\s*"SQuiL\.SqlServer"/i;
const POSTGRES_REF_RE = /<PackageReference\s+Include\s*=\s*"SQuiL\.Postgres"/i;

/**
 * Resolve the dialect from the raw text of a .csproj file.
 *
 * Returns the single referenced provider's dialect (`'sqlite'` / `'postgres'` /
 * `'sqlserver'`) when EXACTLY one of the three provider packages is
 * referenced. Falls back to `'sqlserver'` when zero or 2+ providers are
 * referenced (an explicit marker is preferred whenever the choice is
 * ambiguous), matching the generator's `DialectRegistry.ResolveId` — a single
 * referenced provider wins; 0 or 2+ referenced falls back to SQL Server.
 */
export function resolveDialect(csprojText: string | undefined): EditorDialect {
  if (!csprojText) return 'sqlserver';

  const hasSqlite = SQLITE_REF_RE.test(csprojText);
  const hasSqlServer = SQLSERVER_REF_RE.test(csprojText);
  const hasPostgres = POSTGRES_REF_RE.test(csprojText);

  const referencedCount = [hasSqlite, hasSqlServer, hasPostgres].filter(Boolean).length;
  if (referencedCount === 1) {
    if (hasSqlite) return 'sqlite';
    if (hasPostgres) return 'postgres';
    return 'sqlserver';
  }
  return 'sqlserver';
}

/**
 * True for a "temp-table-header" dialect (SQLite, PostgreSQL) — dialects whose
 * declaration form is `Create Temp Table <Prefix>_<Name> (...)` instead of
 * T-SQL `Declare @...` / `Use`. Mirrors the generator's
 * `Dialect is ITempTableHeaderDialect` check (SQuiLTokenizer.cs / SQuiLParser.cs) —
 * change one side, change all.
 */
export function isTempTableDialect(dialect: EditorDialect): boolean {
  return dialect === 'sqlite' || dialect === 'postgres';
}
