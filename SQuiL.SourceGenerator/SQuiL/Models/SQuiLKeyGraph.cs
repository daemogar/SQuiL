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
/// Build-time container/nested graph inferred from Primary-Key columns and matching-named
/// "foreign key by convention" columns, over one query file's OUTPUT (or INPUT) table/object blocks.
/// Two blocks that share a key column name are linked; ORIENTATION follows declaration order — the
/// earlier-declared block is always the container (<see cref="SQuiLKeyEdge.Parent"/>), regardless of
/// which side owns the Primary Key (<see cref="SQuiLKeyEdge.IsEmbed"/> records which). Graceful
/// degradation: no PKs / no matches → no links (today's flat model).
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

		var errors = new List<SQuiLKeyFinding>();

		// R0 (this task): exactly one block may declare `Primary Key` on a given key name — a
		// key name identifies one relationship, so it can have only one "one" side. Key column
		// name -> its single owning block. A SECOND block claiming a key name already owned is a
		// build error (SP0033, "duplicate-pk") and does NOT enter pkOwners/pkNameOf — its
		// (invalid) Primary Key marker is ignored for every purpose below (edge orientation,
		// IsEmbed, orphan hints).
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

		// R1: orientation follows declaration order, not which side owns the Primary Key.
		// `list` is already in declaration order, so its index is the declaration ordinal.
		var order = new Dictionary<CodeBlock, int>();
		for (var i = 0; i < list.Count; i++) order[list[i]] = i;

		// Distinct unordered pairs {block, pkOwner} that share a key column name. Dedupe is keyed
		// on the PAIR alone (lo, hi) — NOT (lo, hi, key) — so two blocks connected by two different
		// reciprocal key columns (each side's column matching the other's Primary Key) still yield
		// exactly one edge. The first matching key column found (declaration order over blocks,
		// then columns) wins, mirroring the pre-R1 algorithm's `matches[0].Key`. Without this, a
		// pair like `@Return_A table(AID int Primary Key, BID int)` / `@Return_B table(BID int
		// Primary Key, AID int)` would produce two edges with the same Parent/Child — one property
		// emitted per edge — and duplicate members (CS0102). Since R0 (above) guarantees at most
		// one owner per key name, each matching column now yields at most one candidate pair.
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

		// R3: a block with more than one container is either a shared lookup (it owns the key in
		// EVERY such edge — allowed, each container references the same row) or a junction / mixed
		// case (keep the earliest-declared container; invert the rest so the dropped container
		// becomes an embed INTO this block). Dropping or inverting can create a NEW multi-container
		// block, so iterate until stable. `guard` bounds the loop against a pathological graph.
		for (var guard = 0; guard < list.Count + 1; guard++)
		{
			var byNested = edges.GroupBy(e => e.Child)
				.FirstOrDefault(g => g.Count() > 1 && !g.All(e => e.IsEmbed));
			if (byNested is null) break;

			var ordered = byNested.OrderBy(e => order[e.Parent]).ToList();
			var keep = ordered[0];
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

		// childOf drives cycle detection and root computation below. After R3 (above), a block can
		// still legitimately appear as Child in 2+ SURVIVING edges (the "all embed" shared-lookup
		// case) — `childOf` keeps only the LAST one written (matching the editors' documented
		// last-write-wins convention), which is fine here: `ChildrenOf(parent)` (the method
		// consumers actually use to enumerate nested members) filters the full `edges` list by
		// `Parent`, not `childOf`, so a shared lookup still nests under every one of its containers.
		// `childOf` itself is only a cycle-detection/roots convenience.
		var childOf = new Dictionary<CodeBlock, CodeBlock>();
		foreach (var e in edges) childOf[e.Child] = e.Parent;

		// Cycle / self-reference detection over the childOf map. Report each cycle ONCE
		// and name the actual partner (cur) whose FK closes the loop back to start.
		var reportedCycle = new HashSet<CodeBlock>();
		foreach (var start in list)
		{
			if (reportedCycle.Contains(start)) continue;
			var seen = new HashSet<CodeBlock>();
			var cur = start;
			while (childOf.TryGetValue(cur, out var next))
			{
				if (ReferenceEquals(next, start))
				{
					errors.Add(new("cycle", start.Name, cur.Name,
						LineOf(sql, start.DatabaseType.Offset), LineOf(sql, cur.DatabaseType.Offset)));
					// Mark every member of this cycle so it is not re-reported from another start.
					reportedCycle.Add(start);
					var w = start;
					while (childOf.TryGetValue(w, out var n) && reportedCycle.Add(n))
						w = n;
					break;
				}
				if (!seen.Add(next)) break;
				cur = next;
			}
		}

		// Roots = blocks that are not a child of anyone (declaration order). A block whose Primary
		// Key was rejected as a duplicate (R0, above) is treated as a root for degradation, but the
		// build error stops generation anyway.
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
