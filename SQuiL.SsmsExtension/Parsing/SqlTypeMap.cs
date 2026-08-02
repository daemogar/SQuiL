using System.Collections.Generic;

namespace SQuiL.SsmsExtension.Parsing;

/// <summary>
/// SQL → C# type mapping, matching the table in
/// <c>SQuiL.VSCodeExtension/src/squil/previewGenerator.ts</c> (and the smaller
/// duplicate in hoverProvider.ts).  Both VS Code editor surfaces use this set,
/// and the SQuiL source generator follows the same conventions.
/// </summary>
public static class SqlTypeMap
{
    private static readonly Dictionary<string, string> Map = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["bigint"]            = "long",
        ["binary"]            = "byte[]",
        ["bit"]               = "bool",
        ["char"]              = "string",
        ["date"]              = "DateOnly",
        ["datetime"]          = "DateTime",
        ["datetime2"]         = "DateTime",
        ["datetimeoffset"]    = "DateTimeOffset",
        ["decimal"]           = "decimal",
        ["float"]             = "double",
        ["image"]             = "byte[]",
        ["int"]               = "int",
        ["money"]             = "decimal",
        ["nchar"]             = "string",
        ["ntext"]             = "string",
        ["numeric"]           = "decimal",
        ["nvarchar"]          = "string",
        ["real"]              = "float",
        ["smalldatetime"]     = "DateTime",
        ["smallint"]          = "short",
        ["smallmoney"]        = "decimal",
        ["text"]              = "string",
        ["time"]              = "TimeOnly",
        ["timestamp"]         = "byte[]",
        ["tinyint"]           = "byte",
        ["uniqueidentifier"]  = "Guid",
        ["varbinary"]         = "byte[]",
        ["varchar"]           = "string",
        ["xml"]               = "string",
    };

    /// <summary>
    /// SQLite's type vocabulary overlays <see cref="Map"/> for keys whose CLR
    /// mapping differs by dialect (SQLite's <c>REAL</c> is an 8-byte double,
    /// not a 4-byte float; SQLite has no dedicated DATE storage class so both
    /// <c>DATE</c> and <c>DATETIME</c> map to <c>DateTime</c>). Keys absent
    /// here fall back to <see cref="Map"/> unchanged. Matches
    /// <c>SQL_TO_CS</c>/<c>SQLITE_TO_CS</c> in <c>previewGenerator.ts</c>.
    /// </summary>
    private static readonly Dictionary<string, string> SqliteMap = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["integer"]           = "long",
        ["text"]              = "string",
        ["real"]              = "double",
        ["blob"]              = "byte[]",
        ["numeric"]           = "decimal",
        ["boolean"]           = "bool",
        ["date"]              = "DateTime",
        ["datetime"]          = "DateTime",
        ["guid"]              = "Guid",
        ["uniqueidentifier"]  = "Guid",
    };

    /// <summary>
    /// PostgreSQL's type vocabulary overlays <see cref="Map"/> for keys whose CLR mapping
    /// differs by dialect, or whose spelling doesn't exist in the base SQL Server map at all
    /// (<c>int4</c>/<c>int8</c>/<c>int2</c>/<c>bpchar</c>/<c>bytea</c>/<c>uuid</c>/<c>bool</c>/
    /// <c>timestamptz</c>/<c>json</c>/<c>jsonb</c>, plus the ANSI long-form spellings
    /// <c>character varying</c>/<c>timestamp without time zone</c>/<c>timestamp with time
    /// zone</c>/<c>time without time zone</c>). Keys absent here fall back to <see cref="Map"/>
    /// unchanged. Matches the generator's PG type map (<c>Token.CSharpType()</c> /
    /// <c>PostgresDialect.ParamTypeExpr</c>) and <c>POSTGRES_CS</c>/<c>POSTGRES_TO_CS</c> in
    /// <c>hoverProvider.ts</c>/<c>previewGenerator.ts</c>.
    /// </summary>
    private static readonly Dictionary<string, string> PostgresMap = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["int2"]                        = "short",
        ["smallint"]                    = "short",
        ["int4"]                        = "int",
        ["int"]                         = "int",
        ["integer"]                     = "int",
        ["int8"]                        = "long",
        ["bigint"]                      = "long",
        ["text"]                        = "string",
        ["varchar"]                     = "string",
        ["char"]                        = "string",
        ["bpchar"]                      = "string",
        ["character varying"]           = "string",
        ["json"]                        = "string",
        ["jsonb"]                       = "string",
        ["bytea"]                       = "byte[]",
        ["uuid"]                        = "Guid",
        ["bool"]                        = "bool",
        ["boolean"]                     = "bool",
        ["timestamp"]                   = "DateTime",
        ["timestamp without time zone"] = "DateTime",
        ["timestamptz"]                 = "DateTimeOffset",
        ["timestamp with time zone"]    = "DateTimeOffset",
        ["date"]                        = "DateOnly",
        ["time"]                        = "TimeOnly",
        ["time without time zone"]      = "TimeOnly",
        ["numeric"]                     = "decimal",
        ["decimal"]                     = "decimal",
        ["money"]                       = "decimal",
        ["real"]                        = "float",
        ["float4"]                      = "float",
        ["double precision"]            = "double",
        ["float8"]                      = "double",
    };

    /// <summary>Strip any <c>(N)</c> qualifier and look up the base type, defaulting to the SQL Server dialect.</summary>
    public static string SqlToCSharp(string sqlType) => SqlToCSharp(sqlType, EditorDialect.SqlServer);

    /// <summary>Strip any <c>(N)</c> qualifier and look up the base type under <paramref name="dialect"/>.</summary>
    public static string SqlToCSharp(string sqlType, EditorDialect dialect)
    {
        if (string.IsNullOrEmpty(sqlType)) return "object";

        string baseType = sqlType.Trim();
        int paren = baseType.IndexOf('(');
        if (paren >= 0) baseType = baseType.Substring(0, paren).Trim();

        if (dialect == EditorDialect.Sqlite && SqliteMap.TryGetValue(baseType, out var sqliteCs))
            return sqliteCs;

        if (dialect == EditorDialect.Postgres && PostgresMap.TryGetValue(baseType, out var postgresCs))
            return postgresCs;

        return Map.TryGetValue(baseType, out var cs) ? cs : "object";
    }

    /// <summary>
    /// C# type for an entire variable, taking its role into account:
    ///   • <c>Params</c>/<c>Returns</c> → <c>IEnumerable&lt;Name&gt;</c>
    ///   • <c>ParamTable</c>/<c>ReturnTable</c> → <c>Name</c>
    ///   • everything else → scalar mapping of its SQL type.
    /// The <c>Table</c>/<c>Object</c> suffix was dropped in TODO #3 — the bare
    /// record name is used directly (matches the generator and the VS Code hover).
    /// Defaults to the SQL Server dialect.
    /// </summary>
    public static string GetCSharpType(SQuiLVariable v) => GetCSharpType(v, EditorDialect.SqlServer);

    /// <summary>Dialect-aware overload of <see cref="GetCSharpType(SQuiLVariable)"/>.</summary>
    public static string GetCSharpType(SQuiLVariable v, EditorDialect dialect) => v.Role switch
    {
        VariableRole.Params      => $"IEnumerable<{v.Name}>",
        VariableRole.Returns     => $"IEnumerable<{v.Name}>",
        VariableRole.ParamTable  => $"{v.Name}",
        VariableRole.ReturnTable => $"{v.Name}",
        _                        => SqlToCSharp(v.SqlType, dialect),
    };

    /// <summary>True for SQL types that become reference types in C#, defaulting to the SQL Server dialect.</summary>
    public static bool IsRefType(string sqlType) => IsRefType(sqlType, EditorDialect.SqlServer);

    /// <summary>Dialect-aware overload of <see cref="IsRefType(string)"/>.</summary>
    public static bool IsRefType(string sqlType, EditorDialect dialect)
    {
        var cs = SqlToCSharp(sqlType, dialect);
        return cs == "string" || cs == "byte[]";
    }
}
