/**
 * Nested-object link role text for hover/QuickInfo (Task 11).
 *
 * Given a source position, finds the table-column token (if any) at that
 * exact position and — reusing the same parent/child resolution as the
 * SP0033/SP0034/SP0035 diagnostics (`buildKeyGraph` in `./keyGraph.ts`) —
 * explains its role in the nested-object graph:
 *   - a Primary Key column with 1+ children linking to it → "Primary Key…"
 *   - a Primary Key column with no children (orphan) → a short note,
 *     matching SP0035's spirit
 *   - a non-PK column that matches another table's PK by convention (a
 *     resolved FK edge) → "Foreign key by convention…"
 *   - anything else (not on a column, or a column that plays no link role)
 *     → undefined, so hover is left completely unchanged (graceful
 *     degradation — a no-links file surfaces no link text at all).
 *
 * Covers BOTH the OUTPUT (`@Return_`/`@Returns_`) and INPUT (`@Param_`/
 * `@Params_`) table/object universes — a hovered column resolves its role
 * against whichever graph its own variable belongs to, never mixing the two
 * (matches the generator's two independent graphs).
 *
 * Ported to `SQuiLQuickInfoSource.cs` (SSMS + Visual Studio, via the shared
 * `SQuiLLinter.DescribeColumnLinkRole`) — change one side, change all three.
 */

import { SQuiLParseResult, SQuiLVariable, TableColumn, VariableRole } from './parser';
import { buildKeyGraph, OUTPUT_TABLE_ROLES, INPUT_TABLE_ROLES } from './keyGraph';

function tableVariablesFor(
  parsed: SQuiLParseResult,
  roles: ReadonlySet<VariableRole>,
): (SQuiLVariable & { columns: TableColumn[] })[] {
  return parsed.variables.filter(
    (v): v is SQuiLVariable & { columns: TableColumn[] } =>
      roles.has(v.role) && Array.isArray(v.columns) && v.columns.length > 0,
  );
}

/** Finds the table-column token (owning variable + column) whose NAME token
 * covers (line, character), or undefined when the position isn't on one.
 * Searches OUTPUT variables first, then INPUT — a position can only ever
 * land on one variable's column, so the search order is not observable. */
export function findColumnAtPosition(
  parsed: SQuiLParseResult,
  line: number,
  character: number,
): { variable: SQuiLVariable; column: TableColumn } | undefined {
  for (const roles of [OUTPUT_TABLE_ROLES, INPUT_TABLE_ROLES]) {
    for (const variable of tableVariablesFor(parsed, roles)) {
      const column = variable.columns.find(
        c => c.line === line && character >= c.character && character <= c.character + c.name.length,
      );
      if (column) return { variable, column };
    }
  }
  return undefined;
}

/** Describe the nested-object link role of the column at (line, character),
 * or undefined when there is none (unchanged hover). */
export function describeColumnLinkRole(
  parsed: SQuiLParseResult,
  line: number,
  character: number,
): string | undefined {
  const hit = findColumnAtPosition(parsed, line, character);
  if (!hit) return undefined;
  const { variable, column } = hit;

  // Resolve against the SAME universe the hovered variable belongs to —
  // never mix OUTPUT and INPUT columns into one graph.
  const roles = OUTPUT_TABLE_ROLES.has(variable.role) ? OUTPUT_TABLE_ROLES : INPUT_TABLE_ROLES;
  const list = tableVariablesFor(parsed, roles);
  const graph = buildKeyGraph(list, roles);

  if (column.isPrimaryKey) {
    // Only the variable's OWN designated Primary Key column (the first one
    // found) plays the PK role — mirrors buildKeyGraph's `pkColumnOf`.
    const ownPk = list.find(v => v === variable)?.columns.find(c => c.isPrimaryKey);
    if (ownPk !== column) return undefined;

    const hasChild = graph.edges.some(e => e.parent === variable);
    if (hasChild) {
      return `Primary Key — child tables that carry a \`${column.name}\` column nest under \`${variable.name}\`.`;
    }
    // Graceful degradation: in a file with no links at all, an "orphan" PK
    // note would fire on every table's PK, which is noise, not a hint. Only
    // surface the orphan note when at least one real link exists elsewhere
    // in the file (mirrors SP0035's `graph.hasLinks` gate).
    if (!graph.hasLinks) return undefined;
    return `Primary Key — no child table links to \`${column.name}\` yet; add a matching column on a child ` +
        `table to nest rows under \`${variable.name}\`.`;
  }

  const edge = graph.edges.find(
    e => e.child === variable && e.keyName.toLowerCase() === column.name.toLowerCase(),
  );
  if (edge) {
    return `Foreign key by convention → rows of \`${variable.name}\` nest under \`${edge.parent.name}\` ` +
      `(matched by \`${column.name}\`).`;
  }

  return undefined;
}
