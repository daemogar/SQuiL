/**
 * Editor mirror of the generator's nested-object key graph (`SQuiLKeyGraph.cs`): one graph per
 * side (`OUTPUT_TABLE_ROLES` default, or `INPUT_TABLE_ROLES`), with SP0033/SP0034 errors and the
 * SP0035 orphan hint. Change one side, change the other.
 * Rules and rationale: `SQuiL.SourceGenerator/README.md`, "Nested objects: key graph".
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

  // R0: one PK owner per key name (lowercased); a second claimant is SP0033 and its marker is ignored.
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

  // R1: orientation follows declaration order (`list` order), not which side owns the key.
  const order = new Map<SQuiLVariable, number>();
  list.forEach((v, i) => order.set(v, i));

  // One edge per block pair (first matching key column wins). Matching is case-insensitive;
  // `keyName` keeps the carrier's spelling.
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

  // R3: a shared lookup keeps all its containers; a junction keeps the earliest and inverts the
  // rest into embeds. Runs to a fixed point; the bound is proven (README), so hitting it is a bug.
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

  // SP0034: DFS over EVERY edge (a shared lookup has several parents). R3 inversions make it reachable.
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
    // SP0035: a PK is an orphan when its key name is on no edge (embed owners are never a parent).
    for (const [v, col] of pkColumnOf) {
      if (!edges.some(e => e.keyName.toLowerCase() === col.name.toLowerCase())) {
        hints.push({ kind: 'orphan', variable: v, column: col, otherVariable: v });
      }
    }
  }

  return { edges, errors, hints, hasLinks };
}
