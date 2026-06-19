import * as vscode from 'vscode';
import { parseSQuiL, SQuiLDiagnostic, SQuiLVariable } from '../squil/parser';

export class SQuiLDiagnosticsProvider {
  private readonly collection: vscode.DiagnosticCollection;

  constructor() {
    this.collection = vscode.languages.createDiagnosticCollection('squil');
  }

  /** Analyse a document and push diagnostics. */
  public update(document: vscode.TextDocument): void {
    if (document.languageId !== 'squil') return;

    const text = document.getText();
    const parsed = parseSQuiL(text);
    const vsDiags: vscode.Diagnostic[] = [];

    for (const d of parsed.diagnostics) {
      const vsDiag = this.toDiagnostic(document, d);
      vsDiags.push(vsDiag);
    }

    // Additional linting passes
    vsDiags.push(...this.lintVariableNames(document, text));
    vsDiags.push(...this.lintStatementTerminators(document, text));
    vsDiags.push(...this.lintColumnDefaults(document, parsed.variables));

    this.collection.set(document.uri, vsDiags);
  }

  public clear(document: vscode.TextDocument): void {
    this.collection.delete(document.uri);
  }

  public dispose(): void {
    this.collection.dispose();
  }

  // ─── Private helpers ─────────────────────────────────────────────────

  /**
   * SP0010 — a table column with a `default` followed by a column without one.
   * The generated record is positional, so optional (defaulted) parameters must
   * be trailing (else C# emits CS1737). Mirrors the source generator's check.
   */
  private lintColumnDefaults(
    document: vscode.TextDocument,
    variables: SQuiLVariable[],
  ): vscode.Diagnostic[] {
    const diags: vscode.Diagnostic[] = [];
    for (const v of variables) {
      if (!v.columns) continue;
      let defaulted: string | undefined;
      for (const col of v.columns) {
        if (col.defaultValue !== undefined) {
          defaulted = col.name;
        } else if (defaulted !== undefined) {
          const line = Math.min(v.line, document.lineCount - 1);
          const lineLength = document.lineAt(line).text.length;
          const range = new vscode.Range(
            new vscode.Position(line, Math.min(v.character, lineLength)),
            new vscode.Position(line, lineLength),
          );
          const d = new vscode.Diagnostic(
            range,
            `SP0010: column '${defaulted}' has a default but is followed by '${col.name}', which has none. ` +
              `Columns with a default must be declared last (C# positional records require optional parameters to be trailing).`,
            vscode.DiagnosticSeverity.Error,
          );
          d.source = 'squil';
          d.code = 'SP0010';
          diags.push(d);
          break;
        }
      }
    }
    return diags;
  }

  private toDiagnostic(document: vscode.TextDocument, d: SQuiLDiagnostic): vscode.Diagnostic {
    const line = Math.min(d.line, document.lineCount - 1);
    const lineLength = document.lineAt(line).text.length;
    const start = new vscode.Position(line, Math.min(d.startChar, lineLength));
    const end = new vscode.Position(line, Math.min(d.endChar || lineLength, lineLength));
    const range = new vscode.Range(start, end);

    const diag = new vscode.Diagnostic(
      range,
      d.message,
      d.severity === 'error'
        ? vscode.DiagnosticSeverity.Error
        : d.severity === 'warning'
          ? vscode.DiagnosticSeverity.Warning
          : vscode.DiagnosticSeverity.Information,
    );
    diag.source = 'squil';
    return diag;
  }

  /**
   * Warn when a DECLARE @variable looks almost-but-not-quite like a SQuiL
   * convention name (e.g. @param_Name instead of @Param_Name).
   */
  private lintVariableNames(
    _document: vscode.TextDocument,
    text: string,
  ): vscode.Diagnostic[] {
    const diags: vscode.Diagnostic[] = [];
    const lines = text.split('\n');

    const TYPO_PATTERNS: Array<[RegExp, string]> = [
      [/@param_/i,  '@Param_'],
      [/@params_/i, '@Params_'],
      [/@return_/i, '@Return_'],
      [/@returns_/i,'@Returns_'],
    ];

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      if (!/DECLARE/i.test(line)) continue;

      for (const [pattern, correct] of TYPO_PATTERNS) {
        const m = line.match(pattern);
        if (!m) continue;

        const actual = m[0];
        // Only warn if casing is wrong (correct starts with capital Param/Return)
        if (actual === correct || actual.toUpperCase() === correct.toUpperCase()) {
          // If they wrote it in a different case, check if it's the canonical one
          if (actual !== correct && actual.toUpperCase() === correct.toUpperCase()) {
            const idx = line.indexOf(actual);
            const range = new vscode.Range(
              new vscode.Position(i, idx),
              new vscode.Position(i, idx + actual.length),
            );
            const d = new vscode.Diagnostic(
              range,
              `Prefer '${correct}' (PascalCase) over '${actual}'. SQuiL uses PascalCase for variable prefixes.`,
              vscode.DiagnosticSeverity.Information,
            );
            d.source = 'squil';
            diags.push(d);
          }
        }
      }
    }
    return diags;
  }

  /**
   * Hint when DECLARE statements are missing their semicolon terminator.
   */
  private lintStatementTerminators(
    _document: vscode.TextDocument,
    text: string,
  ): vscode.Diagnostic[] {
    const diags: vscode.Diagnostic[] = [];
    const lines = text.split('\n');

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      const trimmed = line.trim();

      if (!trimmed.match(/^DECLARE\s+/i)) continue;
      if (trimmed.endsWith(';') || trimmed.endsWith('*/')) continue;

      // Multi-line TABLE declarations — skip until the closing paren
      if (/TABLE\s*\(/i.test(trimmed) && !trimmed.includes(')')) continue;

      // Single-line DECLARE without semicolon
      const endChar = line.trimEnd().length;
      const range = new vscode.Range(
        new vscode.Position(i, endChar),
        new vscode.Position(i, endChar),
      );
      const d = new vscode.Diagnostic(
        range,
        'DECLARE statement is missing a semicolon terminator.',
        vscode.DiagnosticSeverity.Information,
      );
      d.source = 'squil';
      diags.push(d);
    }
    return diags;
  }
}
