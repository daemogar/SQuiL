namespace SQuiL.Models;

using SQuiL.SourceGenerator.Parser;
using System.Collections.Generic;
using System.Linq;

/// <summary>One container→nested relationship. <paramref name="Parent"/> is the container,
/// <paramref name="Child"/> the nested block, and <paramref name="KeyName"/> the shared key column.
/// <paramref name="IsEmbed"/> is true when <paramref name="Child"/> OWNS the primary key (a
/// many-to-one lookup embedded into its FK carrier) and false when <paramref name="Child"/> merely
/// carries it (the classic one-to-many child).</summary>
public sealed record SQuiLKeyEdge(CodeBlock Parent, CodeBlock Child, string KeyName, bool IsEmbed);

/// <summary>A relationship diagnostic. <c>Kind</c> ∈ "duplicate-pk" | "cycle" | "orphan".</summary>
public sealed record SQuiLKeyFinding(string Kind, string Name, string OtherName, int Line, int OtherLine);

/// <summary>
/// Container/nested graph over one query file's OUTPUT (or INPUT) table/object blocks, linked by
/// Primary Keys and same-named columns. No links means the flat model.
/// </summary>
/// <remarks>Rules R0–R4, termination and cycles: SQuiL.SourceGenerator README, "Nested objects: key graph".</remarks>
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

		var errors = new List<SQuiLKeyFinding>();

		// R0: one PK owner per key name; a second claimant is SP0033 and its marker is ignored.
		var pkOwners = new Dictionary<string, CodeBlock>(System.StringComparer.OrdinalIgnoreCase);
		var pkNameOf = new Dictionary<CodeBlock, string>();
		foreach (var b in list)
		{
			var pk = b.Properties?.FirstOrDefault(p => p.IsPrimaryKey);
			if (pk is null) continue;
			var k = pk.Identifier.Value;
			if (pkOwners.TryGetValue(k, out var first))
			{
				errors.Add(new("duplicate-pk", b.Name, first.Name,
					LineOf(sql, b.DatabaseType.Offset), LineOf(sql, first.DatabaseType.Offset)));
				continue;
			}
			pkOwners[k] = b;
			pkNameOf[b] = k;
		}

		// R1: orientation follows declaration order (`list` order), not which side owns the key.
		var order = new Dictionary<CodeBlock, int>();
		for (var i = 0; i < list.Count; i++) order[list[i]] = i;

		// One edge per block pair (first matching key column wins), else duplicate members (CS0102).
		var pairs = new List<(CodeBlock A, CodeBlock B, string Key)>();
		var pairSeen = new HashSet<(int, int)>();
		foreach (var block in list)
		{
			foreach (var col in block.Properties ?? [])
			{
				if (!pkOwners.TryGetValue(col.Identifier.Value, out var owner)) continue;
				if (ReferenceEquals(owner, block)) continue;      // its own PK column
				var lo = System.Math.Min(order[block], order[owner]);
				var hi = System.Math.Max(order[block], order[owner]);
				if (!pairSeen.Add((lo, hi))) continue;
				pairs.Add((list[lo], list[hi], col.Identifier.Value));
			}
		}

		// R1: the earlier-declared block is the container. IsEmbed when the nested block owns the key.
		var edges = new List<SQuiLKeyEdge>();
		foreach (var (a, b, key) in pairs)
		{
			var nestedOwnsKey = pkNameOf.TryGetValue(b, out var bKey)
				&& string.Equals(bKey, key, System.StringComparison.OrdinalIgnoreCase);
			edges.Add(new(a, b, key, nestedOwnsKey));
		}

		// R3: a shared lookup keeps all its containers; a junction keeps the earliest and inverts the
		// rest into embeds. Runs to a fixed point; the bound is proven (README), so hitting it is a bug.
		var guardLimit = 2 * edges.Count;
		for (var guard = 0; ; guard++)
		{
			var byNested = edges.GroupBy(e => e.Child)
				.FirstOrDefault(g => g.Count() > 1 && !g.All(e => e.IsEmbed));
			if (byNested is null) break;
			if (guard >= guardLimit)
				throw new System.InvalidOperationException(
					$"SQuiLKeyGraph R3 resolution did not reach a fixed point within {guardLimit} " +
					"iterations. This violates the algorithm's proven termination bound and indicates " +
					"a bug in SQuiLKeyGraph.Build's R3 loop, not a malformed query file.");

			var ordered = byNested.OrderBy(e => order[e.Parent]).ToList();
			foreach (var drop in ordered.Skip(1))
			{
				edges.Remove(drop);
				// Invert only when the dropped container owns the key — otherwise there is nothing
				// to embed and the link is simply discarded.
				if (pkNameOf.TryGetValue(drop.Parent, out var parentKey)
					&& string.Equals(parentKey, drop.KeyName, System.StringComparison.OrdinalIgnoreCase))
					edges.Add(new(drop.Child, drop.Parent, drop.KeyName, true));
			}
		}

		// SP0034: DFS over EVERY edge (a shared lookup has several parents). R3 inversions make it reachable.
		var childrenOf = new Dictionary<CodeBlock, List<CodeBlock>>();
		foreach (var e in edges)
		{
			if (!childrenOf.TryGetValue(e.Parent, out var kids))
				childrenOf[e.Parent] = kids = new List<CodeBlock>();
			kids.Add(e.Child);
		}

		var color = new Dictionary<CodeBlock, int>(); // 0 = unvisited (absent), 1 = gray (on stack), 2 = black (done)
		var reportedCycle = new HashSet<CodeBlock>();

		void Dfs(CodeBlock u)
		{
			color[u] = 1;
			if (childrenOf.TryGetValue(u, out var kids))
				foreach (var v in kids)
				{
					if (color.TryGetValue(v, out var cv))
					{
						if (cv == 2) continue;             // already fully explored — no cycle through here
						// Gray: u -> v closes a cycle. Mark both ends so it is reported once.
						if (!reportedCycle.Contains(u) && !reportedCycle.Contains(v))
							errors.Add(new("cycle", u.Name, v.Name,
								LineOf(sql, u.DatabaseType.Offset), LineOf(sql, v.DatabaseType.Offset)));
						reportedCycle.Add(u);
						reportedCycle.Add(v);
						continue;
					}
					Dfs(v);
				}
			color[u] = 2;
		}

		foreach (var start in list)
			if (!color.ContainsKey(start))
				Dfs(start);

		// Roots = blocks that are no one's child, in declaration order.
		var hasParent = new HashSet<CodeBlock>(edges.Select(e => e.Child));
		var roots = list.Where(b => !hasParent.Contains(b)).ToList();

		// SP0035: a PK is an orphan when its key name is on no edge (embed owners are never a Parent).
		var hasLinks = edges.Count > 0;
		var hints = new List<SQuiLKeyFinding>();
		if (hasLinks)
			foreach (var kv in pkNameOf)
				if (!edges.Any(e => string.Equals(e.KeyName, kv.Value, System.StringComparison.OrdinalIgnoreCase)))
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
