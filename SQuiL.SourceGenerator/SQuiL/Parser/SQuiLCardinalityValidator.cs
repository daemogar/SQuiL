namespace SQuiL.SourceGenerator.Parser;

/// <summary>
/// Detects cardinality collisions within a single SQuiL file: a base name declared
/// as BOTH a table (list: <c>@Params_</c>/<c>@Returns_</c>) AND a single object
/// (<c>@Param_…table</c>/<c>@Return_…table</c>) on the SAME side (both inputs → request,
/// or both outputs → response). The two declarations resolve to one request/response
/// property; the generator keeps the first and silently drops the rest. SP0022 flags
/// each dropped (second-and-later) declaration as a build error.
///
/// <para>
/// Cross-file sharing of a row record with differing cardinality is legitimate and is
/// NOT detected here (this operates on one file's blocks). Same rule, different parse
/// substrate, as <c>lintCardinalityCollision()</c> in <c>parser.ts</c> (VS Code) and
/// <c>LintCardinalityCollision()</c> in <c>SQuiLLinter.cs</c> (SSMS + Visual Studio) —
/// change one, change all.
/// </para>
/// </summary>
public static class SQuiLCardinalityValidator
{
    /// <summary>One cardinality-collision finding for a dropped (second-or-later) declaration.</summary>
    /// <param name="Name">The shared base name.</param>
    /// <param name="IsOutput"><c>true</c> when both declarations are outputs; <c>false</c> when both inputs.</param>
    /// <param name="DroppedIsTable"><c>true</c> when the dropped declaration is a table (list); <c>false</c> when a single object.</param>
    /// <param name="DroppedLine">1-based line of the dropped declaration.</param>
    /// <param name="FirstIsTable"><c>true</c> when the first (winning) declaration is a table (list); <c>false</c> when a single object.</param>
    /// <param name="FirstLine">1-based line of the first declaration.</param>
    public sealed record Finding(string Name, bool IsOutput, bool DroppedIsTable, int DroppedLine, bool FirstIsTable, int FirstLine);

    /// <summary>Returns one <see cref="Finding"/> per dropped declaration.</summary>
    public static List<Finding> Detect(IEnumerable<CodeBlock> blocks, string sql)
    {
        // Group table/object declarations by (side, name). Inputs feed the request,
        // outputs the response — a name shared across the two sides lands on different
        // models and never collides, so the side is part of the key. Each group's list
        // preserves declaration (parse) order, so group[0] is the winning declaration.
        var groups = new Dictionary<(bool IsOutput, string Name), List<CodeBlock>>();

        foreach (var block in blocks)
        {
            if (!block.IsTable && !block.IsObject) continue;

            var isOutput = (block.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT;
            var key = (isOutput, block.Name.ToUpperInvariant());

            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = [];
            list.Add(block);
        }

        var findings = new List<Finding>();
        foreach (var entry in groups)
        {
            var group = entry.Value;

            // Collision only when the group mixes a list (table) and a single object.
            if (!group.Exists(b => b.IsTable) || !group.Exists(b => b.IsObject)) continue;

            var first = group[0];
            var firstLine = LineOf(sql, first.DatabaseType.Offset);

            for (var i = 1; i < group.Count; i++)
            {
                var dropped = group[i];
                findings.Add(new Finding(
                    dropped.Name, entry.Key.IsOutput, dropped.IsTable,
                    LineOf(sql, dropped.DatabaseType.Offset), first.IsTable, firstLine));
            }
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
