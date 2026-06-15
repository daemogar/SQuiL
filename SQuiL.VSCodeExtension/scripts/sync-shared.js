// Copies editor-side assets from ../SQuiL.Editor.Shared into this extension's
// tree. Grammar + language-configuration are copied verbatim; guide.html is
// rendered for the "vscode" environment by the shared tools/GuideRenderer.
// Run via `npm run sync-shared` (auto-runs on compile and vscode:prepublish).

const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

const sharedDir = path.resolve(__dirname, '..', '..', 'SQuiL.Editor.Shared');
const extDir = path.resolve(__dirname, '..');
const toolProject = path.resolve(__dirname, '..', '..', 'tools', 'GuideRenderer', 'GuideRenderer.csproj');

if (!fs.existsSync(sharedDir)) {
  console.error(`[sync-shared] Cannot find ${sharedDir}.`);
  process.exit(1);
}

const verbatim = [
  { from: 'squil.tmLanguage.json', to: path.join('syntaxes', 'squil.tmLanguage.json') },
  { from: 'language-configuration.json', to: 'language-configuration.json' },
];

for (const { from, to } of verbatim) {
  const src = path.join(sharedDir, from);
  const dest = path.join(extDir, to);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.copyFileSync(src, dest);
  console.log(`[sync-shared] ${from}  →  ${to}`);
}

// guide.html: render for the vscode environment via the shared C# tool.
execFileSync('dotnet', [
  'run', '--project', toolProject, '-c', 'Release', '--',
  '--in', path.join(sharedDir, 'guide.html'),
  '--out', path.join(extDir, 'guide.html'),
  '--env', 'vscode',
], { stdio: 'inherit' });
console.log('[sync-shared] guide.html  →  rendered for vscode');
