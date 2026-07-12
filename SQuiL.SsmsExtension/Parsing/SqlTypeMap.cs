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

    /// <summary>Strip any <c>(N)</c> qualifier and look up the base type.</summary>
    public static string SqlToCSharp(string sqlType)
    {
        if (string.IsNullOrEmpty(sqlType)) return "object";

        string baseType = sqlType.Trim();
        int paren = baseType.IndexOf('(');
        if (paren >= 0) baseType = baseType.Substring(0, paren).Trim();

        return Map.TryGetValue(baseType, out var cs) ? cs : "object";
    }

    /// <summary>
    /// C# type for an entire variable, taking its role into account:
    ///   • <c>Params</c>/<c>Returns</c> → <c>IEnumerable&lt;Name&gt;</c>
    ///   • <c>ParamTable</c>/<c>ReturnTable</c> → <c>Name</c>
    ///   • everything else → scalar mapping of its SQL type.
    /// The <c>Table</c>/<c>Object</c> suffix was dropped in TODO #3 — the bare
    /// record name is used directly (matches the generator and the VS Code hover).
    /// </summary>
    public static string GetCSharpType(SQuiLVariable v) => v.Role switch
    {
        VariableRole.Params      => $"IEnumerable<{v.Name}>",
        VariableRole.Returns     => $"IEnumerable<{v.Name}>",
        VariableRole.ParamTable  => $"{v.Name}",
        VariableRole.ReturnTable => $"{v.Name}",
        _                        => SqlToCSharp(v.SqlType),
    };

    /// <summary>True for SQL types that become reference types in C#.</summary>
    public static bool IsRefType(string sqlType)
    {
        var cs = SqlToCSharp(sqlType);
        return cs == "string" || cs == "byte[]";
    }
}
