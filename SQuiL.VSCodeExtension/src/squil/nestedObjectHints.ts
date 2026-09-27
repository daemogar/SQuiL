/**
 * Editor-only nested-object hints: orphaned Primary Key (SP0035) and containment direction (SP0045),
 * per graph side. SP0045 is editor-only, mirrored by `LintContainmentHint` in both `SQuiLLinter.cs`.
 * Rules: `SQuiL.SourceGenerator/README.md`, "Nested objects: key graph".
 */

import { SQuiLParseResult, SQuiLVariable } from './parser';
import { buildKeyGraph, KeyGraphEdge, KeyGraphResult, OUTPUT_TABLE_ROLES, INPUT_TABLE_ROLES } from './keyGraph';

export interface NestedObjectHint {
  code: 'SP0035' | 'SP0045';
  message: string;
  line: number;
  character: number;
  /** Length of the token to underline (the Primary Key column name, or the
   *  nested variable's raw name for SP0045). */
  length: number;
}

/** "list" for a plural (`Returns_`/`Params_`) child, "single object" for a
 *  singular (`Return_`/`Param_`) one — but an embed is ALWAYS a single
 *  object (the container's FK column is dropped from the C# record), which
 *  overrides the child's own declared cardinality. */
function cardinalityWord(edge: KeyGraphEdge): string {
  if (edge.isEmbed) return 'single object';
  return edge.child.role === 'returns' || edge.child.role === 'params' ? 'list' : 'single object';
}

/** Declaration order between two variables — the earlier source position wins. */
function declaredBefore(a: SQuiLVariable, b: SQuiLVariable): boolean {
  return a.line !== b.line ? a.line < b.line : a.character < b.character;
}

function containmentHints(graph: KeyGraphResult): NestedObjectHint[] {
  return graph.edges.map(edge => {
    const word = cardinalityWord(edge);
    const containerDeclaredFirst = declaredBefore(edge.parent, edge.child);
    const message = containerDeclaredFirst
      ? `\`${edge.child.name}\` nests inside \`${edge.parent.name}\` as a ${word}, because ` +
        `\`${edge.parent.name}\` is declared first. Reorder the declarations to swap the containment.`
      : `\`${edge.child.name}\` nests inside \`${edge.parent.name}\` as a single object, because ` +
        `\`${edge.parent.name}\` references its Primary Key \`${edge.keyName}\` as a lookup.`;
    return {
      code: 'SP0045',
      message,
      line: edge.child.line,
      character: edge.child.character,
      length: edge.child.rawName.length,
    };
  });
}

/**
 * Return all SP0035 + SP0045 hint descriptors for the given parse result.
 */
export function nestedObjectHints(parsed: SQuiLParseResult): NestedObjectHint[] {
  const outputGraph = buildKeyGraph(parsed.variables, OUTPUT_TABLE_ROLES);
  const inputGraph = buildKeyGraph(parsed.variables, INPUT_TABLE_ROLES);

  const orphanHints: NestedObjectHint[] = [...outputGraph.hints, ...inputGraph.hints].map(finding => {
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

  return [...orphanHints, ...containmentHints(outputGraph), ...containmentHints(inputGraph)];
}
