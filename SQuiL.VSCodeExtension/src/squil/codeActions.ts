/**
 * PK / link code-action logic for nested-object authoring (Task 16).
 *
 * Two authoring aids, both pure text-edit computations over the already-
 * parsed file (no `vscode` dependency here — see
 * `providers/codeActionProvider.ts` for the thin VS Code wrapper):
 *
 *   1. "Add Primary Key" — offered on a table/object variable that declares
 *      no Primary Key column yet. Inserts `Primary Key` after a sensible
 *      column's type (the first `*ID`-suffixed column, else the first
 *      column).
 *   2. "Link to `<Table>` via `<PK>`" — offered per OTHER declared table in
 *      the SAME universe (OUTPUT vs INPUT — never mixed, matching every
 *      other nested-object feature) that has a Primary Key this table
 *      doesn't already carry a matching column for. Selecting one inserts a
 *      new column (same name + type as the target's PK) into this table's
 *      declaration, wiring the relationship-by-convention.
 *
 * Both edits are computed against the RAW source lines (not the parsed
 * `sqlType`/`nullable` summaries) so the inserted text lands at an exact
 * (line, character) position — same "flatten + scan" approach used by
 * `parser.ts`'s `scanTableColumnPositions`.
 */

import { SQuiLParseResult, SQuiLVariable, TableColumn, VariableRole } from './parser';
import { OUTPUT_TABLE_ROLES, INPUT_TABLE_ROLES } from './keyGraph';
import { tableVariablesFor } from './linkRoleHints';

export type TableVariable = SQuiLVariable & { columns: TableColumn[] };

export interface SourcePosition {
  line: number;
  character: number;
}

export interface CodeActionEdit {
  title: string;
  position: SourcePosition;
  insertText: string;
}

/** Every declared table/object variable (both universes), for cursor-hit-testing. */
export function allTableVariables(parsed: SQuiLParseResult): TableVariable[] {
  return [
    ...tableVariablesFor(parsed, OUTPUT_TABLE_ROLES),
    ...tableVariablesFor(parsed, INPUT_TABLE_ROLES),
  ];
}

/** Table/object variables that declare NO Primary Key column. */
export function tablesMissingPrimaryKey(parsed: SQuiLParseResult): TableVariable[] {
  return allTableVariables(parsed).filter(v => !v.columns.some(c => c.isPrimaryKey));
}

/** Picks the column an "Add Primary Key" action should mark: the first
 *  column whose name ends in `ID` (case-insensitive), else the first column. */
export function chooseDefaultKeyColumn(table: TableVariable): TableColumn {
  return table.columns.find(c => /ID$/i.test(c.name)) ?? table.columns[0];
}

/** Finds the source position immediately after a column's `Name <Type>` token
 *  pair (e.g. right after `ParentID int`), so callers can insert a modifier
 *  like ` Primary Key` there. Assumes the column's type is on the same source
 *  line as its name — true for every column position `parser.ts` computes
 *  (one column's tokens never wrap mid-declaration in practice). Returns
 *  undefined if the line doesn't match the expected `name<ws>type` shape
 *  (defensive — should not happen for a column parsed from this same file). */
export function findColumnTypeEndPosition(
  lines: string[],
  column: TableColumn,
): SourcePosition | undefined {
  const line = lines[column.line];
  if (line === undefined) return undefined;
  const rest = line.slice(column.character);
  const m = rest.match(/^(\w+)\s+([\w]+(?:\([^)]*\))?)/i);
  if (!m) return undefined;
  return { line: column.line, character: column.character + m[0].length };
}

/** Finds the source position of the closing `)` of a table/object variable's
 *  `TABLE( ... )` declaration, so callers can insert a new trailing column
 *  just before it. Handles multi-line declarations and nested parens (e.g.
 *  `decimal(18,2)`) via depth tracking, mirroring `scanTableColumnPositions`
 *  in `parser.ts`. */
export function findTableCloseParenPosition(
  lines: string[],
  variable: SQuiLVariable,
): SourcePosition | undefined {
  const flatChars: string[] = [];
  const map: SourcePosition[] = [];
  for (let li = variable.line; li < lines.length; li++) {
    const content = lines[li];
    const begin = li === variable.line ? Math.min(Math.max(variable.character, 0), content.length) : 0;
    for (let ci = begin; ci < content.length; ci++) {
      flatChars.push(content[ci]);
      map.push({ line: li, character: ci });
    }
    if (li < lines.length - 1) {
      flatChars.push('\n');
      map.push({ line: li, character: content.length });
    }
  }

  const text = flatChars.join('');
  const openMatch = /\bTABLE\s*\(/i.exec(text);
  if (!openMatch) return undefined;

  let idx = openMatch.index + openMatch[0].length;
  let depth = 1;
  while (idx < text.length && depth > 0) {
    const c = text[idx];
    if (c === '(') depth++;
    else if (c === ')') {
      depth--;
      if (depth === 0) return map[idx];
    }
    idx++;
  }
  return undefined;
}

/** Returns the "line span" a table/object variable's declaration occupies —
 *  used to decide whether a cursor position falls "on" this variable for
 *  code-action purposes. Falls back to a single-line span when the closing
 *  paren can't be located. */
export function tableVariableLineSpan(
  lines: string[],
  variable: SQuiLVariable,
): { startLine: number; endLine: number } {
  const close = findTableCloseParenPosition(lines, variable);
  return { startLine: variable.line, endLine: close?.line ?? variable.line };
}

/** Builds the "Add Primary Key" edit for a table with no Primary Key, or
 *  undefined if the insertion point can't be located (defensive). */
export function buildAddPrimaryKeyEdit(lines: string[], table: TableVariable): CodeActionEdit | undefined {
  const column = chooseDefaultKeyColumn(table);
  if (!column) return undefined;
  const position = findColumnTypeEndPosition(lines, column);
  if (!position) return undefined;
  return {
    title: `SQuiL: Add Primary Key on \`${column.name}\``,
    position,
    insertText: ' Primary Key',
  };
}

/** A candidate parent this child could link to: another declared table/object
 *  in the SAME universe with a Primary Key this child doesn't already carry
 *  a matching column for (excludes self, excludes already-linked parents). */
export interface LinkTarget {
  parent: TableVariable;
  pkColumn: TableColumn;
}

export function availableLinkTargets(parsed: SQuiLParseResult, child: TableVariable): LinkTarget[] {
  const roles: ReadonlySet<VariableRole> = OUTPUT_TABLE_ROLES.has(child.role)
    ? OUTPUT_TABLE_ROLES
    : INPUT_TABLE_ROLES;
  const list = tableVariablesFor(parsed, roles);

  const childColumnNames = new Set(child.columns.map(c => c.name.toLowerCase()));

  const targets: LinkTarget[] = [];
  for (const candidate of list) {
    if (candidate === child) continue;
    const pk = candidate.columns.find(c => c.isPrimaryKey);
    if (!pk) continue;
    if (childColumnNames.has(pk.name.toLowerCase())) continue; // already linked
    targets.push({ parent: candidate, pkColumn: pk });
  }
  return targets;
}

/** Builds the "Link to `<Table>` via `<PK>`" edit: inserts a new trailing
 *  column (same name + type as the target's Primary Key) into the child's
 *  TABLE(...) declaration, just before the closing paren. */
export function buildInsertLinkColumnEdit(
  lines: string[],
  child: TableVariable,
  target: LinkTarget,
): CodeActionEdit | undefined {
  const position = findTableCloseParenPosition(lines, child);
  if (!position) return undefined;
  return {
    title: `SQuiL: Link to \`${target.parent.name}\` via \`${target.pkColumn.name}\``,
    position,
    insertText: `, ${target.pkColumn.name} ${target.pkColumn.sqlType}`,
  };
}

/** True when `line` falls within `variable`'s declaration span (inclusive). */
export function isCursorOnVariable(lines: string[], variable: SQuiLVariable, line: number): boolean {
  const span = tableVariableLineSpan(lines, variable);
  return line >= span.startLine && line <= span.endLine;
}
