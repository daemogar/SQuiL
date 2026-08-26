/**
 * Editor mirror of the generator's nested-object key graph
 * (`SQuiL.SourceGenerator/SQuiL/Models/SQuiLKeyGraph.cs`).
 *
 * Build-time container/nested graph inferred from Primary-Key columns and
 * matching-named "foreign key by convention" columns, over one query file's
 * table/object blocks. Two blocks that share a key column name are linked;
 * ORIENTATION follows declaration order — the earlier-declared block is
 * always the container (`parent`), regardless of which side owns the
 * Primary Key (`isEmbed` records which). Graceful degradation: no PKs / no
 * matches → no links (flat model).
 *
 * Two independent universes participate, never mixed in the same graph —
 * matches the generator, which calls `SQuiLKeyGraph.Build` once for OUTPUT
 * blocks and once for INPUT blocks (`FileGenerator.cs`'s `keyGraph` /
 * `inputGraph`). Pass `OUTPUT_TABLE_ROLES` (the default) for `@Return_`/
 * `@Returns_` blocks, or `INPUT_TABLE_ROLES` for `@Param_`/`@Params_` blocks.
 *
 * Detects the same two error findings the generator reports as build errors
 * (SP0033 duplicate-pk / SP0034 cycle), plus an editor-only orphan-PK hint
 * (SP0035) that only fires when at least one real link exists elsewhere in
 * the file (`hasLinks`).
 *
 * Change one side, change the other — `SQuiLKeyGraph.cs` ↔ this file.
 */

import { SQuiLVariable, TableColumn, VariableRole } from './parser';

export interface KeyGraphFinding {
  kind: 'duplicate-pk' | 'cycle' | 'orphan';
  /** The subject variable (second declarer for duplicate-pk, cycle-start for cycle, PK owner for orphan). */
  variable: SQuiLVariable;
  /** The subject PK column (orphan only); undefined for duplicate-pk/cycle. */
  column?: TableColumn;
  /** The counterpart variable named in the message (first declarer / cycle partner). */
  otherVariable: SQuiLVariable;
}

export interface KeyGraphEdge {
  parent: SQuiLVariable;
  child: SQuiLVariable;
  keyName: string;
  /** True when `child` OWNS the key (embedded lookup); false for a classic FK-carrier child. */
  isEmbed: boolean;
}

export interface KeyGraphResult {
  edges: KeyGraphEdge[];
  errors: KeyGraphFinding[];
  hints: KeyGraphFinding[];
  hasLinks: boolean;
}

/** OUTPUT table/object roles — the default universe (matches the generator's OUTPUT graph). */
export const OUTPUT_TABLE_ROLES: ReadonlySet<VariableRole> = new Set(['returns', 'return-table']);
/** INPUT table/object roles — the `@Param_`/`@Params_` universe (matches the generator's INPUT graph). */
export const INPUT_TABLE_ROLES: ReadonlySet<VariableRole> = new Set(['params', 'param-table']);

export function buildKeyGraph(
  variables: SQuiLVariable[],
  roles: ReadonlySet<VariableRole> = OUTPUT_TABLE_ROLES,
): KeyGraphResult {
  const list = variables.filter(
    (v): v is SQuiLVariable & { columns: TableColumn[] } =>
      roles.has(v.role) && Array.isArray(v.columns) && v.columns.length > 0,
  );

  const errors: KeyGraphFinding[] = [];

  // R0 (Ruling R0): exactly one variable may declare a Primary Key on a given key name — a key
  // name identifies one relationship, so it can have only one "one" side. Key column name
  // (lowercased) -> its single owning variable. A SECOND variable claiming a key name already
  // owned is a duplicate-primary-key error (SP0033) and does NOT enter pkOwners/pkColumnOf — its
  // (invalid) Primary Key marker is ignored for every purpose below (edge orientation, isEmbed,
  // orphan hints).
  const pkOwners = new Map<string, SQuiLVariable>();
  const pkColumnOf = new Map<SQuiLVariable, TableColumn>();
  for (const v of list) {
    const pk = v.columns.find(c => c.isPrimaryKey);
    if (!pk) continue;
    const key = pk.name.toLowerCase();
    const first = pkOwners.get(key);
    if (first) {
      errors.push({ kind: 'duplicate-pk', variable: v, otherVariable: first });
      continue;
    }
    pkOwners.set(key, v);
    pkColumnOf.set(v, pk);
  }

  // R1: orientation follows declaration order, not which side owns the Primary Key.
  // `list` is already in declaration order, so its index is the declaration ordinal.
  const order = new Map<SQuiLVariable, number>();
  list.forEach((v, i) => order.set(v, i));

  // Distinct unordered pairs {block, pkOwner} that share a key column name. Dedupe is keyed on the
  // PAIR alone (lo, hi) — NOT (lo, hi, key) — so two blocks connected by two different reciprocal
  // key columns (each side's column matching the other's Primary Key) still yield exactly one
  // edge. The first matching key column found (declaration order over blocks, then columns) wins,
  // mirroring `SQuiLKeyGraph.Build`'s `pairSeen`/`pairs`. Without this, a pair like `@Return_A
  // table(AID int Primary Key, BID int)` / `@Return_B table(BID int Primary Key, AID int)` would
  // produce two edges with the same parent/child. Since R0 (above) guarantees at most one owner
  // per key name, each matching column now yields at most one candidate pair. Key-name comparisons
  // throughout are lower-cased for case-insensitive matching (matching the generator's
  // `OrdinalIgnoreCase`); the stored `keyName` itself keeps the author's original casing.
  const pairSeen = new Set<string>();
  const edges: KeyGraphEdge[] = [];
  for (const block of list) {
    for (const col of block.columns) {
      const owner = pkOwners.get(col.name.toLowerCase());
      if (!owner) continue;
      if (owner === block) continue; // own PK column
      const lo = Math.min(order.get(block)!, order.get(owner)!);
      const hi = Math.max(order.get(block)!, order.get(owner)!);
      const id = `${lo}|${hi}`;
      if (pairSeen.has(id)) continue;
      pairSeen.add(id);
      const nested = list[hi];
      const nestedPk = pkColumnOf.get(nested);
      edges.push({
        parent: list[lo],
        child: nested,
        keyName: col.name,
        isEmbed: !!nestedPk && nestedPk.name.toLowerCase() === col.name.toLowerCase(),
      });
    }
  }

  // R3: a block with more than one container is either a shared lookup (it owns the key in EVERY
  // such edge — allowed, each container references the same row) or a junction / mixed case (keep
  // the earliest-declared container; invert the rest so the dropped container becomes an embed
  // INTO this block). Dropping or inverting can create a NEW multi-container block, so iterate
  // until stable.
  //
  // TERMINATION PROOF (review round 1, C2 — `guard < list.length + 1` was NOT a valid bound; see
  // keyGraph.ts's C# twin, SQuiLKeyGraph.cs, for the full proof): the pair `(#nonEmbed, #edges)`,
  // ordered lexicographically, strictly decreases every iteration — every qualifying group has at
  // least one non-embed edge (an all-embed group never qualifies), and dropping a non-embed edge
  // always inverts it (`#nonEmbed` falls), while dropping an already-embed edge never re-inverts
  // (`#edges` falls, since key ownership is unique per name — R0/SP0033). Both counters are
  // bounded below by 0 and start at most `edges.length`, so the loop terminates within
  // `2 * edges.length` iterations. Hitting that bound is proof of a bug in this algorithm, not a
  // possible SQuiL file.
  const guardLimit = 2 * edges.length;
  for (let guard = 0; ; guard++) {
    const groups = new Map<SQuiLVariable, KeyGraphEdge[]>();
    for (const e of edges) {
      const g = groups.get(e.child);
      if (g) g.push(e); else groups.set(e.child, [e]);
    }
    let byNested: KeyGraphEdge[] | undefined;
    for (const g of groups.values()) {
      if (g.length > 1 && !g.every(e => e.isEmbed)) { byNested = g; break; }
    }
    if (!byNested) break;
    if (guard >= guardLimit) {
      throw new Error(
        `buildKeyGraph R3 resolution did not reach a fixed point within ${guardLimit} iterations. ` +
        'This violates the algorithm\'s proven termination bound and indicates a bug in the R3 loop, ' +
        'not a malformed query file.',
      );
    }

    const ordered = [...byNested].sort((a, b) => order.get(a.parent)! - order.get(b.parent)!);
    for (const drop of ordered.slice(1)) {
      edges.splice(edges.indexOf(drop), 1);
      // Invert only when the dropped container owns the key — otherwise there is nothing to
      // embed and the link is simply discarded.
      const parentKey = pkColumnOf.get(drop.parent);
      if (parentKey && parentKey.name.toLowerCase() === drop.keyName.toLowerCase()) {
        edges.push({ parent: drop.child, child: drop.parent, keyName: drop.keyName, isEmbed: true });
      }
    }
  }

  // Cycle detection (review round 1, C1 — MUST walk the full `edges` array, never a
  // last-write-wins `childOf` map; mirrors SQuiLKeyGraph.cs's identical fix). After R3, a block
  // can legitimately keep 2+ SURVIVING parents (the "all embed" shared-lookup case), and R3's
  // inversion can point an edge backward in declaration order. A `childOf` map — one entry per
  // Child, overwritten by whichever edge is enumerated last — can lose the exact edge that closes
  // a cycle while keeping an unrelated, non-cyclic parent for that same Child, letting a genuine
  // cycle slip past undetected. Standard white/gray/black DFS over the true parent -> children
  // adjacency (every edge, not one per child) closes that gap.
  const childrenOf = new Map<SQuiLVariable, SQuiLVariable[]>();
  for (const e of edges) {
    const kids = childrenOf.get(e.parent);
    if (kids) kids.push(e.child); else childrenOf.set(e.parent, [e.child]);
  }

  const color = new Map<SQuiLVariable, 1 | 2>(); // 1 = gray (on stack), 2 = black (done); absent = unvisited
  const reportedCycle = new Set<SQuiLVariable>();

  function dfs(u: SQuiLVariable): void {
    color.set(u, 1);
    for (const v of childrenOf.get(u) ?? []) {
      const cv = color.get(v);
      if (cv === 2) continue; // already fully explored — no cycle through here
      if (cv === 1) {
        // v is a GRAY ancestor on the current DFS path — u -> v closes a cycle back to v.
        if (!reportedCycle.has(u) && !reportedCycle.has(v)) {
          errors.push({ kind: 'cycle', variable: u, otherVariable: v });
        }
        reportedCycle.add(u);
        reportedCycle.add(v);
        continue;
      }
      dfs(v);
    }
    color.set(u, 2);
  }

  for (const start of list) {
    if (!color.has(start)) dfs(start);
  }

  const hasLinks = edges.length > 0;
  const hints: KeyGraphFinding[] = [];
  if (hasLinks) {
    // Orphan PK hint (review round 1, I3): a PK owner is orphaned when its key name is NOT the
    // keyName of any surviving edge — NOT merely "not a parent of any edge". The old
    // `!edges.some(e => e.parent === v)` check false-positived on every embed edge (the owner is
    // the edge's CHILD there) and on every R3-inverted edge. Since R0/SP0033 guarantees one owner
    // per key name, matching by keyName is exact regardless of which side of the edge the owner
    // ended up on.
    for (const [v, col] of pkColumnOf) {
      if (!edges.some(e => e.keyName.toLowerCase() === col.name.toLowerCase())) {
        hints.push({ kind: 'orphan', variable: v, column: col, otherVariable: v });
      }
    }
  }

  return { edges, errors, hints, hasLinks };
}
