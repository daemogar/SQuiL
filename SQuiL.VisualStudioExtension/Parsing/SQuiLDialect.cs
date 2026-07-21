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
}

/// <summary>
/// Determines whether the .csproj owning a .squil file targets SQL Server or
/// SQLite, so editor features (type vocabulary, completion, hover, preview)
/// can present the right SQL type keywords. Discovery reads the
/// <c>&lt;PackageReference&gt;</c> elements of the owning .csproj — the same
/// package ids the source generator's <c>DialectRegistry.ProviderPackageId</c>
/// uses ("SQuiL.SqlServer" / "SQuiL.Sqlite").
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

    /// <summary>
    /// Resolve the dialect from the raw text of a .csproj file.
    ///
    /// Returns <see cref="EditorDialect.Sqlite"/> iff the project references
    /// <c>SQuiL.Sqlite</c> and does NOT also reference <c>SQuiL.SqlServer</c>
    /// (an explicit marker is preferred whenever both — or neither — are
    /// referenced, matching the generator's "reference both" consumer model
    /// default of SQL Server). Defaults to <see cref="EditorDialect.SqlServer"/>.
    /// </summary>
    public static EditorDialect ResolveDialect(string? csprojText)
    {
        if (string.IsNullOrEmpty(csprojText)) return EditorDialect.SqlServer;

        bool hasSqlite = SqliteRefRegex.IsMatch(csprojText);
        bool hasSqlServer = SqlServerRefRegex.IsMatch(csprojText);

        if (hasSqlite && !hasSqlServer) return EditorDialect.Sqlite;
        return EditorDialect.SqlServer;
    }
}
