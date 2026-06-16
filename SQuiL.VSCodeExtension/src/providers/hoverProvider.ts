import * as vscode from 'vscode';
import { parseSQuiL, SQuiLVariable, describeRole } from '../squil/parser';

// ─── SQL → C# quick-reference (duplicated from previewGenerator for independence) ──

const SQL_CS: Record<string, string> = {
  bigint: 'long', binary: 'byte[]', bit: 'bool',
  char: 'string', date: 'DateOnly', datetime: 'DateTime',
  datetime2: 'DateTime', datetimeoffset: 'DateTime',
  decimal: 'decimal', float: 'double', image: 'byte[]',
  int: 'int', money: 'decimal', nchar: 'string', ntext: 'string',
  numeric: 'decimal', nvarchar: 'string', real: 'float',
  smalldatetime: 'DateTime', smallint: 'short', smallmoney: 'decimal',
  text: 'string', time: 'TimeSpan', tinyint: 'byte',
  uniqueidentifier: 'Guid', varbinary: 'byte[]', varchar: 'string', xml: 'string',
};

function sqlToCSharp(sqlType: string): string {
  const base = sqlType.toLowerCase().replace(/\s*\(.*\)/, '').trim();
  return SQL_CS[base] ?? 'object';
}

function getCSharpType(v: SQuiLVariable): string {
  // Generated record naming, per CLAUDE.md:
  //   table-valued (params / returns)   → `<Name>Table`   (collection element)
  //   single-object (param/return-table) → `<Name>Object` (single object)
  // The older `<Name>Item` suffix is legacy — do not reintroduce it.
  if (v.role === 'params' || v.role === 'returns') return `IEnumerable<${v.name}Table>`;
  if (v.role === 'param-table' || v.role === 'return-table') return `${v.name}Object`;
  return sqlToCSharp(v.sqlType);
}

function recordTypeName(v: SQuiLVariable): string {
  if (v.role === 'params' || v.role === 'returns') return `${v.name}Table`;
  if (v.role === 'param-table' || v.role === 'return-table') return `${v.name}Object`;
  return v.name;
}

// ─── Provider ─────────────────────────────────────────────────────────────

export class SQuiLHoverProvider implements vscode.HoverProvider {
  provideHover(
    document: vscode.TextDocument,
    position: vscode.Position,
  ): vscode.Hover | undefined {
    const wordRange = document.getWordRangeAtPosition(position, /@[\w_]+/);
    if (!wordRange) return undefined;

    const word = document.getText(wordRange);
    if (!word.startsWith('@')) return undefined;

    const parsed = parseSQuiL(document.getText());
    const variable = parsed.variables.find(
      v => v.rawName.toUpperCase() === word.toUpperCase(),
    );

    if (!variable) {
      // Unknown @variable — still provide basic info
      return new vscode.Hover(
        new vscode.MarkdownString(
          `**\`${word}\`** — unrecognised variable\n\n` +
          `SQuiL naming conventions:\n` +
          `- \`@Param_Name\` — input scalar\n` +
          `- \`@Params_Name\` — input table-valued\n` +
          `- \`@Return_Name\` — output scalar\n` +
          `- \`@Returns_Name\` — output table`,
        ),
        wordRange,
      );
    }

    const md = new vscode.MarkdownString();
    md.isTrusted = true;

    md.appendMarkdown(`**\`${variable.rawName}\`** — ${describeRole(variable.role)}\n\n`);

    // @AsOfDate is a special only in recognition — unlike the other specials it
    // IS emitted as a nullable typed property on `*Request`, so it gets the full
    // type table (with a nullable note) rather than the "not emitted" message.
    if (variable.role === 'asOfDate') {
      // Map only the type token (drop any "= default" the SQL initializer adds).
      const asOfType = variable.sqlType.split(/[\s=]/)[0];
      md.appendMarkdown(`| | |\n|---|---|\n`);
      md.appendMarkdown(`| **SQL type** | \`${variable.sqlType}\` |\n`);
      md.appendMarkdown(`| **C# type** | \`${sqlToCSharp(asOfType)}?\` |\n`);
      md.appendMarkdown(`| **C# name** | \`${variable.name}\` |\n`);
      md.appendMarkdown(`| **Generated in** | \`*Request\` record (nullable) |\n`);
      md.appendMarkdown(
        `\n> ℹ️ Special SQuiL variable — emitted as a **nullable typed property** on \`*Request\`. ` +
        `When null, the current time at execution is substituted.\n`,
      );
      return new vscode.Hover(md, wordRange);
    }

    const isSpecial = ['debug', 'suppressDebug', 'environmentName', 'error', 'errors', 'unknown'].includes(variable.role);

    if (!isSpecial) {
      md.appendMarkdown(`| | |\n|---|---|\n`);
      md.appendMarkdown(`| **SQL type** | \`${variable.sqlType}\` |\n`);
      md.appendMarkdown(`| **C# type** | \`${getCSharpType(variable)}\` |\n`);
      md.appendMarkdown(`| **C# name** | \`${variable.name}\` |\n`);
      md.appendMarkdown(`| **Generated in** | `);

      if (variable.role.startsWith('param')) {
        md.appendMarkdown('`*Request` record |\n');
      } else {
        md.appendMarkdown('`*Response` record |\n');
      }

      if (variable.columns && variable.columns.length > 0) {
        md.appendMarkdown(`\n**Columns** → \`${recordTypeName(variable)}\` record:\n\n`);
        md.appendCodeblock(
          variable.columns
            .map(c => `${sqlToCSharp(c.sqlType)}${c.nullable ? '?' : ''} ${c.name}`)
            .join('\n'),
          'csharp',
        );
      }
    } else {
      md.appendMarkdown(`\n> ℹ️ This is a **special SQuiL variable** and is not emitted as a C# property.\n`);
    }

    return new vscode.Hover(md, wordRange);
  }
}
