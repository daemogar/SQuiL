/**
 * Hover text for a column's role in the nested-object key graph (`buildKeyGraph`): the key
 * owner's Primary Key, the other end's foreign key (classic child or embed container), an orphan
 * PK note, or undefined. Each column resolves against its own side's graph (OUTPUT or INPUT).
 * Mirrors `SQuiLLinter.DescribeColumnLinkRole` (SSMS + Visual Studio).
 */

import { SQuiLParseResult, SQuiLVariable, TableColumn, VariableRole } from './parser';
import { buildKeyGraph, OUTPUT_TABLE_ROLES, INPUT_TABLE_ROLES } from './keyGraph';

/** Exported for reuse by other editor-only helpers (semantic tokens, code actions)
 *  that need the same "table/object variable with columns, restricted to one
 *  role universe" filter — one implementation, not a third duplicated copy. */
export function tableVariablesFor(
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

    // Edges on this key: classic children nest under the owner; embed containers hold it as a lookup.
    const keyEdges = graph.edges.filter(e => e.keyName.toLowerCase() === column.name.toLowerCase());
    const parts: string[] = [];
    if (keyEdges.some(e => !e.isEmbed)) {
      parts.push(`child tables that carry a \`${column.name}\` column nest under \`${variable.name}\``);
    }
    const containers = keyEdges.filter(e => e.isEmbed).map(e => `\`${e.parent.name}\``);
    if (containers.length > 0) {
      parts.push(`\`${variable.name}\` embeds as a single lookup object into ${containers.join(', ')}`);
    }
    if (parts.length > 0) return `Primary Key — ${parts.join('; ')}.`;
    // Graceful degradation: in a file with no links at all, an "orphan" PK
    // note would fire on every table's PK, which is noise, not a hint. Only
    // surface the orphan note when at least one real link exists elsewhere
    // in the file (mirrors SP0035's `graph.hasLinks` gate).
    if (!graph.hasLinks) return undefined;
    return `Primary Key — no child table links to \`${column.name}\` yet; add a matching column on a child ` +
        `table to nest rows under \`${variable.name}\`.`;
  }

  // The key's non-owner side: the container of an embed, the child of a classic edge.
  const edge = graph.edges.find(
    e => (e.isEmbed ? e.parent : e.child) === variable && e.keyName.toLowerCase() === column.name.toLowerCase(),
  );
  if (edge?.isEmbed) {
    return `Foreign key by convention → the matching \`${edge.child.name}\` row embeds into \`${variable.name}\` ` +
      `as a single object (matched by \`${column.name}\`).`;
  }
  if (edge) {
    return `Foreign key by convention → rows of \`${variable.name}\` nest under \`${edge.parent.name}\` ` +
      `(matched by \`${column.name}\`).`;
  }

  return undefined;
}
