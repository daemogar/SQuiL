# Installing SQuiL for Visual Studio Code

The release asset is a single file: **`squil-editor-<version>.vsix`**.

## Install

Pick whichever you prefer.

**From the command line** (works with VS Code, VS Code Insiders, Cursor, etc. —
anything that ships the `code` CLI):

```bash
code --install-extension squil-editor-<version>.vsix
```

**From the VS Code UI:**

1. Open the **Extensions** view (`Ctrl+Shift+X`).
2. Click the **`···`** menu at the top of the view → **Install from VSIX…**
3. Select the downloaded `squil-editor-<version>.vsix`.
4. Reload when prompted.

## Requirements

- **VS Code 1.85.0 or newer.**
- For the **Build Project** command: the **.NET SDK** on your `PATH`
  (`dotnet --version` resolves). You can point at a specific CLI with the
  `squil.dotnetPath` setting.

## What you get

Open any `.squil` file and you get:

- SQuiL syntax highlighting (coral `@Param_*`, mauve `@Return_*`, `USE` +
  database name, `--Name:` annotations, teal SQL types).
- IntelliSense for SQuiL `@`-prefixes, declared variables, and file snippets
  (type `@`, or `Ctrl+Space`).
- Hover info — role, SQL type, mapped C# type, target record name.
- Diagnostics — missing/duplicate `USE`, unknown variable prefixes, casing
  typos, missing `;`.
- Commands (Command Palette, or the editor title/context menu on a `.squil`
  file): **SQuiL: New File**, **Preview Generated C#**, **Build Project**,
  **Open Writing Guide**.

## Uninstall

Extensions view → find **SQuiL SQL Editor** → gear icon → **Uninstall**. Or:

```bash
code --uninstall-extension southern.squil-editor
```
