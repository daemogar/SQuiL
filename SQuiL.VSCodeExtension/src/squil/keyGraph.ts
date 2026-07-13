/**
 * Editor mirror of the generator's nested-object key graph
 * (`SQuiL.SourceGenerator/SQuiL/Models/SQuiLKeyGraph.cs`).
 *
 * Build-time parent/child graph inferred from Primary-Key columns and
 * matching-named "foreign key by convention" columns, over one query file's
 * OUTPUT (`@Return_`/`@Returns_`) table/object blocks. A table's key is its
 * single Primary-Key column name; any OTHER block carrying a column of that
 * exact name is its child. Graceful degradation: no PKs / no matches → no
 * links (flat model).
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
}

export interface KeyGraphResult {
  edges: KeyGraphEdge[];
  errors: KeyGraphFinding[];
  hints: KeyGraphFinding[];
  hasLinks: boolean;
}

/** Only OUTPUT table/object variables participate — input nesting is out of
 * scope, matching the generator (which builds its graph from OUTPUT blocks only). */
const OUTPUT_TABLE_ROLES: ReadonlySet<VariableRole> = new Set(['returns', 'return-table']);

export function buildKeyGraph(variables: SQuiLVariable[]): KeyGraphResult {
  const list = variables.filter(
    (v): v is SQuiLVariable & { columns: TableColumn[] } =>
      OUTPUT_TABLE_ROLES.has(v.role) && Array.isArray(v.columns) && v.columns.length > 0,
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

  const edges: KeyGraphEdge[] = [];
  const errors: KeyGraphFinding[] = [];
  const childOf = new Map<SQuiLVariable, SQuiLVariable>();

  for (const child of list) {
    // Which declared keys does this variable carry a matching column for
    // (excluding its own PK)?
    const matches: { key: string; parent: SQuiLVariable }[] = [];
    for (const col of child.columns) {
      const owners = pkOwners.get(col.name.toLowerCase());
      if (!owners) continue;
      for (const owner of owners) {
        if (owner === child) continue; // own PK column
        matches.push({ key: col.name, parent: owner });
      }
    }
    if (matches.length === 0) continue;

    // A child column matching >1 distinct parent → ambiguous (graph must be a tree).
    const distinctParents = matches.map(m => m.parent).filter((p, i, arr) => arr.indexOf(p) === i);
    if (distinctParents.length > 1) {
      const other = distinctParents.find(p => p !== distinctParents[0])!;
      errors.push({ kind: 'ambiguous', variable: child, otherVariable: other });
      continue;
    }

    const parent = distinctParents[0];
    edges.push({ parent, child, keyName: matches[0].key });
    childOf.set(child, parent);
  }

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
