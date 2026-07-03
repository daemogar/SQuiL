import * as vscode from 'vscode';
import * as fs from 'fs';
import { parseSQuiL, SQuiLDiagnostic, lintShapeCollision } from '../squil/parser';
import { nullabilityHints } from '../squil/nullabilityHints';
import { shapeHints } from '../squil/shapeHints';
import { transactionHints } from '../squil/transactionHints';
import { resolveContext } from '../squil/contextResolver';
import { scanMutations } from '../squil/mutationScanner';

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

    // SP0010: nullability hints (unmarked scalars and table columns)
    for (const hint of nullabilityHints(parsed)) {
      const line = Math.min(hint.line, document.lineCount - 1);
      const lineLength = document.lineAt(line).text.length;
      const startChar = Math.min(hint.character, lineLength);
      const endChar = Math.min(hint.character + hint.length, lineLength);
      const range = new vscode.Range(
        new vscode.Position(line, startChar),
        new vscode.Position(line, endChar),
      );
      const d = new vscode.Diagnostic(range, hint.message, vscode.DiagnosticSeverity.Hint);
      d.source = 'squil';
      d.code = hint.code;
      vsDiags.push(d);
    }

    // SP0020: similar-signature hints (differently-named tables with identical column shape)
    for (const hint of shapeHints(parsed)) {
      const line = Math.min(hint.line, document.lineCount - 1);
      const lineLength = document.lineAt(line).text.length;
      const range = new vscode.Range(
        new vscode.Position(line, Math.min(hint.character, lineLength)),
        new vscode.Position(line, Math.min(hint.character + hint.length, lineLength)),
      );
      const d = new vscode.Diagnostic(range, hint.message, vscode.DiagnosticSeverity.Hint);
      d.source = 'squil';
      d.code = hint.code;
      vsDiags.push(d);
    }

    // SP0030: result-shape collision — same-file same-side output pairs with identical
    // canonical shape key.  These can't be routed to different records at runtime.
    for (const d of lintShapeCollision(parsed)) {
      vsDiags.push(this.toDiagnostic(document, d));
    }

    // SP0028 / SP0027: orphan / duplicate context resolver diagnostics.
    // We use real fs here (diagnosticsProvider runs in the extension host with
    // real disk access). The resolver is injected with real-fs callbacks.
    const squilPath = document.uri.fsPath;
    const fsReadFile = (p: string): string | undefined => {
      try { return fs.readFileSync(p, 'utf-8'); } catch { return undefined; }
    };
    const fsListDir = (d: string): string[] => {
      try { return fs.readdirSync(d).map(String); } catch { return []; }
    };
    const ctx = resolveContext(squilPath, fsReadFile, fsListDir);
    if (!ctx.found) {
      const range0 = new vscode.Range(new vscode.Position(0, 0), new vscode.Position(0, 0));
      if (ctx.matchCount === 0) {
        // SP0028 — orphan: no data context registers this query file.
        const d = new vscode.Diagnostic(
          range0,
          "This query file isn't registered by any data context. " +
          "Add a [SQuiLQuery] or [SQuiLQueryTransaction] attribute referencing it.",
          vscode.DiagnosticSeverity.Warning,
        );
        d.source = 'squil';
        d.code = 'SP0028';
        vsDiags.push(d);
      } else {
        // SP0027 — duplicate: multiple contexts register this query file.
        const d = new vscode.Diagnostic(
          range0,
          `This query file is registered by ${ctx.matchCount} data contexts. ` +
          "Only one [SQuiLQuery] or [SQuiLQueryTransaction] may reference each QueryFiles member.",
          vscode.DiagnosticSeverity.Error,
        );
        d.source = 'squil';
        d.code = 'SP0027';
        vsDiags.push(d);
      }
    }

    // SP0023 / SP0024 / SP0025 — mutation-vs-transaction diagnostics.
    // These combine the resolved context (attribute kind + enabled) with the
    // mutation scanner (which body is read-only / has own Begin Tran).
    // Port of the build-time emit in FileGenerator.cs — change one, change all.
    if (ctx.found) {
      // Extract the body text: everything after the USE statement line.
      // parsed.databaseLine is 0-based; body starts on the NEXT line.
      const databaseLine = parsed.databaseLine ?? -1;
      let bodyText = '';
      let bodyStartOffset = 0;
      if (databaseLine >= 0 && databaseLine + 1 < document.lineCount) {
        bodyStartOffset = document.offsetAt(new vscode.Position(databaseLine + 1, 0));
        bodyText = text.slice(bodyStartOffset);
      }

      const scan = scanMutations(bodyText);

      if (!ctx.enabled) {
        // [SQuiLQuery] or [SQuiLQueryTransaction(enabled:false)] — warn if mutation detected.
        if (!scan.isProvablyReadOnly && scan.mutations.length > 0) {
          const hit = scan.mutations[0];
          const hitPos = document.positionAt(bodyStartOffset + hit.start);
          const hitEndPos = document.positionAt(bodyStartOffset + hit.start + hit.length);
          const hitRange = new vscode.Range(hitPos, hitEndPos);
          const d = new vscode.Diagnostic(
            hitRange,
            `The query body contains a persistent real-table mutation (${hit.kind}). ` +
            'Use [SQuiLQueryTransaction] to wrap the mutation in a transaction.',
            vscode.DiagnosticSeverity.Warning,
          );
          d.source = 'squil';
          d.code = 'SP0023';
          vsDiags.push(d);
        }
      } else {
        // [SQuiLQueryTransaction(enabled:true)] — warn if read-only; error if own Begin Tran.
        if (scan.isProvablyReadOnly) {
          const range0 = new vscode.Range(new vscode.Position(0, 0), new vscode.Position(0, 0));
          const d = new vscode.Diagnostic(
            range0,
            'No persistent mutation was detected in the query body. ' +
            'Use [SQuiLQuery] instead — a transaction wrapper adds overhead with no benefit on a read-only query.',
            vscode.DiagnosticSeverity.Warning,
          );
          d.source = 'squil';
          d.code = 'SP0024';
          vsDiags.push(d);
        }
        if (scan.hasOwnTransaction) {
          // Range on the Begin Tran itself, if we can find it in the body.
          const btMatch = bodyText.match(/\bBegin\s+Tran(?:saction)?\b/i);
          const btRange = btMatch && btMatch.index !== undefined
            ? new vscode.Range(
                document.positionAt(bodyStartOffset + btMatch.index),
                document.positionAt(bodyStartOffset + btMatch.index + btMatch[0].length),
              )
            : new vscode.Range(new vscode.Position(0, 0), new vscode.Position(0, 0));
          const d = new vscode.Diagnostic(
            btRange,
            'The query body contains a Begin Tran/Begin Transaction statement, but ' +
            '[SQuiLQueryTransaction] already wraps the query in a C# DbTransaction. ' +
            'Remove the SQL-level transaction, or set enabled:false on [SQuiLQueryTransaction] to manage the transaction manually.',
            vscode.DiagnosticSeverity.Error,
          );
          d.source = 'squil';
          d.code = 'SP0025';
          vsDiags.push(d);
        }
      }
    }

    // SP0026: debugRollback has no effect without @Debug (editor-only hint)
    for (const hint of transactionHints(parsed, ctx)) {
      const range0 = new vscode.Range(new vscode.Position(0, 0), new vscode.Position(0, 0));
      const d = new vscode.Diagnostic(range0, hint.message, vscode.DiagnosticSeverity.Hint);
      d.source = 'squil';
      d.code = hint.code;
      vsDiags.push(d);
    }

    // Additional linting passes
    vsDiags.push(...this.lintVariableNames(document, text));
    vsDiags.push(...this.lintStatementTerminators(document, text));

    this.collection.set(document.uri, vsDiags);
  }

  public clear(document: vscode.TextDocument): void {
    this.collection.delete(document.uri);
  }

  public dispose(): void {
    this.collection.dispose();
  }

  // ─── Private helpers ─────────────────────────────────────────────────

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

    if (d.code !== undefined) {
      diag.code = d.code;
    }

    if (d.relatedLine !== undefined) {
      const relLine = Math.min(d.relatedLine, document.lineCount - 1);
      const relLineLen = document.lineAt(relLine).text.length;
      const relStart = new vscode.Position(relLine, Math.min(d.relatedStartChar ?? 0, relLineLen));
      const relEnd = new vscode.Position(relLine, Math.min(d.relatedEndChar ?? relLineLen, relLineLen));
      diag.relatedInformation = [
        new vscode.DiagnosticRelatedInformation(
          new vscode.Location(document.uri, new vscode.Range(relStart, relEnd)),
          d.relatedMessage ?? 'first declared here',
        ),
      ];
    }

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
