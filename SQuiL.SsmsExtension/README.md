# SQuiL for SSMS 22.6

A SQL Server Management Studio 22.6 extension that adds authoring support
for SQuiL-annotated `.squil` files **on top of** SSMS's full SQL editor.
F5 / connection picker / results pane all work like a plain `.sql` file
— SQuiL just overlays its own syntax colouring, IntelliSense, hover, and
linting on `.squil`-extension files.

> `.squil` files are source artefacts for the
> [`SQuiL.SourceGenerator`](https://github.com/daemogar/SQuiL) C# source
> generator. They are also valid T-SQL — you can execute them against a
> live database to verify behaviour, then let the source generator turn
> them into typed C# records and data-context methods at build time.

---

## Features

| Feature | Trigger |
|---|---|
| Open `.squil` files in SSMS's SQL Query Editor | Double-click a `.squil` file (or File → Open) |
| Execute against a SQL Server (F5) | Connect using the toolbar dropdown, then `F5` |
| SQuiL syntax overlay — coral `@Param_*`, mauve `@Return_*`, italic `@Debug`, orange `USE` + blue database name, green `--Name:` annotation | Live on every `.squil` file |
| Teal SQL types (`int`, `varchar`, `bigint`, `uniqueidentifier`, …) — distinct from blue keywords | Live on every `.squil` file |
| IntelliSense — SQuiL `@`-prefixes, declared variables, file snippets | Type `@`, or `Ctrl+Space` on a blank header line |
| SSMS native SQL IntelliSense (tables, columns, functions) | Augmented, not replaced — appears alongside SQuiL in the dropdown |
| Hover info — role, SQL type, mapped C# type, target record name | Hover over a `@Param_*` / `@Return_*` |
| Error squigglies — missing/duplicate USE, unknown variable prefix, casing typos, missing `;` | Live as you type |
| Sample data — insert/modify an `Insert Into @Params_X Values …;` block | Type `@` on a blank header line directly under `Declare @Params_X table(...)` — the ⊕ entry appears in the list |
| Preview Generated C# | Right-click in editor → **Preview Generated C#** |
| Build SQuiL Project (`dotnet build` of the nearest `.csproj`/`.sln`) | Right-click in editor → **Build SQuiL Project** |
| Writing Guide (WebView2-hosted) | **Tools → SQuiL Writing Guide** |

---

## Sharing with a colleague (no need to build)

The compiled VSIX is the only artefact you need to share. Located at:

```
C:\dev\projects\SQuiL\SQuiL.SsmsExtension\bin\Debug\SQuiL.SsmsExtension.vsix
```

(Build the extension first if `bin\Debug\` is empty — see *Building from source* below.)

Send your colleague the VSIX plus this section. They run **two commands**
in PowerShell (closing SSMS first):

```powershell
$installer = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe"
$vsix      = "C:\path\to\SQuiL.SsmsExtension.vsix"

# Stop any running SSMS instances:
Get-Process ssms -ErrorAction SilentlyContinue | Stop-Process -Force

# Install:
& $installer /quiet $vsix

# Launch SSMS:
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe"
```

That's it. Open a `.squil` file — it should pick up the SQL Query Editor
(F5 enabled, connection picker visible) and the SQuiL overlay (coral
`@Param_`, etc.).

### To uninstall

```powershell
$installer = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe"
& $installer /quiet /uninstall:SQuiL.SsmsExtension.5b1e9a6e-3c4f-4c1f-9a3e-2a8f8c1e7d20
```

### If the install seems to install but nothing works

A few install/uninstall cycles can leave SSMS in a stale state. Recovery:

```powershell
# 1. Close SSMS:
Get-Process ssms -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. Uninstall:
& $installer /quiet /uninstall:SQuiL.SsmsExtension.5b1e9a6e-3c4f-4c1f-9a3e-2a8f8c1e7d20

# 3. Clear caches:
$ssmsLocal = "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_f16a3f6a"
Get-ChildItem "$ssmsLocal\ComponentModelCache" -File | Remove-Item -Force
Remove-Item "$ssmsLocal\privateregistry.bin" -Force

# 4. Reinstall:
& $installer /quiet $vsix

# 5. Force pkgdef merge:
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe" /setup

# 6. Launch:
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe"
```

The `22.0_f16a3f6a` instance ID may differ on your colleague's machine —
look up with `vswhere.exe -products * -all -property instanceId`.

---

## Prerequisites

- **SQL Server Management Studio 22.6** (or any 22.x — built on the VS 2026 shell).
  Older SSMS versions use the VS 2017 isolated shell and will **not** load this
  VSIX. The manifest targets `Microsoft.VisualStudio.Ssms [22.0,)`.
- **.NET Framework 4.7.2** runtime (ships with Windows).
- **WebView2 Runtime** for the writing-guide tool window. Ships with Windows
  11 24H2 and recent Edge updates; if missing, the guide pane shows an
  inline link to the installer at
  <https://developer.microsoft.com/microsoft-edge/webview2/>. **Every other
  feature still works without WebView2.**
- For *Build SQuiL Project*: **.NET SDK** on `PATH` (`dotnet --version`
  resolves to something).

---

## Building from source

Prerequisites:

- Visual Studio 2026 (Community or higher) **with the "Visual Studio extension
  development" workload installed.**
- The repo's `SQuiL.Editor.Shared/` folder must be intact — the build syncs
  grammar and guide assets out of it.

```powershell
cd C:\dev\projects\SQuiL
$msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild SQuiL.SsmsExtension\SQuiL.SsmsExtension.csproj /t:Restore
& $msbuild SQuiL.SsmsExtension\SQuiL.SsmsExtension.csproj /t:Build
```

Output: `SQuiL.SsmsExtension\bin\Debug\SQuiL.SsmsExtension.vsix`.

> **Use full `MSBuild.exe`, not `dotnet build`.** The VSSDK tasks
> (`VSCTCompile`, `GeneratePkgDef`, `CreateVsixContainer`) are net472-only
> and aren't loaded by the .NET CLI host. `dotnet build` will produce the
> DLL but skip VSIX packaging.

Bump `<Identity Version="…">` in `source.extension.vsixmanifest` before
rebuilding — VSIXInstaller skips re-installation if it sees the same
version already installed.

---

## File-extension configuration inside SSMS

`.squil` is bound automatically by the extension — opening a `.squil` file
gets the SQL editor + SQuiL overlay with no extra setup.

If your team stores SQuiL queries in plain `.sql` files but you only want
SQuiL behaviour on a subset of them, **rename those files to `.squil`**.
Mapping `.sql` itself to the SQuiL editor would suppress SQuiL detection
for files that are pure T-SQL — almost never what you want, because the
SQuiL overlay only adds behaviour, it does not strip SSMS's SQL behaviour.

(Reverse: nothing stops you from opening a `.squil` file with the plain
text editor via File → Open With… → Source Code (Text) Editor. You just
lose F5 and the SQuiL overlay.)

---

## Troubleshooting

### The VSIX installs (exit code 0), but `.squil` files open as plain text

The pkgdef binding (which tells SSMS to open `.squil` with the SQL Query
Editor) didn't take effect. This happens after multiple install cycles or
on a fresh machine that has never run our extension before. Run the
**full reset** above — uninstall, clear `ComponentModelCache` and
`privateregistry.bin`, reinstall, `Ssms.exe /setup`.

### VSIXInstaller returns exit code 2003 ("NoApplicableSKUs")

You scoped the install to the wrong instance with `/instanceIds:…`. Don't
use that flag — `VSIXInstaller.exe /quiet path-to-vsix.vsix` is enough.
The manifest's `Microsoft.VisualStudio.Ssms [22.0,)` target is matched only
by SSMS 22, not by VS Enterprise / Community / Professional installed on
the same machine.

### VSIXInstaller returns exit code 2001 ("RequiresAdminRightsException")

You passed `/admin`. Drop it. SSMS extensions install per-user.

### Right-click commands don't appear in the editor context menu

The `.vsct` is compiled into a `.pkgdef` that SSMS reads once at startup.
After a reinstall, clear `%LOCALAPPDATA%\Microsoft\SSMS\22.0_<id>\ComponentModelCache\*`
and relaunch — SSMS will rebuild its menu and MEF caches.

### Package fails to load (no errors, just no features)

SSMS 22 writes a detailed activity log at:

```
%AppData%\Microsoft\SSMS\22.0_<instance-id>\ActivityLog.xml
```

Open it and search for `SQuiL` — package load failures, MEF composition
errors, and missing dependencies are logged there with stack traces. Run
`Ssms.exe /log` to make sure it's written.

### "Build SQuiL Project" reports `dotnet` not found

`dotnet` must be on the `PATH` that SSMS inherits. Confirm in a fresh
PowerShell:

```powershell
(Get-Command dotnet).Source
```

If that resolves, restart SSMS — the SSMS process was started before the
`PATH` change took effect.

### Writing Guide pane shows "WebView2 Runtime missing"

Install the Evergreen runtime from
<https://developer.microsoft.com/microsoft-edge/webview2/>. Restart SSMS.

---

## Known limitations

- **First-run writing-guide auto-open is disabled.** It deadlocked SSMS
  startup. Open manually from Tools → SQuiL Writing Guide.
- **DB tables don't sort to the top of the IntelliSense list.** SSMS's
  SQL completion source doesn't expose ordering hooks to extensions.
  Tables show in their default alphabetical position.
- **Snippet completions have no Tab-through placeholders.** VS classic
  IntelliSense doesn't host VS Code's snippet syntax — accepting a
  scaffold inserts plain text; users overwrite the example fragments
  by hand.

---

## Layout (developer reference)

```
SQuiL.SsmsExtension/
├── SQuiL.SsmsExtension.csproj            ← SDK-style, net472, VSSDK 17.x
├── source.extension.vsixmanifest         ← targets Microsoft.VisualStudio.Ssms [22.0,) amd64
├── SQuiL.Bindings.pkgdef                 ← binds .squil to SSMS's SQL Query Editor factory
├── SQuiLPackage.cs                       ← AsyncPackage, background load
├── SQuiLPackageGuids.cs                  ← package + cmd-set GUIDs, content-type name
├── Properties/AssemblyInfo.cs
├── VSPackage/
│   ├── SQuiLPackage.vsct                 ← command table (Preview / Build / Open Guide)
│   └── VSPackage.resx
├── ContentType/                          ← IsSquilBuffer helper (file-extension gate)
├── Classification/                       ← syntax highlighting (20 SQuiL scopes)
├── Completion/                           ← IntelliSense, @-trigger filter, Ctrl+Space backup, sample-data marker
├── QuickInfo/                            ← async hover info
├── Tagging/                              ← IErrorTag squigglies
├── Commands/                             ← OleMenuCommand handlers + InsertSampleDataCommand
├── Preview/                              ← C# preview generator
├── Parsing/                              ← parser, linter, SQL→C# type map
├── SampleData/                           ← Insert-block generator + row-count dialog
├── Guide/                                ← WebView2-hosted writing guide tool window
└── Resources/                            ← icons, writing guide HTML, license
```

`SQuiL.Editor.Shared/` (sibling folder) is the canonical home of the
grammar JSON, language configuration, and writing guide. **Edit them
there** — both this extension and the VS Code extension copy from it
at build time.
