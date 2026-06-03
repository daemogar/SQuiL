# Installing SQuiL for Visual Studio 2026

The release asset is a single file: **`SQuiL.VisualStudioExtension.vsix`**.

## Things to know before you install

- **Visual Studio 2026 required** (shell version 18.x — Community, Professional,
  or Enterprise). The VSIX manifest targets `Microsoft.VisualStudio.Community
  [18.0,)`, which installs into any 2026 edition. Earlier VS versions will not
  load it.
- **WebView2 Runtime** is needed *only* for the Writing Guide pane. Every other
  feature works without it. Modern Windows 11 + Edge ships it; otherwise install
  from <https://developer.microsoft.com/microsoft-edge/webview2/>.
- **.NET SDK on `PATH`** is needed *only* for the **Build SQuiL Project** command
  (it shells out to `dotnet build`).
- **`.squil` files only.** Plain `.sql` files are unaffected — VS's normal SQL
  behaviour is untouched.
- **Install is per-user.** No elevation required.

## Install

Close Visual Studio first, then **either**:

**Double-click** `SQuiL.VisualStudioExtension.vsix` — the VSIX Installer opens,
lists "Visual Studio 2026", and installs.

**Or from PowerShell** (adjust the edition segment — `Enterprise` /
`Professional` / `Community` — to match your install):

```powershell
$installer = "${env:ProgramFiles}\Microsoft Visual Studio\18\Enterprise\Common7\IDE\VSIXInstaller.exe"
$vsix      = "SQuiL.VisualStudioExtension.vsix"   # or the full path to the file you downloaded

# Close any running VS instances:
Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force

# Install:
& $installer $vsix
```

Launch Visual Studio and open any `.squil` file.

## What you get

- SQuiL syntax overlay (coral `@Param_*`, mauve `@Return_*`, italic `@Debug`,
  `USE` + database name, `--Name:` annotation, teal SQL types).
- IntelliSense for SQuiL `@`-prefixes, declared variables, and file snippets
  (type `@`, or `Ctrl+Space` on a blank header line).
- Hover any `@Param_X` / `@Return_X` for role + type info.
- Error squiggles for missing/duplicate `USE`, unknown variable prefixes,
  casing typos, and missing semicolons.
- Sample-data ⊕ entry in the `@` completion list under a
  `Declare @Params_X table(...)` declaration.
- **File → New → New SQuiL File**.
- **Preview Generated C#** and **Build SQuiL Project** on the editor right-click
  menu and the **Tools** menu.
- **Tools → SQuiL Writing Guide** (WebView2-hosted).

## Uninstall

```powershell
$installer = "${env:ProgramFiles}\Microsoft Visual Studio\18\Enterprise\Common7\IDE\VSIXInstaller.exe"
Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force
& $installer /uninstall:SQuiL.VisualStudioExtension.7c9d1e3f-4a6b-4c8d-9e0f-1a2b3c4d5e6f
```

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| VSIXInstaller exit code 2003 ("NoApplicableSKUs") | No VS 2026 (18.x) found | Install VS 2026, or check you're not on VS 2022 (17.x) |
| Installs, but `.squil` opens as plain text with no colours | MEF/extension cache stale | Run `devenv /updateconfiguration`, or `devenv /setup`, then relaunch |
| Writing Guide pane shows "WebView2 Runtime missing" | WebView2 not installed | Install from <https://developer.microsoft.com/microsoft-edge/webview2/> |
| "Build SQuiL Project" reports `dotnet` not found | `dotnet` not on the `PATH` VS inherited | Confirm `(Get-Command dotnet).Source`, then restart VS |
