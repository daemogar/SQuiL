import * as vscode from 'vscode';
import { parseSQuiL, describeRole } from '../squil/parser';
import { sampleDataExists } from '../squil/sampleDataGenerator';

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

const SQL_TYPES = [
  'bigint', 'binary', 'bit', 'char', 'date',
  'datetime', 'datetime2', 'datetimeoffset',
  'decimal', 'float', 'image', 'int', 'money',
  'nchar', 'ntext', 'numeric', 'nvarchar',
  'real', 'smalldatetime', 'smallint', 'smallmoney',
  'text', 'time', 'tinyint', 'uniqueidentifier',
  'varbinary', 'varchar', 'xml',
  // Common parameterised variants
  'varchar(50)', 'varchar(100)', 'varchar(255)', 'varchar(max)',
  'nvarchar(50)', 'nvarchar(100)', 'nvarchar(255)', 'nvarchar(max)',
  'decimal(18, 2)', 'decimal(18, 4)',
  'char(1)', 'char(10)',
];

const TABLE_HINTS = ['NoLock', 'ReadPast', 'UpdLock', 'RowLock', 'TabLock'];

// ─── SQuiL variable descriptors ───────────────────────────────────────────

interface VarDescriptor {
  prefix: string;
  snippet: string;
  detail: string;
  docs: string;
}

const HEADER_VARS: VarDescriptor[] = [
  {
    prefix: '@Param_',
    snippet: '@Param_${1:Name} ${2:varchar(100)}',
    detail: 'Input scalar parameter',
    docs:
      'Maps to a property on the generated `*Request` record.\n\n' +
      '```sql\nDeclare @Param_UserID int;\n```',
  },
  {
    prefix: '@Params_',
    snippet: '@Params_${1:Items} table (${2:ID int})',
    detail: 'Input table-valued parameter → IEnumerable<T>',
    docs:
      'Maps to an `IEnumerable<ItemT>` property on `*Request`.\n\n' +
      '```sql\nDeclare @Params_UserIDs table (ID int);\n```',
  },
  {
    prefix: '@Return_',
    snippet: '@Return_${1:Name} ${2:int}',
    detail: 'Output scalar variable',
    docs:
      'Maps to a property on the generated `*Response` record.\n\n' +
      '```sql\nDeclare @Return_Count int;\n```',
  },
  {
    prefix: '@Returns_',
    snippet: '@Returns_${1:Items} table (${2:ID int, Name varchar(100)})',
    detail: 'Output table variable → IEnumerable<T>',
    docs:
      'Maps to an `IEnumerable<ItemT>` property on `*Response`.\n\n' +
      '```sql\nDeclare @Returns_Users table (ID int, Name varchar(100));\n```',
  },
  {
    prefix: '@Debug',
    snippet: '@Debug bit = 1',
    detail: 'Debug flag — on *Request as `bool Debug` when declared',
    docs:
      'Opt-in special SQuiL variable. `*Request` exposes `bool Debug` **only when `@Debug` is declared**. ' +
      'Declare `@SuppressDebug` alongside it to gate the auto-debug expression. ' +
      'The default `= 1` is convenient when running the query directly in SSMS.\n\n' +
      '```sql\nDeclare @Debug bit = 1;\n```',
  },
  {
    prefix: '@SuppressDebug',
    snippet: '@SuppressDebug bit = 0',
    detail: 'Suppress auto-debug — on *Request as `bool SuppressDebug` when declared',
    docs:
      'Opt-in special SQuiL variable. Gates the auto-debug expression (replaces the old `DebugOnly` property). ' +
      'Must be declared together with `@Debug`, otherwise **SP0019** is reported.\n\n' +
      '```sql\nDeclare @Debug bit = 1;\nDeclare @SuppressDebug bit = 0;\n```',
  },
  {
    prefix: '@EnvironmentName',
    snippet: '@EnvironmentName varchar(50)',
    detail: 'Environment name — resolved by SQuiLBaseDataContext',
    docs:
      'Resolved by `SQuiLBaseDataContext` from `IConfiguration["EnvironmentName"]` or the ' +
      '`ASPNETCORE_ENVIRONMENT` environment variable (defaulting to `"Development"`). ' +
      'Declare in SQL only when the query body needs to read it. Sent as a parameter only — never a C# property.\n\n' +
      '```sql\nDeclare @EnvironmentName varchar(50);\n```',
  },
  {
    prefix: '@AsOfDate',
    snippet: "@AsOfDate date = '2008-10-01'",
    detail: 'Point-in-time — nullable typed property on *Request',
    docs:
      'Opt-in special SQuiL variable. Caller-supplied point-in-time value, surfaced as a **nullable typed property** ' +
      'on `*Request` (its type follows the SQL type map, e.g. `date` → `DateOnly?`). ' +
      'When null, the **current time at execution** is substituted; the SQL initializer is ignored at runtime.\n\n' +
      "```sql\nDeclare @AsOfDate date = '2008-10-01';\n```",
  },
];

// ─── File-level scaffold snippets ─────────────────────────────────────────

const FILE_SNIPPETS = [
  {
    label: 'squil-file',
    snippet: [
      '--Name: ${1:QueryName}',
      '',
      'Declare @Param_${2:Name} ${3:varchar(100)};',
      'Declare @Return_${4:Result} ${5:int};',
      '',
      'Use [${6:DatabaseName}];',
      '',
      '-- SQL body',
      'Set @Return_${4:Result} = (Select ${7:Count(*)} From ${8:TableName} Where ${9:1=1});',
      'Select @Return_${4:Result};',
    ].join('\n'),
    detail: 'Scaffold a complete SQuiL file',
  },
  {
    label: 'squil-declare-input',
    snippet: 'Declare @Param_${1:Name} ${2:varchar(100)};',
    detail: 'Declare input scalar parameter',
  },
  {
    label: 'squil-declare-input-table',
    snippet: ['Declare @Params_${1:Items} table (', '    ${2:ID} ${3:int}', ');'].join('\n'),
    detail: 'Declare input table-valued parameter',
  },
  {
    label: 'squil-declare-output',
    snippet: 'Declare @Return_${1:Name} ${2:int};',
    detail: 'Declare output scalar variable',
  },
  {
    label: 'squil-declare-output-table',
    snippet: [
      'Declare @Returns_${1:Items} table (',
      '    ${2:ID} ${3:int},',
      '    ${4:Name} ${5:varchar(100)}',
      ');',
    ].join('\n'),
    detail: 'Declare output table variable',
  },
];

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
      // `Declare @var ` → SQL type completions
      if (/DECLARE\s+@\w+\s+$/i.test(textBefore)) {
        return this.typeCompletions();
      }
      return this.sqlKeywordCompletions(textBefore);
    }

    // ── Header section ────────────────────────────────────────────────

    // @ typed → SQuiL declaration patterns + any variables already declared above this line
    const atMatch = textBefore.match(/@(\w*)$/);
    if (atMatch) {
      const lineHasDeclare = /^\s*DECLARE\s+/i.test(textBefore);
      const items = this.headerVariableCompletions(atMatch[0], position, !lineHasDeclare);
      items.push(...this.variablesDefinedAbove(document, position, atMatch[0]));
      items.push(...this.sampleDataCompletions(document, position, atMatch[0]));
      return items;
    }

    // "Declare " typed → offer @Prefix_ patterns (no second Declare)
    if (/^\s*DECLARE\s+$/i.test(textBefore)) {
      return this.headerVariableCompletions('', position, false);
    }

    // After "Declare @var " or "AS " → SQL types
    if (/DECLARE\s+@\w+\s+$/i.test(textBefore) || /\bAS\s+$/i.test(textBefore)) {
      return this.typeCompletions();
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
      return FILE_SNIPPETS.map(s => {
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
  ): vscode.CompletionItem[] {
    const replaceRange = new vscode.Range(
      position.translate(0, -typed.length),
      position,
    );

    return HEADER_VARS.map(v => {
      const item = new vscode.CompletionItem(v.prefix, vscode.CompletionItemKind.Variable);
      item.detail = v.detail;
      item.documentation = new vscode.MarkdownString(v.docs);
      item.sortText = '0' + v.prefix;
      item.insertText = new vscode.SnippetString(
        prependDeclare ? `Declare ${v.snippet};` : `${v.snippet};`,
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
    const parsed = parseSQuiL(document.getText());
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
    const parsed = parseSQuiL(document.getText());

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

  private typeCompletions(): vscode.CompletionItem[] {
    const items = SQL_TYPES.map(t => {
      const item = new vscode.CompletionItem(t, vscode.CompletionItemKind.TypeParameter);
      item.detail = 'SQL type';
      return item;
    });

    const tableItem = new vscode.CompletionItem('table (...)', vscode.CompletionItemKind.TypeParameter);
    tableItem.insertText = new vscode.SnippetString('table (${1:ColumnName} ${2:int})');
    tableItem.detail = 'SQL table type';
    items.push(tableItem);

    return items;
  }
}
