namespace SQuiL.SourceGenerator.Parser;

using SQuiL.Models;

using System.Collections.Generic;

/// <summary>
/// SP0030: within one query file, two @Return/@Returns outputs whose ordered
/// canonical signatures are identical cannot be routed apart at runtime. All colliding
/// declarations are flagged, cross-referencing each other. Length/precision and
/// varchar-vs-nvarchar fold into the canonical token, so those differences collide.
/// </summary>
public static class SQuiLShapeCollisionValidator
{
    public sealed record Finding(string Name, string OtherName, bool IsOutput, bool IsTable, int Line, int OtherLine, bool IsWinner);

    public static List<Finding> Detect(IEnumerable<CodeBlock> blocks, string sql)
    {
        // Only OUTPUT table/object blocks route by shape (inputs are JSON params).
        var outputs = new List<CodeBlock>();
        foreach (var block in blocks)
        {
            if (!block.IsTable && !block.IsObject) continue;
            if ((block.CodeType & CodeType.OUTPUT) != CodeType.OUTPUT) continue;
            outputs.Add(block);
        }

        // Group by identical ordered signature key.
        var groups = new Dictionary<string, List<CodeBlock>>();
        foreach (var block in outputs)
        {
            var key = SQuiLShapeKey.ShapeKeyOf(block);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(block);
        }

        var findings = new List<Finding>();
        foreach (var group in groups.Values)
        {
            // Same base name is a #3 merge, not a collision — only DISTINCT names collide.
            var distinct = new List<CodeBlock>();
            var seenNames = new HashSet<string>();
            foreach (var b in group)
                if (seenNames.Add(b.Name.ToUpperInvariant())) distinct.Add(b);
            if (distinct.Count < 2) continue;

            var winner = distinct[0];
            var winnerLine = LineOf(sql, winner.DatabaseType.Offset);
            for (var i = 0; i < distinct.Count; i++)
            {
                var self = distinct[i];
                var other = i == 0 ? distinct[1] : winner;
                findings.Add(new Finding(
                    self.Name, other.Name,
                    IsOutput: true, self.IsTable,
                    LineOf(sql, self.DatabaseType.Offset),
                    LineOf(sql, other.DatabaseType.Offset),
                    IsWinner: i == 0));
            }
        }
        return findings;
    }

    // Copied from SQuiLCardinalityValidator — 1-based line for a character offset.
    private static int LineOf(string sql, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset && i < sql.Length; i++)
            if (sql[i] == '\n') line++;
        return line;
    }
}
