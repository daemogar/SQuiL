// Copies editor-side assets from ../SQuiL.Editor.Shared into this extension's
// tree so VS Code's grammar/language-configuration paths can resolve them.
// Run via `npm run sync-shared` (auto-runs on compile and vscode:prepublish).

const fs = require('node:fs');
const path = require('node:path');

const sharedDir = path.resolve(__dirname, '..', '..', 'SQuiL.Editor.Shared');
const extDir = path.resolve(__dirname, '..');

const copies = [
  { from: 'squil.tmLanguage.json', to: path.join('syntaxes', 'squil.tmLanguage.json') },
  { from: 'language-configuration.json', to: 'language-configuration.json' },
  { from: 'guide.html', to: 'guide.html' },
];

if (!fs.existsSync(sharedDir)) {
  console.error(`[sync-shared] Cannot find ${sharedDir}.`);
  process.exit(1);
}

for (const { from, to } of copies) {
  const src = path.join(sharedDir, from);
  const dest = path.join(extDir, to);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.copyFileSync(src, dest);
  console.log(`[sync-shared] ${from}  →  ${to}`);
}
