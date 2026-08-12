/**
 * Implicit scalar select alias hint (SP0042).
 *
 * Port of ScalarSelectAliaser.cs (source generator) — change one, change all four.
 * EDITOR-ONLY — must NOT appear in the source generator.
 *
 * The generator auto-appends `" As [<Name>]"` to a bare `Select @Return_X;` (SQL Server
 * only — a bare variable select returns an UNNAMED column that the runtime shape-key
 * router can't match; see ScalarSelectAliaser.cs). Rather than let that rewrite happen
 * invisibly, this hint surfaces it to the author so they can write the alias themselves —
 * accepting the offered quick-fix inserts the exact bracketed form the generator would
 * have emitted.
 *
 * Like every other Hint in this extension (see nullabilityHints.ts), this module produces
 * plain-data descriptors with no `vscode` dependency — the caller (diagnosticsProvider)
 * converts them into `vscode.Diagnostic`s with `vscode.DiagnosticSeverity.Hint` hardcoded,
 * since `SQuiLDiagnostic.severity` has no Hint level.
 */

import { SQuiLParseResult, buildScalarsByVariableName, findBareScalarSelects, offsetToPosition } from './parser';
import { EditorDialect, isTempTableDialect } from './dialect';

export interface ScalarAliasHint {
  code: 'SP0042';
  message: string;
  line: number;
  character: number;
  /** Length of the token to underline (the `@Return_<Name>` token). */
  length: number;
  declaredName: string;
}

/**
 * Port of ScalarSelectAliaser.cs (source generator) — change one, change all four.
 * EDITOR-ONLY — must NOT appear in the source generator.
 *
 * Returns every SP0042 hint for a bare single-scalar select in `bodyText`. `bodyText`'s
 * lines are BODY-RELATIVE (0-based within `bodyText`, not the full document) — mirroring
 * `lintUnmatchedSelect`'s (SP0031) treatment of the same body-text slice; the caller adds
 * its own line offset back to get a document-absolute position.
 *
 * SQL-Server-only: SQLite and PostgreSQL declare scalars as single-column temp tables and
 * select a real named column, so there is nothing to alias — returns `[]` immediately for
 * either temp-table-header dialect.
 */
export function scalarAliasHints(
  parsed: SQuiLParseResult,
  bodyText: string,
  dialect: EditorDialect,
): ScalarAliasHint[] {
  if (isTempTableDialect(dialect)) return [];

  const scalarsByVariableName = buildScalarsByVariableName(parsed.variables);
  if (scalarsByVariableName.size === 0) return [];

  const hints: ScalarAliasHint[] = [];
  for (const bare of findBareScalarSelects(bodyText, scalarsByVariableName)) {
    const pos = offsetToPosition(bodyText, bare.variableOffset);
    hints.push({
      code: 'SP0042',
      message: `The generator supplies \`As [${bare.declaredName}]\`; add it to make the column name explicit.`,
      line: pos.line,
      character: pos.character,
      length: bare.variableLength,
      declaredName: bare.declaredName,
    });
  }
  return hints;
}
