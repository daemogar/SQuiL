import * as vscode from 'vscode';
import * as fs from 'fs';
import { parseSQuiL } from '../squil/parser';
import {
  allTableVariables,
  availableLinkTargets,
  buildAddPrimaryKeyEdit,
  buildInsertLinkColumnEdit,
  buildAddScalarAliasEdit,
  isCursorOnVariable,
  CodeActionEdit,
} from '../squil/codeActions';
import { scalarAliasHints } from '../squil/scalarAliasHints';
import { resolveProjectDialect } from '../squil/contextResolver';

/**
 * Nested-object authoring code actions (Task 16):
 *   - "Add Primary Key" on a table/object with none declared yet.
 *   - "Link to `<Table>` via `<PK>`" — one action per other declared table's
 *     Primary Key this table doesn't already carry a matching column for.
 *     Offering one action per candidate (rather than a single action that
 *     opens a QuickPick) makes the lightbulb list itself the picker.
 *
 * All edit computation is pure logic in `../squil/codeActions.ts` — this
 * class only hit-tests the cursor position and turns the resulting
 * `CodeActionEdit`s into `vscode.WorkspaceEdit`s.
 */
export class SQuiLCodeActionProvider implements vscode.CodeActionProvider {
  public static readonly providedCodeActionKinds = [vscode.CodeActionKind.QuickFix];

  provideCodeActions(
    document: vscode.TextDocument,
    range: vscode.Range | vscode.Selection,
  ): vscode.CodeAction[] {
    const text = document.getText();
    const lines = text.split('\n');
    const parsed = parseSQuiL(text);
    const cursorLine = range.start.line;

    const actions: vscode.CodeAction[] = [];

    for (const table of allTableVariables(parsed)) {
      if (!isCursorOnVariable(lines, table, cursorLine)) continue;

      if (!table.columns.some(c => c.isPrimaryKey)) {
        const edit = buildAddPrimaryKeyEdit(lines, table);
        if (edit) actions.push(this.toCodeAction(document, edit));
      }

      for (const target of availableLinkTargets(parsed, table)) {
        const edit = buildInsertLinkColumnEdit(lines, table, target);
        if (edit) actions.push(this.toCodeAction(document, edit));
      }
    }

    // SP0042 quick-fix: "Add `As [<Name>]`" on a bare scalar select the cursor sits on.
    // Resolve the dialect the same way the diagnostics provider does — real fs access,
    // since this provider (like diagnosticsProvider) runs in the extension host.
    const fsReadFile = (p: string): string | undefined => {
      try { return fs.readFileSync(p, 'utf-8'); } catch { return undefined; }
    };
    const fsListDir = (d: string): string[] => {
      try { return fs.readdirSync(d).map(String); } catch { return []; }
    };
    const dialect = resolveProjectDialect(document.uri.fsPath, fsReadFile, fsListDir);
    const databaseLine = parsed.databaseLine ?? -1;
    const bodyLineOffset = databaseLine >= 0 ? databaseLine + 1 : 0;
    const bodyText = databaseLine >= 0 && bodyLineOffset < lines.length
      ? lines.slice(bodyLineOffset).join('\n')
      : '';
    for (const hint of scalarAliasHints(parsed, bodyText, dialect)) {
      const absoluteLine = hint.line + bodyLineOffset;
      if (absoluteLine !== cursorLine) continue;
      const edit = buildAddScalarAliasEdit(lines, { ...hint, line: absoluteLine });
      if (edit) actions.push(this.toCodeAction(document, edit));
    }

    return actions;
  }

  private toCodeAction(document: vscode.TextDocument, edit: CodeActionEdit): vscode.CodeAction {
    const action = new vscode.CodeAction(edit.title, vscode.CodeActionKind.QuickFix);
    const workspaceEdit = new vscode.WorkspaceEdit();
    const pos = new vscode.Position(edit.position.line, edit.position.character);
    workspaceEdit.insert(document.uri, pos, edit.insertText);
    action.edit = workspaceEdit;
    return action;
  }
}
