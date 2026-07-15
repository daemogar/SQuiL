import * as vscode from 'vscode';
import { parseSQuiL } from '../squil/parser';
import { linkedColumnRanges } from '../squil/linkedColumnRanges';

/** Single semantic token type: a nested-object relationship key (a Primary
 *  Key column and its matching foreign-key-by-convention column(s) on child
 *  tables). Legend index 0 — see `provideDocumentSemanticTokens` below. Must
 *  match the "relationshipKey" id contributed in package.json's
 *  `semanticTokenTypes`. */
export const squilSemanticTokensLegend = new vscode.SemanticTokensLegend(['relationshipKey'], []);

/** Tags every linked PK/FK column NAME token with the `relationshipKey`
 *  semantic token type (see `linkedColumnRanges.ts` for the pure logic that
 *  resolves the nested-object graph). Classification only — never emits a
 *  diagnostic, and a file with no links produces zero tokens. */
export class SQuiLSemanticTokensProvider implements vscode.DocumentSemanticTokensProvider {
  provideDocumentSemanticTokens(document: vscode.TextDocument): vscode.SemanticTokens {
    const builder = new vscode.SemanticTokensBuilder(squilSemanticTokensLegend);
    const parsed = parseSQuiL(document.getText());

    // SemanticTokensBuilder requires tokens in non-decreasing (line, character) order.
    const ranges = linkedColumnRanges(parsed).sort(
      (a, b) => a.line - b.line || a.character - b.character,
    );

    for (const r of ranges) {
      builder.push(r.line, r.character, r.length, 0, 0);
    }

    return builder.build();
  }
}
