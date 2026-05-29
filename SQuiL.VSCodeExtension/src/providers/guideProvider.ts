/**
 * Loads the SQuiL writing guide into a VS Code webview panel.
 *
 * The guide HTML is NOT defined in this file — it lives in:
 *
 *     ../../../SQuiL.Editor.Shared/guide.html       (canonical)
 *
 * and is copied to `<extension-root>/guide.html` by `scripts/sync-shared.js`
 * before each build / package. When the user invokes the Open Writing Guide
 * command, we read that file at runtime and hand its contents to the webview.
 *
 * EDIT-HERE TRAIL
 *   • Updating the guide content? → SQuiL.Editor.Shared/guide.html
 *   • Changing how the guide loads or what it can do? → THIS FILE.
 *
 * The same `guide.html` is consumed by SQuiL.VisualStudioExtension (future
 * WebView2 tool window) so both IDEs render identical docs.
 */

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';

export function openGuide(context: vscode.ExtensionContext): void {
  const panel = vscode.window.createWebviewPanel(
    'squil.guide',
    'SQuiL Writing Guide',
    vscode.ViewColumn.Beside,
    { enableScripts: false },
  );

  panel.webview.html = readGuideHtml(context);
  context.subscriptions.push(panel);
}

function readGuideHtml(context: vscode.ExtensionContext): string {
  const guidePath = path.join(context.extensionPath, 'guide.html');
  try {
    return fs.readFileSync(guidePath, 'utf-8');
  } catch (err) {
    // The sync-shared step normally copies guide.html into the extension
    // root before packaging. If it's missing the extension is mis-built.
    const msg = (err as Error).message;
    return `<!DOCTYPE html>
<html><body style="font-family:sans-serif;padding:24px;">
<h1>SQuiL Writing Guide — load error</h1>
<p>Could not read <code>guide.html</code> from the extension root.</p>
<p>This usually means <code>npm run sync-shared</code> did not run before the
extension was packaged. Re-run <code>npm run package</code> and reinstall.</p>
<pre>${escapeHtml(msg)}</pre>
</body></html>`;
  }
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
