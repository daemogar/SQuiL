/**
 * Orphaned Primary-Key hint pass (SP0035) + containment-direction hint (SP0046).
 *
 * Editor-only Hint (VS Code Hint severity, C# Info severity) — NOT a
 * build/generator diagnostic. SP0035 fires when a table/object variable
 * declares a `Primary Key` column that NO other table/object in the file
 * links to (no matching-named column anywhere else) — but ONLY when nesting
 * is already "in play" in that same universe, i.e. at least one real
 * parent/child link exists elsewhere (`hasLinks`). A deliberately-flat file
 * whose tables happen to each declare an unrelated Primary Key must NOT be
 * nagged.
 *
 * SP0046 fires once per key-graph edge, anchored on the NESTED (child)
 * variable's declaration, explaining WHY the edge nests the way it does:
 * declaration order (R1) for a normal edge — the earlier-declared block is
 * always the container — or, for an R3-inverted junction edge (the
 * container is declared AFTER the nested variable), the container's own
 * reference to the nested variable's Primary Key as a lookup. Never suggests
 * reordering for an inverted edge, since declaration order isn't why it
 * nests that way.
 *
 * Both hints apply to BOTH the OUTPUT (`@Return_`/`@Returns_`) and INPUT
 * (`@Param_`/`@Params_`) key graphs independently — `hasLinks` (SP0035) and
 * edge enumeration (SP0046) are evaluated per-graph, matching the
 * generator's two independent graphs.
 *
 * Mirrors `SQuiLKeyGraph.Hints` (`SQuiL.SourceGenerator/SQuiL/Models/SQuiLKeyGraph.cs`)
 * and `LintKeyGraph`'s orphan + containment branches in `SQuiLLinter.cs`
 * (SSMS + Visual Studio) — change one side, change all three.
 *
 * The caller (diagnosticsProvider) converts these into vscode.Diagnostic
 * objects; unit tests consume the raw descriptors directly — no vscode
 * dependency here.
 */

import { SQuiLParseResult, SQuiLVariable } from './parser';
import { buildKeyGraph, KeyGraphEdge, KeyGraphResult, OUTPUT_TABLE_ROLES, INPUT_TABLE_ROLES } from './keyGraph';

export interface NestedObjectHint {
  code: 'SP0035' | 'SP0046';
  message: string;
  line: number;
  character: number;
  /** Length of the token to underline (the Primary Key column name, or the
   *  nested variable's raw name for SP0046). */
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
      code: 'SP0046',
      message,
      line: edge.child.line,
      character: edge.child.character,
      length: edge.child.rawName.length,
    };
  });
}

/**
 * Return all SP0035 + SP0046 hint descriptors for the given parse result.
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
