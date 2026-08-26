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
		// block, so iterate until stable.
		//
		// TERMINATION PROOF (review round 1, C2 — `guard < list.Count + 1` was NOT a valid bound;
		// one inversion can spawn several new conflicts, so iterations are not bounded by block
		// count): the invariant `IsEmbed == true` iff `Child` owns `KeyName` holds for every edge,
		// original or inverted (inversion is only performed when the DROPPED edge's Parent owns the
		// key, and the new edge's Child is that same Parent). Key ownership is unique per name
		// (R0/SP0033), so an embed edge, once dropped, can never be re-inverted — dropping it only
		// ever REMOVES an edge (`#edges` falls). A non-embed edge, when dropped, is ALWAYS inverted
		// (its Parent owns the key by construction — see the pairs loop above and R1's
		// `nestedOwnsKey`), so dropping it converts it to an embed edge (`#nonEmbed` falls, `#edges`
		// unchanged). A qualifying group (`byNested`) always has AT LEAST ONE non-embed edge — an
		// all-embed group is explicitly excluded by the `!g.All(...)` guard — so every iteration
		// drops at least one non-embed edge, meaning `#nonEmbed` strictly falls at least once every
		// `#nonEmbed` iterations, and `#edges` never rises. So the pair `(#nonEmbed, #edges)`,
		// ordered lexicographically, strictly decreases every iteration. Both counters are bounded
		// below by 0 and start at most `edges.Count`, so the loop terminates in at most
		// `2 * edges.Count` iterations. Hitting the bound below is therefore proof of a BUG in this
		// algorithm, not a possible shape of user input — it throws rather than silently returning a
		// half-resolved graph.
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

		// Cycle detection (review round 1, C1 — MUST walk the full `edges` set, never a
		// last-write-wins `childOf` map). After R3, a block can legitimately keep 2+ SURVIVING
		// parents (the "all embed" shared-lookup case), and R3's inversion can point an edge
		// backward in declaration order (breaking the `order(Parent) < order(Child)` invariant every
		// RAW pre-R3 edge satisfies). A `childOf[Child] = Parent` map — one entry per Child,
		// overwritten by whichever edge is enumerated last — can therefore lose the exact edge that
		// closes a cycle while keeping an unrelated, non-cyclic parent for that same Child, letting a
		// genuine cycle slip past SP0034 undetected. A missed cycle is not merely a wrong diagnostic:
		// `SQuiLDataContext.cs`'s `DeepestFirstEdges.Visit` recurses over exactly this edge set at
		// build-code-generation time with no cycle guard of its own, so an undetected cycle here
		// means unbounded recursion there — a stack overflow that kills the compiler process with no
		// diagnostic at all. Standard white/gray/black DFS over the true Parent -> Children adjacency
		// (every edge, not one-per-child) closes that gap: a GRAY (currently-on-the-DFS-stack) node
		// reached again is definitionally a cycle, regardless of how many OTHER parents that node
		// also has.
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
						// cv == 1: v is a GRAY ancestor on the current DFS path — the edge u -> v
						// closes a cycle back to v. Report once per cycle (both endpoints of the
						// closing edge are marked so re-discovering the same cycle from a different
						// back edge doesn't double-report it).
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

		// Roots = blocks that are not a child of anyone (declaration order). A block whose Primary
		// Key was rejected as a duplicate (R0, above) is treated as a root for degradation, but the
		// build error stops generation anyway.
		var hasParent = new HashSet<CodeBlock>(edges.Select(e => e.Child));
		var roots = list.Where(b => !hasParent.Contains(b)).ToList();

		// Orphan PK hint (review round 1, I3): a PK owner is orphaned when its key name is NOT the
		// KeyName of any surviving edge — NOT merely "not a Parent of any edge". The old
		// `!edges.Any(e => ReferenceEquals(e.Parent, kv.Key))` check false-positived on every embed
		// edge (the owner is the EDGE'S CHILD there, by definition — a shared lookup or an inverted
		// junction owner is legitimately linked, just never as a Parent) and on the INVERTED edges
		// R3 introduces. Since R0/SP0033 guarantees one owner per key name, matching by KeyName is
		// exact and doesn't care which side of the edge the owner ended up on.
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
