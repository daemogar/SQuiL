/**
 * Orphaned Primary-Key hint pass (SP0035).
 *
 * Editor-only Hint (VS Code Hint severity, C# Info severity) — NOT a
 * build/generator diagnostic. Fires when a table/object variable declares a
 * `Primary Key` column that NO other table/object in the file links to (no
 * matching-named column anywhere else) — but ONLY when nesting is already
 * "in play" in that same universe, i.e. at least one real parent/child link
 * exists elsewhere (`hasLinks`). A deliberately-flat file whose tables happen
 * to each declare an unrelated Primary Key must NOT be nagged.
 *
 * Applied to BOTH the OUTPUT (`@Return_`/`@Returns_`) and INPUT (`@Param_`/
 * `@Params_`) key graphs independently — `hasLinks` is evaluated per-graph, so
 * an orphaned output PK is never masked by an unrelated input link (or vice
 * versa), matching the generator's two independent graphs.
 *
 * Mirrors `SQuiLKeyGraph.Hints` (`SQuiL.SourceGenerator/SQuiL/Models/SQuiLKeyGraph.cs`)
 * and `LintKeyGraph`'s orphan branch in `SQuiLLinter.cs` (SSMS + Visual Studio) —
 * change one side, change all three.
 *
 * The caller (diagnosticsProvider) converts these into vscode.Diagnostic
 * objects; unit tests consume the raw descriptors directly — no vscode
 * dependency here.
 */

import { SQuiLParseResult } from './parser';
import { buildKeyGraph, OUTPUT_TABLE_ROLES, INPUT_TABLE_ROLES } from './keyGraph';

export interface NestedObjectHint {
  code: 'SP0035';
  message: string;
  line: number;
  character: number;
  /** Length of the token to underline (the Primary Key column name). */
  length: number;
}

/**
 * Return all SP0035 hint descriptors for the given parse result.
 */
export function nestedObjectHints(parsed: SQuiLParseResult): NestedObjectHint[] {
  const outputGraph = buildKeyGraph(parsed.variables, OUTPUT_TABLE_ROLES);
  const inputGraph = buildKeyGraph(parsed.variables, INPUT_TABLE_ROLES);

  return [...outputGraph.hints, ...inputGraph.hints].map(finding => {
    const col = finding.column!;
    const v = finding.variable;
    return {
      code: 'SP0035',
      message:
        `Primary Key \`${col.name}\` on \`${v.name}\` has no child linking to it — no nesting will occur; ` +
        `add a matching column on a child table, or remove the key.`,
      line: col.line,
      character: col.character,
      length: col.name.length,
    };
  });
}
