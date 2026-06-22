/**
 * Nullability hint pass (SP0010).
 *
 * Produces a plain-data hint descriptor for every TABLE column and every
 * scalar `param`/`return` variable that has no explicit NULL / NOT NULL
 * marker.  The caller (diagnosticsProvider) converts these into
 * vscode.Diagnostic objects; unit tests consume the raw descriptors directly
 * — no vscode dependency here.
 */

import { SQuiLParseResult, VariableRole } from './parser';
import { sqlToCSharp } from './previewGenerator';

export interface NullabilityHint {
  code: 'SP0010';
  message: string;
  line: number;
  character: number;
  /** Length of the token to underline (the column/scalar name). */
  length: number;
}

/** Variable roles for which a scalar-level hint is produced. */
const SCALAR_HINT_ROLES: ReadonlySet<VariableRole> = new Set(['param', 'return']);

/**
 * Return all SP0010 hint descriptors for the given parse result.
 */
export function nullabilityHints(parsed: SQuiLParseResult): NullabilityHint[] {
  const hints: NullabilityHint[] = [];

  for (const v of parsed.variables) {
    if (v.columns && v.columns.length) {
      // Table variable — check each column individually.
      for (const col of v.columns) {
        if (col.nullabilityMarker === undefined) {
          const csType = sqlToCSharp(col.sqlType);
          hints.push({
            code: 'SP0010',
            message:
              `No \`null\`/\`not null\` marker — generated C# is non-nullable \`${csType} ${col.name}\`. ` +
              `Add \`not null\` to confirm, or \`null\` to make it nullable.`,
            // TableColumn does not track its own line/character; fall back to the
            // declaring variable's position (acceptable for v1).
            line: v.line,
            character: v.character,
            length: col.name.length,
          });
        }
      }
    } else if (SCALAR_HINT_ROLES.has(v.role) && v.nullabilityMarker === undefined) {
      const csType = sqlToCSharp(v.sqlType);
      hints.push({
        code: 'SP0010',
        message:
          `No \`null\`/\`not null\` marker — generated C# is non-nullable \`${csType} ${v.name}\`. ` +
          `Add \`not null\` to confirm, or \`null\` to make it nullable.`,
        line: v.line,
        character: v.character,
        length: v.name.length,
      });
    }
  }

  return hints;
}
