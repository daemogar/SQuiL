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
 * (SP0033 ambiguous / SP0034 cycle), plus an editor-only orphan-PK hint
 * (SP0035) that only fires when at least one real link exists elsewhere in
 * the file (`hasLinks`).
 *
 * Change one side, change the other — `SQuiLKeyGraph.cs` ↔ this file.
 */

import { SQuiLVariable, TableColumn, VariableRole } from './parser';

export interface KeyGraphFinding {
  kind: 'ambiguous' | 'cycle' | 'orphan';
  /** The subject variable (child for ambiguous, cycle-start for cycle, PK owner for orphan). */
  variable: SQuiLVariable;
  /** The subject PK column (orphan only); undefined for ambiguous/cycle. */
  column?: TableColumn;
  /** The counterpart variable named in the message (other parent / cycle partner). */
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

  // Key column name (lowercased) -> owning variable(s). A variable's key = its
  // single Primary-Key column.
  const pkOwners = new Map<string, SQuiLVariable[]>();
  const pkColumnOf = new Map<SQuiLVariable, TableColumn>();
  for (const v of list) {
    const pk = v.columns.find(c => c.isPrimaryKey);
    if (!pk) continue;
    pkColumnOf.set(v, pk);
    const key = pk.name.toLowerCase();
    const owners = pkOwners.get(key);
    if (owners) { owners.push(v); } else { pkOwners.set(key, [v]); }
  }

  const errors: KeyGraphFinding[] = [];

  // R1: orientation follows declaration order, not which side owns the Primary Key.
  // `list` is already in declaration order, so its index is the declaration ordinal.
  const order = new Map<SQuiLVariable, number>();
  list.forEach((v, i) => order.set(v, i));

  // Distinct unordered pairs {block, pkOwner} that share a key column name.
  const pairSeen = new Set<string>();
  const edges: KeyGraphEdge[] = [];
  for (const block of list) {
    for (const col of block.columns) {
      const owners = pkOwners.get(col.name.toLowerCase());
      if (!owners) continue;
      for (const owner of owners) {
        if (owner === block) continue; // own PK column
        const lo = Math.min(order.get(block)!, order.get(owner)!);
        const hi = Math.max(order.get(block)!, order.get(owner)!);
        const id = `${lo}|${hi}|${col.name.toLowerCase()}`;
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
  }

  // childOf drives cycle detection below. Ambiguity handling (a block linked to more than one
  // container) is reintroduced under the new pair/order model in a later task — this task is the
  // orientation seam only, so `errors` collects cycles alone.
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
