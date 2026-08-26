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
  // until stable. `guard` bounds the loop against a pathological graph.
  for (let guard = 0; guard < list.length + 1; guard++) {
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

  // childOf drives cycle detection below. After R3 (above), a block can still legitimately appear
  // as Child in 2+ SURVIVING edges (the "all embed" shared-lookup case) — `childOf` keeps only the
  // LAST one written (last-write-wins), which is fine here: consumers that enumerate nested
  // members filter the full `edges` array by `parent`, not `childOf`, so a shared lookup still
  // nests under every one of its containers. `childOf` itself is only a cycle-detection convenience.
  const childOf = new Map<SQuiLVariable, SQuiLVariable>();
  for (const e of edges) childOf.set(e.child, e.parent);

  // Cycle / self-reference detection over the childOf map. Report each cycle
  // ONCE and name the actual partner (cur) whose FK closes the loop back to start.
  const reportedCycle = new Set<SQuiLVariable>();
  for (const start of list) {
    if (reportedCycle.has(start)) continue;
    const seen = new Set<SQuiLVariable>();
    let cur: SQuiLVariable = start;
    while (childOf.has(cur)) {
      const next = childOf.get(cur)!;
      if (next === start) {
        errors.push({ kind: 'cycle', variable: start, otherVariable: cur });
        // Mark every member of this cycle so it is not re-reported from another start.
        reportedCycle.add(start);
        let w: SQuiLVariable = start;
        while (childOf.has(w)) {
          const n = childOf.get(w)!;
          if (reportedCycle.has(n)) break;
          reportedCycle.add(n);
          w = n;
        }
        break;
      }
      if (seen.has(next)) break;
      seen.add(next);
      cur = next;
    }
  }

  const hasLinks = edges.length > 0;
  const hints: KeyGraphFinding[] = [];
  if (hasLinks) {
    // Orphan PK = a PK no child links to.
    for (const [v, col] of pkColumnOf) {
      if (!edges.some(e => e.parent === v)) {
        hints.push({ kind: 'orphan', variable: v, column: col, otherVariable: v });
      }
    }
  }

  return { edges, errors, hints, hasLinks };
}
