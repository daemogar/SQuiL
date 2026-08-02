using System.Text.RegularExpressions;

namespace SQuiL.VisualStudioExtension.Parsing;

/// <summary>
/// The SQuiL editor dialect — determines which SQL type vocabulary (and CLR
/// mapping) the editor surfaces for a given <c>.squil</c> file, mirroring the
/// generator's <c>ISqlDialect</c> seam (TODO #6 / Phase 3B).
/// </summary>
public enum EditorDialect
{
    SqlServer,
    Sqlite,
    Postgres,
}

/// <summary>
/// Determines whether the .csproj owning a .squil file targets SQL Server,
/// SQLite, or PostgreSQL, so editor features (type vocabulary, completion,
/// hover, preview) can present the right SQL type keywords. Discovery reads
/// the <c>&lt;PackageReference&gt;</c> elements of the owning .csproj — the
/// same package ids the source generator's
/// <c>DialectRegistry.ProviderPackageId</c> uses ("SQuiL.SqlServer" /
/// "SQuiL.Sqlite" / "SQuiL.Postgres").
///
/// Port of <c>SQuiL.VSCodeExtension/src/squil/dialect.ts</c> —
/// change one side, change all.
/// </summary>
public static class SQuiLDialect
{
    private static readonly Regex SqliteRefRegex =
        new(@"<PackageReference\s+Include\s*=\s*""SQuiL\.Sqlite""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SqlServerRefRegex =
        new(@"<PackageReference\s+Include\s*=\s*""SQuiL\.SqlServer""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PostgresRefRegex =
        new(@"<PackageReference\s+Include\s*=\s*""SQuiL\.Postgres""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Resolve the dialect from the raw text of a .csproj file.
    ///
    /// Returns the single referenced provider's dialect (<see cref="EditorDialect.Sqlite"/> /
    /// <see cref="EditorDialect.Postgres"/> / <see cref="EditorDialect.SqlServer"/>) when
    /// EXACTLY one of the three provider packages is referenced. Falls back to
    /// <see cref="EditorDialect.SqlServer"/> when zero or 2+ providers are referenced (an
    /// explicit marker is preferred whenever the choice is ambiguous), matching the
    /// generator's <c>DialectRegistry.ResolveId</c> — a single referenced provider wins;
    /// 0 or 2+ referenced falls back to SQL Server.
    /// </summary>
    public static EditorDialect ResolveDialect(string? csprojText)
    {
        if (string.IsNullOrEmpty(csprojText)) return EditorDialect.SqlServer;

        bool hasSqlite = SqliteRefRegex.IsMatch(csprojText);
        bool hasSqlServer = SqlServerRefRegex.IsMatch(csprojText);
        bool hasPostgres = PostgresRefRegex.IsMatch(csprojText);

        int referencedCount = (hasSqlite ? 1 : 0) + (hasSqlServer ? 1 : 0) + (hasPostgres ? 1 : 0);
        if (referencedCount == 1)
        {
            if (hasSqlite) return EditorDialect.Sqlite;
            if (hasPostgres) return EditorDialect.Postgres;
            return EditorDialect.SqlServer;
        }
        return EditorDialect.SqlServer;
    }

    /// <summary>
    /// True for a "temp-table-header" dialect (SQLite, PostgreSQL) — dialects whose
    /// declaration form is <c>Create Temp Table &lt;Prefix&gt;_&lt;Name&gt; (...)</c> instead of
    /// T-SQL <c>Declare @...</c> / <c>Use</c>. Mirrors the generator's
    /// <c>Dialect is ITempTableHeaderDialect</c> check (SQuiLTokenizer.cs / SQuiLParser.cs) —
    /// change one side, change all.
    /// </summary>
    public static bool IsTempTableDialect(EditorDialect dialect)
        => dialect == EditorDialect.Sqlite || dialect == EditorDialect.Postgres;
}
