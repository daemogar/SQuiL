import * as vscode from 'vscode';
import * as fs from 'fs';
import { parseSQuiL, SQuiLVariable, describeRole } from '../squil/parser';
import { describeColumnLinkRole } from '../squil/linkRoleHints';
import { EditorDialect } from '../squil/dialect';
import { resolveProjectDialect } from '../squil/contextResolver';

// ─── Real-filesystem resolver callbacks (mirrors previewProvider.ts) ──────

function fsReadFile(p: string): string | undefined {
  try { return fs.readFileSync(p, 'utf-8'); } catch { return undefined; }
}

function fsListDir(d: string): string[] {
  try { return fs.readdirSync(d).map(String); } catch { return []; }
}

// ─── SQL → C# quick-reference (duplicated from previewGenerator for independence) ──

const SQL_CS: Record<string, string> = {
  bigint: 'long', binary: 'byte[]', bit: 'bool',
  char: 'string', date: 'DateOnly', datetime: 'DateTime',
  datetime2: 'DateTime', datetimeoffset: 'DateTimeOffset',
  decimal: 'decimal', float: 'double', image: 'byte[]',
  int: 'int', money: 'decimal', nchar: 'string', ntext: 'string',
  numeric: 'decimal', nvarchar: 'string', real: 'float',
  smalldatetime: 'DateTime', smallint: 'short', smallmoney: 'decimal',
  text: 'string', time: 'TimeOnly', tinyint: 'byte',
  uniqueidentifier: 'Guid', varbinary: 'byte[]', varchar: 'string', xml: 'string',
};

/** SQLite's type vocabulary overlays SQL_CS for keys whose CLR mapping differs by dialect. */
const SQLITE_CS: Record<string, string> = {
  integer: 'long', text: 'string', real: 'double', blob: 'byte[]',
  numeric: 'decimal', boolean: 'bool', date: 'DateTime', datetime: 'DateTime',
  guid: 'Guid', uniqueidentifier: 'Guid',
};

function sqlToCSharp(sqlType: string, dialect: EditorDialect = 'sqlserver'): string {
  const base = sqlType.toLowerCase().replace(/\s*\(.*\)/, '').trim();
  if (dialect === 'sqlite' && base in SQLITE_CS) return SQLITE_CS[base];
  return SQL_CS[base] ?? 'object';
}

function getCSharpType(v: SQuiLVariable, dialect: EditorDialect = 'sqlserver'): string {
  if (v.role === 'params' || v.role === 'returns') return `IEnumerable<${v.name}>`;
  if (v.role === 'param-table' || v.role === 'return-table') return v.name;
  return sqlToCSharp(v.sqlType, dialect);
}

function recordTypeName(v: SQuiLVariable): string {
  return v.name;
}

// ─── Provider ─────────────────────────────────────────────────────────────

export class SQuiLHoverProvider implements vscode.HoverProvider {
  provideHover(
    document: vscode.TextDocument,
    position: vscode.Position,
  ): vscode.Hover | undefined {
    const wordRange = document.getWordRangeAtPosition(position, /@[\w_]+/);
    if (!wordRange) return this.provideColumnLinkRoleHover(document, position);

    const word = document.getText(wordRange);
    if (!word.startsWith('@')) return undefined;

    const dialect = resolveProjectDialect(document.uri.fsPath, fsReadFile, fsListDir);

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
      md.appendMarkdown(`| **C# type** | \`${sqlToCSharp(asOfType, dialect)}?\` |\n`);
      md.appendMarkdown(`| **C# name** | \`${variable.name}\` |\n`);
      md.appendMarkdown(`| **Generated in** | \`*Request\` record (nullable) |\n`);
      md.appendMarkdown(
        `\n> ℹ️ Special SQuiL variable — emitted as a **nullable typed property** on \`*Request\`. ` +
        `When null, the current time at execution is substituted.\n`,
      );
      return new vscode.Hover(md, wordRange);
    }

    const isSpecial = ['debug', 'suppressDebug', 'environmentName', 'unknown'].includes(variable.role);

    if (!isSpecial) {
      md.appendMarkdown(`| | |\n|---|---|\n`);
      md.appendMarkdown(`| **SQL type** | \`${variable.sqlType}\` |\n`);
      md.appendMarkdown(`| **C# type** | \`${getCSharpType(variable, dialect)}\` |\n`);
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
            .map(c => `${sqlToCSharp(c.sqlType, dialect)}${c.nullable ? '?' : ''} ${c.name}`)
            .join('\n'),
          'csharp',
        );
      }
    } else {
      md.appendMarkdown(`\n> ℹ️ This is a **special SQuiL variable** and is not emitted as a C# property.\n`);
    }

    return new vscode.Hover(md, wordRange);
  }

  /**
   * Hover for a bare table-column identifier (no `@` prefix) that plays a
   * role in the nested-object PK/FK-by-convention graph — see
   * `linkRoleHints.ts` (`describeColumnLinkRole`). Columns that play no
   * link role fall through to `undefined`, leaving hover completely
   * unchanged (graceful degradation — a no-links file shows no link text).
   */
  private provideColumnLinkRoleHover(
    document: vscode.TextDocument,
    position: vscode.Position,
  ): vscode.Hover | undefined {
    const wordRange = document.getWordRangeAtPosition(position);
    if (!wordRange) return undefined;

    const parsed = parseSQuiL(document.getText());
    const text = describeColumnLinkRole(parsed, wordRange.start.line, wordRange.start.character);
    if (!text) return undefined;

    return new vscode.Hover(new vscode.MarkdownString(text), wordRange);
  }
}
