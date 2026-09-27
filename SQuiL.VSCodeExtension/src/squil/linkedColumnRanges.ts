/**
 * Semantic-token ranges for every key column on either end of a key-graph edge (OUTPUT and INPUT
 * graphs), for `providers/semanticTokensProvider.ts`. Mirrors `SQuiLLinter.LinkedColumnSpans`.
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
      // The PK lives on the owner (the nested side of an embed); the FK on the other end.
      const owner = edge.isEmbed ? edge.child : edge.parent;
      const carrier = edge.isEmbed ? edge.parent : edge.child;
      const pkCol = owner.columns?.find(
        c => c.isPrimaryKey && c.name.toLowerCase() === edge.keyName.toLowerCase(),
      );
      if (pkCol) ranges.push({ line: pkCol.line, character: pkCol.character, length: pkCol.name.length });

      const fkCol = carrier.columns?.find(c => c.name.toLowerCase() === edge.keyName.toLowerCase());
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
