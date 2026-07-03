/**
 * Similar-signature hint pass (SP0020).
 *
 * Produces a plain-data hint descriptor for every pair of TABLE/OBJECT
 * variables (differently-named) that share an EXACT column signature
 * (same names, type tokens, nullability, and order; sizes may differ because
 * sqlType is compared lowercased as written in the DECLARE).
 *
 * "Same shape — different name" is often an accidental typo; this editor-only
 * hint (VS Code Hint severity, C# Info severity) prompts the author to unify
 * them under a single name so only one generated record type is emitted.
 *
 * The caller (diagnosticsProvider) converts these into vscode.Diagnostic
 * objects; unit tests consume the raw descriptors directly — no vscode
 * dependency here.
 */

import { SQuiLParseResult, SQuiLVariable, TableColumn, VariableRole } from './parser';
import { shapeKeyOf } from './shapeKey';

export interface ShapeHint {
  code: 'SP0020';
  message: string;
  line: number;
  character: number;
  /** Length of the token to underline (the raw `@...` variable token). */
  length: number;
}

/** Variable roles that produce table/object record types. */
const TABLE_ROLES: ReadonlySet<VariableRole> = new Set([
  'params',
  'returns',
  'param-table',
  'return-table',
]);

/**
 * Build a canonical signature string for a column list.
 * Format: `name:sqltype(size-stripped,lowercased):N|NN` per column, joined by `|`.
 * Size is stripped — `varchar(100)` and `varchar(50)` produce the same signature
 * because the generator's SameShape comparison is size-independent (sizes may differ).
 * Intent: catch accidentally-renamed copies of the same logical declaration.
 */
function columnSignature(cols: TableColumn[]): string {
  return cols
    .map(c => `${c.name}:${c.sqlType.replace(/\s*\([^)]*\)/, '').toLowerCase()}:${c.nullable ? 'N' : 'NN'}`)
    .join('|');
}

/**
 * Return all SP0020 hint descriptors for the given parse result.
 *
 * One hint is emitted per participating declaration (two hints for a
 * two-table match, three for a three-way match, etc.).  Each hint names
 * one counterpart in its message.
 */
export function shapeHints(parsed: SQuiLParseResult): ShapeHint[] {
  // Collect all table/object variables that have at least one column.
  const tables = parsed.variables.filter(
    (v): v is SQuiLVariable & { columns: TableColumn[] } =>
      TABLE_ROLES.has(v.role) && Array.isArray(v.columns) && v.columns.length > 0,
  );

  if (tables.length < 2) return [];

  // SP0030 reconciliation: compute the set of output variable rawNames that are
  // already flagged by SP0030 (same-file same-side output pairs with identical
  // canonical shape key).  Those pairs must NOT also fire SP0020.
  const outputRoles: ReadonlySet<VariableRole> = new Set(['returns', 'return-table']);
  const sp0030CollisionNames = new Set<string>();
  {
    const outputTables = tables.filter(v => outputRoles.has(v.role));
    const outputBySig = new Map<string, typeof outputTables>();
    for (const v of outputTables) {
      const key = shapeKeyOf(v.columns);
      const existing = outputBySig.get(key);
      if (existing) { existing.push(v); } else { outputBySig.set(key, [v]); }
    }
    for (const group of outputBySig.values()) {
      const distinct = group.filter(
        (v, i) => group.findIndex(g => g.name.toLowerCase() === v.name.toLowerCase()) === i,
      );
      if (distinct.length >= 2) {
        for (const v of distinct) sp0030CollisionNames.add(v.rawName);
      }
    }
  }

  // Build a signature → [variables] map.  Variables with the same base name
  // are NOT compared here — that's SP0017's domain (same-name different-shape).
  const bySig = new Map<string, typeof tables>();
  for (const v of tables) {
    const sig = columnSignature(v.columns);
    const group = bySig.get(sig);
    if (group) {
      group.push(v);
    } else {
      bySig.set(sig, [v]);
    }
  }

  const hints: ShapeHint[] = [];

  for (const group of bySig.values()) {
    if (group.length < 2) continue;

    // Filter out same-name groups (SP0017 handles those).
    // SQL identifiers are case-insensitive — lowercase to match the C# copies'
    // OrdinalIgnoreCase comparison.
    const distinctNames = new Set(group.map(v => v.name.toLowerCase()));
    if (distinctNames.size < 2) continue;

    // For each variable in the group, emit one hint pointing at the first
    // other variable whose name differs.
    for (let i = 0; i < group.length; i++) {
      const a = group[i];

      // SP0030 reconciliation: skip variables that are already flagged by SP0030
      // (same-file same-side output pair with identical canonical shape).
      if (sp0030CollisionNames.has(a.rawName)) continue;

      // Find the first differently-named partner to mention in the message.
      const partner = group.find(
        (b, j) => j !== i && b.name.toLowerCase() !== a.name.toLowerCase(),
      );
      if (!partner) continue;

      hints.push({
        code: 'SP0020',
        message:
          `\`${a.name}\` has the same column signature as \`${partner.name}\` ` +
          `(line ${partner.line + 1}). ` +
          `If these are the same shape, give them the same name to share one generated type.`,
        line: a.line,
        character: a.character,
        length: a.rawName.length,
      });
    }
  }

  return hints;
}
