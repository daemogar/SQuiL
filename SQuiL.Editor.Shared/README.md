# SQuiL.Editor.Shared

Canonical home for editor-side assets that should look identical in **VS Code** and
**Visual Studio**:

| File | Purpose |
|---|---|
| `squil.tmLanguage.json` | TextMate grammar driving syntax highlighting |
| `language-configuration.json` | Bracket / comment / auto-closing-pair rules |
| `guide.html` | Rendered SQuiL writing guide (self-contained HTML+CSS) loaded by VS Code's webview and Visual Studio's WebView2 tool window |

## Editing rules

- **Edit these files here, not inside the consuming extensions.**
- Each consuming extension copies them into its own tree at build time so the packaged
  artifact is self-contained.

## Consumers

| Consumer | Copies into | Triggered by |
|---|---|---|
| [SQuiL.VSCodeExtension](../SQuiL.VSCodeExtension) | `syntaxes/squil.tmLanguage.json` · `language-configuration.json` · `guide.html` | `npm run sync-shared` (auto-runs on `vscode:prepublish` and `compile`) |
| [SQuiL.VisualStudioExtension](../SQuiL.VisualStudioExtension) | `Grammars\squil.tmLanguage.json` · `Grammars\language-configuration.json` · `Resources\guide.html` | MSBuild target `SyncSharedEditorAssets` (auto-runs on every build) |

If you edit a shared file directly inside a consumer, your change will be overwritten
on the next build. Always edit the canonical copy here.
