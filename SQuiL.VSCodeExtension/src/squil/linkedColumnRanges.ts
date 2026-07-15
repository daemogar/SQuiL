/**
 * Relationship-key column ranges for semantic-token coloring (Task 16).
 *
 * Given a parsed SQuiL file, returns the source (line, character, length)
 * span of every column NAME token that plays a role in the nested-object
 * PK/FK-by-convention graph: a parent's designated Primary Key column, and
 * every child column that resolves to it (`buildKeyGraph` in `./keyGraph.ts`
 * — the same graph the SP0033/SP0034/SP0035 diagnostics and the hover-role
 * text in `linkRoleHints.ts` already use).
 *
 * Classification only — never emits a diagnostic. Graceful degradation: a
 * file with no links produces zero ranges (matches `graph.hasLinks`).
 *
 * Covers BOTH the OUTPUT (`@Return_`/`@Returns_`) and INPUT (`@Param_`/
 * `@Params_`) universes independently, never mixed — matches every other
 * nested-object editor feature.
 *
 * Consumed by `providers/semanticTokensProvider.ts`. No C# port exists for
 * this exact range list — the SSMS/Visual Studio classifiers derive their
 * own linked-span list directly from `SQuiLLinter.BuildKeyGraph` (see
 * `SQuiLLinkedKeyClassifier.cs`) rather than porting this file line-for-line,
 * since the two hosts use different span representations (LSP-style semantic
 * tokens vs. VS `ClassificationSpan`s).
 */

import { SQuiLParseResult } from './parser';
import { buildKeyGraph, OUTPUT_TABLE_ROLES, INPUT_TABLE_ROLES } from './keyGraph';
import { tableVariablesFor } from './linkRoleHints';

export interface LinkedColumnRange {
  line: number;
  character: number;
  length: number;
}

/** Every linked PK/FK column span in the file, deduplicated (a PK shared by
 *  multiple children only yields one range for the PK itself). */
export function linkedColumnRanges(parsed: SQuiLParseResult): LinkedColumnRange[] {
  const ranges: LinkedColumnRange[] = [];

  for (const roles of [OUTPUT_TABLE_ROLES, INPUT_TABLE_ROLES]) {
    const list = tableVariablesFor(parsed, roles);
    const graph = buildKeyGraph(list, roles);
    if (!graph.hasLinks) continue;

    for (const edge of graph.edges) {
      const pkCol = edge.parent.columns?.find(
        c => c.isPrimaryKey && c.name.toLowerCase() === edge.keyName.toLowerCase(),
      );
      if (pkCol) ranges.push({ line: pkCol.line, character: pkCol.character, length: pkCol.name.length });

      const fkCol = edge.child.columns?.find(c => c.name.toLowerCase() === edge.keyName.toLowerCase());
      if (fkCol) ranges.push({ line: fkCol.line, character: fkCol.character, length: fkCol.name.length });
    }
  }

  const seen = new Set<string>();
  return ranges.filter(r => {
    const key = `${r.line}:${r.character}:${r.length}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}
