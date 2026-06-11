import * as vscode from 'vscode';
import * as cp from 'child_process';
import * as path from 'path';

import { SQuiLCompletionProvider } from './providers/completionProvider';
import { SQuiLDiagnosticsProvider } from './providers/diagnosticsProvider';
import { SQuiLHoverProvider } from './providers/hoverProvider';
import {
  SQuiLPreviewContentProvider,
  openPreview,
  refreshPreview,
} from './providers/previewProvider';
import { openGuide } from './providers/guideProvider';
import { checkForUpdates } from './providers/updateChecker';
import { generateSampleInsert, findSampleDataLines } from './squil/sampleDataGenerator';
import { SQuiLVariable } from './squil/parser';

const SQUIL_LANG = 'squil';

// ─── Activate ─────────────────────────────────────────────────────────────

export function activate(context: vscode.ExtensionContext): void {
  const diagnostics = new SQuiLDiagnosticsProvider();
  const preview = new SQuiLPreviewContentProvider();

  context.subscriptions.push(diagnostics, preview);

  // ── Language feature registrations ──────────────────────────────────

  context.subscriptions.push(
    vscode.languages.registerCompletionItemProvider(
      { language: SQUIL_LANG },
      new SQuiLCompletionProvider(),
      '@', ' ', '(',   // trigger characters
    ),
  );

  context.subscriptions.push(
    vscode.languages.registerHoverProvider(
      { language: SQUIL_LANG },
      new SQuiLHoverProvider(),
    ),
  );

  // Virtual document provider for preview
  context.subscriptions.push(
    vscode.workspace.registerTextDocumentContentProvider(
      'squil-preview',
      preview,
    ),
  );

  // ── Commands ─────────────────────────────────────────────────────────

  context.subscriptions.push(
    vscode.commands.registerCommand(
      'squil.previewGeneratedCSharp',
      async () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
          vscode.window.showInformationMessage('SQuiL: No active editor.');
          return;
        }
        await openPreview(preview, editor.document);
      },
    ),
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('squil.openGuide', () => openGuide(context)),
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('squil.checkForUpdates', () =>
      checkForUpdates(context, { manual: true }),
    ),
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(
      'squil.newFile',
      async () => {
        // Empty untitled document tagged as squil; saving defaults to a .squil extension.
        const doc = await vscode.workspace.openTextDocument({
          language: SQUIL_LANG,
          content: '',
        });
        await vscode.window.showTextDocument(doc);
      },
    ),
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(
      'squil.insertSampleData',
      async (uri: vscode.Uri, variable: SQuiLVariable, hasExisting: boolean) => {
        const editor = vscode.window.visibleTextEditors.find(
          e => e.document.uri.toString() === uri.toString(),
        );
        if (!editor) return;

        // @Param_ (single object) always inserts exactly one row — no prompt.
        // @Params_ (list) asks how many.
        let count: number;
        if (variable.role === 'params') {
          const input = await vscode.window.showInputBox({
            title: `${hasExisting ? 'Modify' : 'Insert'} sample data for ${variable.rawName}`,
            prompt: 'How many records?',
            value: '3',
            validateInput: v => {
              const n = parseInt(v, 10);
              if (isNaN(n) || n < 1) return 'Enter a positive integer';
              if (n > 100) return 'Maximum 100 records';
              return undefined;
            },
          });

          if (input === undefined) return;
          count = parseInt(input, 10);
        } else {
          count = 1;
        }
        const sql = generateSampleInsert(variable, count);
        const lines = editor.document.getText().split('\n');

        if (hasExisting) {
          // Replace the existing block in-place
          const found = findSampleDataLines(lines, variable.rawName);
          if (found) {
            const replaceRange = new vscode.Range(
              new vscode.Position(found.startLine, 0),
              new vscode.Position(found.endLine, editor.document.lineAt(found.endLine).text.length),
            );
            await editor.edit(edit => edit.replace(replaceRange, sql));
            return;
          }
        }

        // Insert at the start of the current cursor line
        await editor.edit(edit => {
          const pos = editor.selection.active;
          edit.insert(new vscode.Position(pos.line, 0), sql + '\n');
        });
      },
    ),
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(
      'squil.buildProject',
      async () => {
        const editor = vscode.window.activeTextEditor;
        const targetDir = editor
          ? path.dirname(editor.document.fileName)
          : vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;

        if (!targetDir) {
          vscode.window.showErrorMessage('SQuiL Build: Cannot determine project directory.');
          return;
        }

        await runDotnetBuild(targetDir);
      },
    ),
  );

  // ── Document event listeners ─────────────────────────────────────────

  // Lint on open
  context.subscriptions.push(
    vscode.workspace.onDidOpenTextDocument(doc => {
      if (doc.languageId === SQUIL_LANG) {
        diagnostics.update(doc);
      }
    }),
  );

  // Lint + refresh preview on save
  context.subscriptions.push(
    vscode.workspace.onDidSaveTextDocument(doc => {
      if (doc.languageId === SQUIL_LANG) {
        diagnostics.update(doc);
        refreshPreview(preview, doc);
      }
    }),
  );

  // Lint on change (debounced)
  let lintTimer: ReturnType<typeof setTimeout> | undefined;
  context.subscriptions.push(
    vscode.workspace.onDidChangeTextDocument(e => {
      if (e.document.languageId !== SQUIL_LANG) return;
      if (lintTimer) clearTimeout(lintTimer);
      lintTimer = setTimeout(() => {
        diagnostics.update(e.document);
        refreshPreview(preview, e.document);
      }, 500);
    }),
  );

  // Clear diagnostics when closed
  context.subscriptions.push(
    vscode.workspace.onDidCloseTextDocument(doc => {
      if (doc.languageId === SQUIL_LANG) {
        diagnostics.clear(doc);
      }
    }),
  );

  // ── Lint any already-open squil files (e.g. on reload) ──────────────
  for (const doc of vscode.workspace.textDocuments) {
    if (doc.languageId === SQUIL_LANG) {
      diagnostics.update(doc);
    }
  }

  // Background update check (channel-aware; throttled for prereleases).
  void checkForUpdates(context, { manual: false });

  console.log('SQuiL extension activated.');
}

// ─── Deactivate ───────────────────────────────────────────────────────────

export function deactivate(): void {
  // Nothing to clean up; subscriptions handle disposal.
}

// ─── dotnet build helper ──────────────────────────────────────────────────

async function runDotnetBuild(cwd: string): Promise<void> {
  const dotnetPath =
    vscode.workspace.getConfiguration('squil').get<string>('dotnetPath', 'dotnet');

  // Walk up to find the nearest .csproj or .sln
  const projectPath = await findProjectFile(cwd);
  const buildTarget = projectPath ?? cwd;

  const channel = vscode.window.createOutputChannel('SQuiL Build');
  channel.show(true);
  channel.appendLine(`> ${dotnetPath} build "${buildTarget}"`);
  channel.appendLine('');

  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Window,
      title: 'SQuiL: Building…',
      cancellable: false,
    },
    () =>
      new Promise<void>((resolve) => {
        const proc = cp.spawn(dotnetPath, ['build', buildTarget], {
          cwd,
          shell: true,
        });

        proc.stdout.on('data', (d: Buffer) => channel.append(d.toString()));
        proc.stderr.on('data', (d: Buffer) => channel.append(d.toString()));

        proc.on('close', code => {
          channel.appendLine('');
          if (code === 0) {
            channel.appendLine('✔  Build succeeded.');
            vscode.window.setStatusBarMessage('SQuiL: Build succeeded ✔', 5000);
          } else {
            channel.appendLine(`✘  Build failed (exit code ${code}).`);
            vscode.window.showErrorMessage(`SQuiL Build failed. Check the SQuiL Build output panel.`);
          }
          resolve();
        });

        proc.on('error', err => {
          channel.appendLine(`Error spawning dotnet: ${err.message}`);
          vscode.window.showErrorMessage(`Could not run dotnet: ${err.message}`);
          resolve();
        });
      }),
  );
}

async function findProjectFile(startDir: string): Promise<string | undefined> {
  let dir = startDir;
  for (let i = 0; i < 8; i++) {
    const files = await vscode.workspace.fs.readDirectory(vscode.Uri.file(dir));
    const proj = files.find(
      ([name]) => name.endsWith('.csproj') || name.endsWith('.sln'),
    );
    if (proj) return path.join(dir, proj[0]);
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  return undefined;
}
