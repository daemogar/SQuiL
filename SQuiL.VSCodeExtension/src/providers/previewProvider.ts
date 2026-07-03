import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { parseSQuiL } from '../squil/parser';
import { generateCSharpPreview } from '../squil/previewGenerator';
import { resolveContext } from '../squil/contextResolver';

// ─── Real-filesystem resolver callbacks ───────────────────────────────────

function fsReadFile(p: string): string | undefined {
  try { return fs.readFileSync(p, 'utf-8'); } catch { return undefined; }
}

function fsListDir(d: string): string[] {
  try { return fs.readdirSync(d, { withFileTypes: false }) as string[]; } catch { return []; }
}

// ─── Virtual text document scheme ─────────────────────────────────────────

const SCHEME = 'squil-preview';

export class SQuiLPreviewContentProvider
  implements vscode.TextDocumentContentProvider
{
  private readonly changeEmitter = new vscode.EventEmitter<vscode.Uri>();
  readonly onDidChange = this.changeEmitter.event;

  /** Key = source URI string  →  Value = generated content */
  private readonly cache = new Map<string, string>();

  setContent(sourceUri: vscode.Uri, content: string): void {
    this.cache.set(sourceUri.toString(), content);
    this.changeEmitter.fire(this.previewUri(sourceUri));
  }

  provideTextDocumentContent(uri: vscode.Uri): string {
    // The uri path is the encoded source path
    const sourceKey = uri.query;
    return this.cache.get(sourceKey) ?? '// No preview available.';
  }

  previewUri(sourceUri: vscode.Uri): vscode.Uri {
    return vscode.Uri.from({
      scheme: SCHEME,
      path: sourceUri.path.replace(/\.(squil|sql)$/, '.g.cs'),
      query: sourceUri.toString(),
    });
  }

  dispose(): void {
    this.changeEmitter.dispose();
  }
}

// ─── Command handler ───────────────────────────────────────────────────────

export async function openPreview(
  provider: SQuiLPreviewContentProvider,
  document: vscode.TextDocument,
): Promise<void> {
  if (document.languageId !== 'squil') {
    vscode.window.showWarningMessage('SQuiL Preview: active file is not a SQuiL file.');
    return;
  }

  const text = document.getText();
  const parsed = parseSQuiL(text);

  // Derive query name: prefer --Name: annotation, fallback to file stem
  const queryName =
    parsed.queryName ??
    path.basename(document.fileName, path.extname(document.fileName));

  // Resolve [SQuiLQuery]/[SQuiLQueryTransaction] context from disk to pick up
  // enabled/debugRollback. Falls back to enabled=false (no transaction) when
  // the file is orphaned or has duplicate registrations.
  const ctx = resolveContext(document.uri.fsPath, fsReadFile, fsListDir);
  const content = generateCSharpPreview(parsed, queryName, undefined, ctx.enabled, ctx.debugRollback);
  provider.setContent(document.uri, content);

  const previewUri = provider.previewUri(document.uri);
  const previewDoc = await vscode.workspace.openTextDocument(previewUri);

  await vscode.window.showTextDocument(previewDoc, {
    viewColumn: vscode.ViewColumn.Beside,
    preserveFocus: true,
    preview: true,
  });

  await vscode.languages.setTextDocumentLanguage(previewDoc, 'csharp');
}

/** Refresh the preview when the source document changes. */
export function refreshPreview(
  provider: SQuiLPreviewContentProvider,
  document: vscode.TextDocument,
): void {
  if (document.languageId !== 'squil') return;

  const text = document.getText();
  const parsed = parseSQuiL(text);
  const queryName =
    parsed.queryName ??
    path.basename(document.fileName, path.extname(document.fileName));

  // Re-resolve context on each refresh so the preview stays in sync with
  // any changes to the C# attribute.
  const ctx = resolveContext(document.uri.fsPath, fsReadFile, fsListDir);
  const content = generateCSharpPreview(parsed, queryName, undefined, ctx.enabled, ctx.debugRollback);
  provider.setContent(document.uri, content);
}
