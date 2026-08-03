import * as vscode from 'vscode';
import * as fs from 'fs';
import { parseSQuiL, describeRole } from '../squil/parser';
import { sampleDataExists } from '../squil/sampleDataGenerator';
import { EditorDialect, isTempTableDialect } from '../squil/dialect';
import { resolveProjectDialect } from '../squil/contextResolver';
import {
  VarDescriptor,
  headerVarsFor,
  fileSnippetsFor,
  typesFor,
  isSqliteColumnTypePosition,
} from '../squil/completionData';

// ─── Real-filesystem resolver callbacks (mirrors previewProvider.ts) ──────

function fsReadFile(p: string): string | undefined {
  try { return fs.readFileSync(p, 'utf-8'); } catch { return undefined; }
}

function fsListDir(d: string): string[] {
  try { return fs.readdirSync(d).map(String); } catch { return []; }
}

// ─── PascalCase keyword / type lists ──────────────────────────────────────

const DML_KEYWORDS = [
  'Select', 'Insert', 'Update', 'Delete', 'Merge', 'Truncate',
  'From', 'Where',
  'Join', 'Inner Join', 'Left Join', 'Right Join', 'Full Outer Join', 'Cross Join',
  'On', 'Into', 'Values', 'Set', 'Top', 'Distinct', 'As',
  'Union', 'Union All', 'Intersect', 'Except',
  'Group By', 'Order By', 'Having', 'Over', 'Partition By',
  'Rows Between', 'Range Between', 'Between',
  'And', 'Or', 'Not', 'In', 'Like',
  'Is Null', 'Is Not Null', 'Exists',
  'Case', 'When', 'Then', 'Else', 'End',
  'With', 'Exec', 'Execute', 'Output',
  'Declare', 'Use',
];

const CONTROL_KEYWORDS = [
  'If', 'Else', 'Begin', 'End',
  'While', 'Break', 'Continue', 'Return',
  'RaiseError', 'Throw', 'Try', 'Catch', 'Print',
];

const TABLE_HINTS = ['NoLock', 'ReadPast', 'UpdLock', 'RowLock', 'TabLock'];

// SQL type vocabularies, SQuiL variable descriptors, and file-level scaffold
// snippets (T-SQL + SQLite variants, with dialect selectors) live in the pure,
// unit-tested `../squil/completionData` module.

// ─── Helpers ──────────────────────────────────────────────────────────────

function findUseLine(document: vscode.TextDocument): number | undefined {
  for (let i = 0; i < document.lineCount; i++) {
    if (/^\s*USE\s+/i.test(document.lineAt(i).text)) {
      return i;
    }
  }
  return undefined;
}

function isInHeader(document: vscode.TextDocument, position: vscode.Position): boolean {
  const useLine = findUseLine(document);
  return useLine === undefined || position.line < useLine;
}

// ─── Provider ─────────────────────────────────────────────────────────────

export class SQuiLCompletionProvider implements vscode.CompletionItemProvider {
  provideCompletionItems(
    document: vscode.TextDocument,
    position: vscode.Position,
  ): vscode.CompletionItem[] {
    const lineText = document.lineAt(position).text;
    const textBefore = lineText.substring(0, position.character);
    const textAfter = lineText.substring(position.character);
    const inHeader = isInHeader(document, position);
    const dialect = resolveProjectDialect(document.uri.fsPath, fsReadFile, fsListDir);

    // If the cursor is inside an existing @word token (i.e., word chars
    // continue past the cursor), suppress completions so editing the
    // prefix — e.g. Param ↔ Params, Return ↔ Returns — is a plain text
    // edit and not hijacked by an auto-complete replace.
    if (/@\w*$/.test(textBefore) && /^\w/.test(textAfter)) {
      return [];
    }

    // ── Body section: @ → context-sensitive ──────────────────────────
    if (!inHeader) {
      const atMatch = textBefore.match(/@(\w*)$/);
      if (atMatch) {
        // Only offer declared-variable completions when NOT on a Declare line
        const lineHasDeclare = /^\s*DECLARE\s+/i.test(textBefore);
        if (!lineHasDeclare) {
          return this.bodyVariableCompletions(document, atMatch[0], position);
        }
      }
      // Type completions. For the temp-table dialect family (SQLite, PostgreSQL), types
      // belong at the `Create Temp Table (col │` column-type position ONLY — an author
      // never writes `Declare @x`. For SqlServer, `Declare @var ` → types (unchanged).
      // (Temp-table files have no USE line, so this body branch is effectively
      // unreachable for them, but the gate keeps the dialects parallel.)
      if (isTempTableDialect(dialect)) {
        if (isSqliteColumnTypePosition(textBefore)) {
          return this.typeCompletions(dialect);
        }
      } else if (/DECLARE\s+@\w+\s+$/i.test(textBefore)) {
        return this.typeCompletions(dialect);
      }
      return this.sqlKeywordCompletions(textBefore);
    }

    // ── Header section ────────────────────────────────────────────────

    // @ typed → SQuiL declaration patterns + any variables already declared above this line
    const atMatch = textBefore.match(/@(\w*)$/);
    if (atMatch) {
      const lineHasDeclare = /^\s*DECLARE\s+/i.test(textBefore);
      const items = this.headerVariableCompletions(atMatch[0], position, !lineHasDeclare, dialect);
      items.push(...this.variablesDefinedAbove(document, position, atMatch[0]));
      items.push(...this.sampleDataCompletions(document, position, atMatch[0]));
      return items;
    }

    // "Declare " typed → offer @Prefix_ patterns (no second Declare)
    if (/^\s*DECLARE\s+$/i.test(textBefore)) {
      return this.headerVariableCompletions('', position, false, dialect);
    }

    // Type completions. For the temp-table dialect family (SQLite, PostgreSQL), types
    // belong ONLY at the `Create Temp Table (col │` column-type position — the T-SQL
    // `Declare @var ` / `AS ` positions must NOT drive their type completion (an author
    // never writes `Declare @x`, and in a USE-less file the whole file reads as header,
    // so `AS ` is an alias position, not a type position). For SqlServer the
    // existing `Declare @var ` / `AS ` behavior is UNCHANGED.
    if (isTempTableDialect(dialect)) {
      if (isSqliteColumnTypePosition(textBefore)) {
        return this.typeCompletions(dialect);
      }
    } else if (/DECLARE\s+@\w+\s+$/i.test(textBefore) || /\bAS\s+$/i.test(textBefore)) {
      return this.typeCompletions(dialect);
    }

    // Table hints
    if (/WITH\s*\($/i.test(textBefore)) {
      return TABLE_HINTS.map(h => {
        const item = new vscode.CompletionItem(h, vscode.CompletionItemKind.Keyword);
        item.detail = 'SQL table hint';
        return item;
      });
    }

    // Empty/scaffold lines
    if (/^\s*(sq)?$/i.test(textBefore)) {
      return fileSnippetsFor(dialect).map(s => {
        const item = new vscode.CompletionItem(s.label, vscode.CompletionItemKind.Snippet);
        item.insertText = new vscode.SnippetString(s.snippet);
        item.detail = s.detail;
        return item;
      });
    }

    return this.sqlKeywordCompletions(textBefore);
  }

  // ── Header completions: Declare @Prefix_ → full snippet ───────────

  private headerVariableCompletions(
    typed: string,
    position: vscode.Position,
    prependDeclare: boolean,
    dialect: EditorDialect,
  ): vscode.CompletionItem[] {
    const replaceRange = new vscode.Range(
      position.translate(0, -typed.length),
      position,
    );

    // Temp-table-family (SQLite, PostgreSQL) header declarations are full
    // `Create Temp Table …` statements — never prefixed with the T-SQL `Declare` keyword.
    const isTempTable = isTempTableDialect(dialect);

    return headerVarsFor(dialect).map((v: VarDescriptor) => {
      const item = new vscode.CompletionItem(v.prefix, vscode.CompletionItemKind.Variable);
      item.detail = v.detail;
      item.documentation = new vscode.MarkdownString(v.docs);
      item.sortText = '0' + v.prefix;
      item.insertText = new vscode.SnippetString(
        !isTempTable && prependDeclare ? `Declare ${v.snippet};` : `${v.snippet};`,
      );
      item.range = replaceRange;
      return item;
    });
  }

  // ── Variable completions: only those declared above the cursor ────

  private variablesDefinedAbove(
    document: vscode.TextDocument,
    position: vscode.Position,
    typed: string,
  ): vscode.CompletionItem[] {
    const dialect = resolveProjectDialect(document.uri.fsPath, fsReadFile, fsListDir);
    const parsed = parseSQuiL(document.getText(), dialect);
    const replaceRange = new vscode.Range(
      position.translate(0, -typed.length),
      position,
    );

    return parsed.variables
      .filter(v => v.line < position.line)
      .map(v => {
        const item = new vscode.CompletionItem(v.rawName, vscode.CompletionItemKind.Variable);
        item.detail = `${describeRole(v.role)}  —  ${v.sqlType}`;
        item.documentation = new vscode.MarkdownString(
          `**Role:** ${describeRole(v.role)}\n\n` +
          `**SQL type:** \`${v.sqlType}\`` +
          (v.columns
            ? `\n\n**Columns:** ${v.columns.map(c => `\`${c.name}\``).join(', ')}`
            : ''),
        );
        item.range = replaceRange;
        item.sortText = '1' + v.rawName; // sorts below pattern completions (which use '0')
        return item;
      });
  }

  // ── Body completions: only variables declared above the cursor ─────

  private bodyVariableCompletions(
    document: vscode.TextDocument,
    typed: string,
    position: vscode.Position,
  ): vscode.CompletionItem[] {
    return this.variablesDefinedAbove(document, position, typed);
  }

  // ── Sample data insertion completions ─────────────────────────────
  // Rules:
  //   • Only the immediately-previous variable (highest line < cursor) matters
  //   • That variable must be a param table type (params or param-table)
  //   • Show "Insert" if no sample block exists yet, "Modify" if one does

  private sampleDataCompletions(
    document: vscode.TextDocument,
    position: vscode.Position,
    typed: string,
  ): vscode.CompletionItem[] {
    const dialect = resolveProjectDialect(document.uri.fsPath, fsReadFile, fsListDir);
    const parsed = parseSQuiL(document.getText(), dialect);

    // The immediately-previous variable (last one before cursor line)
    const varsAbove = parsed.variables.filter(v => v.line < position.line);
    if (varsAbove.length === 0) return [];

    const lastVar = varsAbove[varsAbove.length - 1];

    // Must be a param table type — scalar params and all return types are excluded
    if (lastVar.role !== 'params' && lastVar.role !== 'param-table') return [];
    if (!lastVar.columns || lastVar.columns.length === 0) return [];

    const text = document.getText();
    const hasBlock = sampleDataExists(text, lastVar.rawName);

    const replaceRange = new vscode.Range(
      position.translate(0, -typed.length),
      position,
    );

    const label = hasBlock
      ? `⊕ Modify sample data → ${lastVar.rawName}`
      : `⊕ Insert sample data → ${lastVar.rawName}`;

    const item = new vscode.CompletionItem(label, vscode.CompletionItemKind.Snippet);
    item.detail = hasBlock
      ? `Change the number of test rows for ${lastVar.rawName}`
      : `Add test rows to ${lastVar.rawName} (${lastVar.columns.map(c => c.name).join(', ')})`;
    item.documentation = new vscode.MarkdownString(
      `${hasBlock ? 'Modify' : 'Insert'} a sample **Insert Into** block.\n\n` +
      `> ⚠ Sample data is for local testing only — remove before committing.`,
    );
    item.insertText = '';
    item.range = replaceRange;
    item.sortText = '2' + lastVar.rawName;
    item.command = {
      command: 'squil.insertSampleData',
      title: label,
      arguments: [document.uri, lastVar, hasBlock],
    };
    return [item];
  }

  // ── SQL keyword completions ────────────────────────────────────────

  private sqlKeywordCompletions(textBefore: string): vscode.CompletionItem[] {
    const wordMatch = textBefore.match(/\b(\w+)$/);
    if (!wordMatch) return [];
    const prefix = wordMatch[1].toLowerCase();

    const items: vscode.CompletionItem[] = [];
    for (const kw of [...DML_KEYWORDS, ...CONTROL_KEYWORDS]) {
      if (kw.toLowerCase().startsWith(prefix)) {
        const item = new vscode.CompletionItem(kw, vscode.CompletionItemKind.Keyword);
        item.detail = 'SQL keyword';
        items.push(item);
      }
    }
    return items;
  }

  // ── SQL type completions ───────────────────────────────────────────

  private typeCompletions(dialect: EditorDialect = 'sqlserver'): vscode.CompletionItem[] {
    const types = typesFor(dialect);
    const items = types.map(t => {
      const item = new vscode.CompletionItem(t, vscode.CompletionItemKind.TypeParameter);
      item.detail = dialect === 'sqlite' ? 'SQLite type' : dialect === 'postgres' ? 'PostgreSQL type' : 'SQL type';
      return item;
    });

    const tableItem = new vscode.CompletionItem('table (...)', vscode.CompletionItemKind.TypeParameter);
    tableItem.insertText = new vscode.SnippetString('table (${1:ColumnName} ${2:int})');
    tableItem.detail = 'SQL table type';
    items.push(tableItem);

    return items;
  }
}
