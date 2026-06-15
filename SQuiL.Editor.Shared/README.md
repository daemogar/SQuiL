# SQuiL.Editor.Shared

Canonical home for editor-side assets that should look identical in **VS Code** and
**Visual Studio**:

| File | Purpose |
|---|---|
| `squil.tmLanguage.json` | TextMate grammar driving syntax highlighting |
| `language-configuration.json` | Bracket / comment / auto-closing-pair rules |
| `guide.html` | SQuiL writing guide **template** (self-contained HTML+CSS) rendered per editor host and loaded by VS Code's webview, SSMS's WebView2 tool window, and Visual Studio's WebView2 tool window |

## Editing rules

- **Edit these files here, not inside the consuming extensions.**
- Each consuming extension copies (grammar, language-config) or **renders**
  (guide.html) them into its own tree at build time so the packaged artifact is
  self-contained.

## `guide.html` template markers

`guide.html` is a **template**: most content is shared, but some passages differ
per editor host. Host-specific passages are wrapped in HTML-comment markers that
`tools/GuideRenderer` strips down to a single host's content at build time.

### Environment tokens

| Token | Editor host |
|---|---|
| `vscode` | SQuiL.VSCodeExtension (webview) |
| `ssms` | SQuiL.SsmsExtension (WebView2 tool window) |
| `visualstudio` | SQuiL.VisualStudioExtension (WebView2 tool window) |

### Block forms

A single-token block — included only when rendering for that environment:

```html
<!--#if vscode-->
…content only VS Code sees…
<!--#endif-->
```

A multi-token block — a space-separated list is an **OR**: included when
rendering for *any* of the listed environments:

```html
<!--#if ssms visualstudio-->
…content both SSMS and Visual Studio see (but not VS Code)…
<!--#endif-->
```

### Rules

- Each `<!--#if …-->` and `<!--#endif-->` marker must sit **on its own line**.
- Blocks **cannot nest**.
- Content **outside** any block is always included (shared in every render).
- `tools/GuideRenderer` renders the template per environment at build time
  (`--in <template> --out <output> --env <vscode|ssms|visualstudio>`) and
  **fails the build** on a malformed template (unbalanced, nested, or unknown
  token).

Because each host gets only its own content, the per-extension `guide.html`
outputs are **tailored** — they are no longer identical copies of this template
nor of each other. (The grammar and language-configuration files are still
copied verbatim and remain identical across consumers.)

## Consumers

| Consumer | Copies / renders into | Triggered by |
|---|---|---|
| [SQuiL.VSCodeExtension](../SQuiL.VSCodeExtension) | `syntaxes/squil.tmLanguage.json` · `language-configuration.json` (copied) · `guide.html` (rendered `--env vscode`) | `npm run sync-shared` (auto-runs on `vscode:prepublish` and `compile`) |
| [SQuiL.SsmsExtension](../SQuiL.SsmsExtension) | `Grammars\squil.tmLanguage.json` · `Grammars\language-configuration.json` (copied) · `Resources\guide.html` (rendered `--env ssms`) | MSBuild target `SyncSharedEditorAssets` (auto-runs on every build) |
| [SQuiL.VisualStudioExtension](../SQuiL.VisualStudioExtension) | `Grammars\squil.tmLanguage.json` · `Grammars\language-configuration.json` (copied) · `Resources\guide.html` (rendered `--env visualstudio`) | MSBuild target `SyncSharedEditorAssets` (auto-runs on every build) |

If you edit a shared file directly inside a consumer, your change will be overwritten
on the next build. Always edit the canonical copy here.
