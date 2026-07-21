/**
 * SQuiL Editor Dialect Discovery
 *
 * Determines whether the .csproj owning a .squil file targets SQL Server or
 * SQLite, so editor features (type vocabulary, completion, hover, preview)
 * can present the right SQL type keywords. Discovery reads the `<PackageReference>`
 * elements of the owning .csproj — the same package ids the source generator's
 * `DialectRegistry.ProviderPackageId` uses ("SQuiL.SqlServer" / "SQuiL.Sqlite").
 *
 * Port: SQuiL.SsmsExtension/Parsing/SQuiLDialect.cs and
 *       SQuiL.VisualStudioExtension/Parsing/SQuiLDialect.cs —
 *       change one side, change all.
 */

export type EditorDialect = 'sqlite' | 'sqlserver';

const SQLITE_REF_RE = /<PackageReference\s+Include\s*=\s*"SQuiL\.Sqlite"/i;
const SQLSERVER_REF_RE = /<PackageReference\s+Include\s*=\s*"SQuiL\.SqlServer"/i;

/**
 * Resolve the dialect from the raw text of a .csproj file.
 *
 * Returns `'sqlite'` iff the project references `SQuiL.Sqlite` and does NOT
 * also reference `SQuiL.SqlServer` (an explicit marker is preferred whenever
 * both — or neither — are referenced, matching the generator's "reference
 * both" consumer model default of SQL Server). Defaults to `'sqlserver'`.
 */
export function resolveDialect(csprojText: string | undefined): EditorDialect {
  if (!csprojText) return 'sqlserver';

  const hasSqlite = SQLITE_REF_RE.test(csprojText);
  const hasSqlServer = SQLSERVER_REF_RE.test(csprojText);

  if (hasSqlite && !hasSqlServer) return 'sqlite';
  return 'sqlserver';
}
