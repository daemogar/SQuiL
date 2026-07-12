namespace SQuiL.Models;

using SQuiL.SourceGenerator.Parser;
using System.Collections.Generic;
using System.Linq;

/// <summary>One parent→child relationship: the child carries a column named <paramref name="KeyName"/>
/// that matches the parent's Primary-Key column name.</summary>
public sealed record SQuiLKeyEdge(CodeBlock Parent, CodeBlock Child, string KeyName);

/// <summary>A relationship diagnostic. <c>Kind</c> ∈ "ambiguous" | "cycle" | "orphan".</summary>
public sealed record SQuiLKeyFinding(string Kind, string Name, string OtherName, int Line, int OtherLine);

/// <summary>
/// Build-time parent/child graph inferred from Primary-Key columns and matching-named
/// "foreign key by convention" columns, over one query file's OUTPUT (or INPUT) table/object blocks.
/// A table's key = its single Primary-Key column name; any OTHER block carrying a column of that
/// exact name is its child. Graceful degradation: no PKs / no matches → no links (today's flat model).
/// </summary>
public sealed class SQuiLKeyGraph
{
    private readonly List<SQuiLKeyEdge> _edges;
    private readonly List<CodeBlock> _roots;
    private readonly List<SQuiLKeyFinding> _errors;
    private readonly List<SQuiLKeyFinding> _hints;

    private SQuiLKeyGraph(List<SQuiLKeyEdge> edges, List<CodeBlock> roots,
        List<SQuiLKeyFinding> errors, List<SQuiLKeyFinding> hints)
    { _edges = edges; _roots = roots; _errors = errors; _hints = hints; }

    public bool HasLinks => _edges.Count > 0;
    public IReadOnlyList<SQuiLKeyEdge> Edges => _edges;
    public IReadOnlyList<CodeBlock> Roots => _roots;
    public IReadOnlyList<SQuiLKeyFinding> Errors => _errors;
    public IReadOnlyList<SQuiLKeyFinding> Hints => _hints;

    public IReadOnlyList<SQuiLKeyEdge> ChildrenOf(CodeBlock parent)
        => _edges.Where(e => ReferenceEquals(e.Parent, parent)).ToList();

    public static SQuiLKeyGraph Build(IEnumerable<CodeBlock> blocks, string sql)
    {
        var list = blocks.Where(b => b.IsTable || b.IsObject).ToList();

        // key column name -> owning block(s). A block's key = its single Primary-Key column.
        var pkOwners = new Dictionary<string, List<CodeBlock>>(System.StringComparer.OrdinalIgnoreCase);
        var pkNameOf = new Dictionary<CodeBlock, string>();
        foreach (var b in list)
        {
            var pk = b.Properties?.FirstOrDefault(p => p.IsPrimaryKey);
            if (pk is null) continue;
            var k = pk.Identifier.Value;
            pkNameOf[b] = k;
            if (!pkOwners.TryGetValue(k, out var owners)) pkOwners[k] = owners = [];
            owners.Add(b);
        }

        var edges = new List<SQuiLKeyEdge>();
        var errors = new List<SQuiLKeyFinding>();
        var childOf = new Dictionary<CodeBlock, CodeBlock>();

        foreach (var child in list)
        {
            // Which declared keys does this block carry a matching column for (excluding its own PK)?
            var matches = new List<(string Key, CodeBlock Parent)>();
            foreach (var col in child.Properties ?? [])
            {
                if (!pkOwners.TryGetValue(col.Identifier.Value, out var owners)) continue;
                foreach (var owner in owners)
                {
                    if (ReferenceEquals(owner, child)) continue;          // own PK column
                    matches.Add((col.Identifier.Value, owner));
                }
            }
            if (matches.Count == 0) continue;

            // A child column matching >1 distinct parent → ambiguous (graph must be a tree).
            var distinctParents = matches.Select(m => m.Parent).Distinct().ToList();
            if (distinctParents.Count > 1)
            {
                var other = distinctParents.First(p => !ReferenceEquals(p, distinctParents[0]));
                errors.Add(new("ambiguous", child.Name, distinctParents[0].Name,
                    LineOf(sql, child.DatabaseType.Offset), LineOf(sql, other.DatabaseType.Offset)));
                continue;
            }

            var parent = distinctParents[0];
            edges.Add(new(parent, child, matches[0].Key));
            childOf[child] = parent;
        }

        // Cycle / self-reference detection over the childOf map.
        foreach (var start in list)
        {
            var seen = new HashSet<CodeBlock>();
            var cur = start;
            while (childOf.TryGetValue(cur, out var next))
            {
                if (ReferenceEquals(next, start))
                {
                    errors.Add(new("cycle", start.Name, next.Name,
                        LineOf(sql, start.DatabaseType.Offset), LineOf(sql, next.DatabaseType.Offset)));
                    break;
                }
                if (!seen.Add(next)) break;
                cur = next;
            }
        }

        // Roots = blocks that are not a child of anyone (declaration order). Ambiguous children
        // are treated as roots for degradation but the build error stops generation anyway.
        var roots = list.Where(b => !childOf.ContainsKey(b)).ToList();

        var hasLinks = edges.Count > 0;
        var hints = new List<SQuiLKeyFinding>();
        if (hasLinks)
            foreach (var kv in pkNameOf)              // orphan PK = a PK no child links to
                if (!edges.Any(e => ReferenceEquals(e.Parent, kv.Key)))
                    hints.Add(new("orphan", kv.Key.Name, "", LineOf(sql, kv.Key.DatabaseType.Offset), 0));

        return new SQuiLKeyGraph(edges, roots, errors, hints);
    }

    private static int LineOf(string sql, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset && i < sql.Length; i++)
            if (sql[i] == '\n') line++;
        return line;
    }
}
