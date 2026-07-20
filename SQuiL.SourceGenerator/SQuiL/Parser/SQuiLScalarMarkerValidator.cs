namespace SQuiL.SourceGenerator.Parser;

using System.Collections.Generic;

/// <summary>
/// SP0037: a standalone <c>null</c>/<c>not null</c> marker on a scalar <c>Declare</c> is invalid
/// T-SQL — <c>Declare</c> statements don't support nullability modifiers. Flags any SCALAR
/// declaration (not a table/object) whose declare carried a standalone marker rather than an
/// <c>= null</c> initializer. Table-column markers are unaffected (out of scope).
/// </summary>
public static class SQuiLScalarMarkerValidator
{
    public sealed record Finding(string Name, int Line);

    public static List<Finding> Detect(IEnumerable<CodeBlock> blocks, string sql)
    {
        var findings = new List<Finding>();
        foreach (var block in blocks)
        {
            if (block.IsTable || block.IsObject) continue;

            if (block.HasScalarNullabilityMarker)
                findings.Add(new Finding(block.Name, LineOf(sql, block.DatabaseType.Offset)));
        }
        return findings;
    }

    private static int LineOf(string sql, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset && i < sql.Length; i++)
            if (sql[i] == '\n') line++;
        return line;
    }
}
