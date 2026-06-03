# SQuiL for Visual Studio 2026

A Visual Studio 2026 extension that adds authoring support for SQuiL-annotated
`.squil` files: syntax highlighting, IntelliSense, hover info, linting, and a
generated-C# preview. It is the VS-IDE sibling of **SQuiL for SSMS** — both are
built from the same editor code in this repo.

> `.squil` files are source artefacts for the
> [`SQuiL.SourceGenerator`](https://github.com/daemogar/SQuiL) C# source
> generator. They are also valid T-SQL — the source generator turns them into
> typed C# records and data-context methods at build time.

Unlike the SSMS extension (which overlays SSMS's SQL Query Editor and its F5
execution), the VS extension defines its **own `squil` content type**: VS opens
`.squil` files in the standard code editor and SQuiL overlays its colouring,
completion, hover, and linting on top. There is no F5-against-a-database
experience — that's an SSMS feature.

---

## Features

| Feature | Trigger |
|---|---|
| Open `.squil` files in the VS code editor with SQuiL highlighting | Double-click a `.squil` file (or File → Open) |
| SQuiL syntax overlay — coral `@Param_*`, mauve `@Return_*`, italic `@Debug`, orange `USE` + blue database name, green `--Name:` annotation | Live on every `.squil` file |
| Teal SQL types (`int`, `varchar`, `bigint`, `uniqueidentifier`, …) — distinct from blue keywords | Live on every `.squil` file |
| IntelliSense — SQuiL `@`-prefixes, declared variables, file snippets | Type `@`, or `Ctrl+Space` on a blank header line |
| Hover info — role, SQL type, mapped C# type, target record name | Hover over a `@Param_*` / `@Return_*` |
| Error squigglies — missing/duplicate USE, unknown variable prefix, casing typos, missing `;` | Live as you type |
| Sample data — insert/modify an `Insert Into @Params_X Values …;` block | Type `@` on a blank header line directly under `Declare @Params_X table(...)` — the ⊕ entry appears in the list |
| New SQuiL File | **File → New → New SQuiL File** |
| Preview Generated C# | Right-click in editor → **Preview Generated C#**, or **Tools → Preview Generated C#** |
| Build SQuiL Project (`dotnet build` of the nearest `.csproj`/`.sln`) | Right-click in editor → **Build SQuiL Project**, or **Tools → Build SQuiL Project** |
| Writing Guide (WebView2-hosted) | **Tools → SQuiL Writing Guide** |

---

## Installing (no need to build)

The compiled VSIX is the only artefact you need. Grab
`SQuiL.VisualStudioExtension.vsix` from the
[GitHub release assets](https://github.com/daemogar/SQuiL/releases/latest), or
build it (see below).

Close Visual Studio first, then double-click the `.vsix` — the VSIX Installer
opens, lists "Visual Studio 2026", and installs per-user. Or from PowerShell:

```powershell
$installer = "${env:ProgramFiles}\Microsoft Visual Studio\18\Enterprise\Common7\IDE\VSIXInstaller.exe"
$vsix      = "C:\path\to\SQuiL.VisualStudioExtension.vsix"

Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force
& $installer $vsix
```

(The installer path's edition segment — `Enterprise` / `Professional` /
`Community` — must match your install.)

Launch VS, open a `.squil` file, and the SQuiL overlay (coral `@Param_`, etc.)
appears.

### To uninstall

```powershell
& $installer /uninstall:SQuiL.VisualStudioExtension.7c9d1e3f-4a6b-4c8d-9e0f-1a2b3c4d5e6f
```

---

## Prerequisites

- **Visual Studio 2026** (shell version 18.x — Community, Professional, or
  Enterprise). The manifest targets `Microsoft.VisualStudio.Community [18.0,)`,
  which installs into any 2026 edition. Earlier VS versions will not load it.
- **.NET Framework 4.7.2** runtime (ships with Windows).
- **WebView2 Runtime** for the writing-guide tool window. Ships with Windows 11
  and recent Edge updates; if missing, only the guide pane is affected — every
  other feature still works. Installer:
  <https://developer.microsoft.com/microsoft-edge/webview2/>.
- For *Build SQuiL Project*: **.NET SDK** on `PATH` (`dotnet --version` resolves).

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
& $msbuild SQuiL.VisualStudioExtension\SQuiL.VisualStudioExtension.csproj /t:Restore
& $msbuild SQuiL.VisualStudioExtension\SQuiL.VisualStudioExtension.csproj /t:Build
```

Output: `SQuiL.VisualStudioExtension\bin\Debug\SQuiL.VisualStudioExtension.vsix`.

> **Use full `MSBuild.exe`, not `dotnet build`.** The VSSDK tasks
> (`VSCTCompile`, `GeneratePkgDef`, `CreateVsixContainer`) are net472-only and
> aren't loaded by the .NET CLI host.

Bump `<Identity Version="…">` in `source.extension.vsixmanifest` before
rebuilding — VSIXInstaller skips re-installation if it sees the same version
already installed.

---

## Layout (developer reference)

```
SQuiL.VisualStudioExtension/
├── SQuiL.VisualStudioExtension.csproj      ← SDK-style, net472, VSSDK 17.x
├── source.extension.vsixmanifest           ← targets Microsoft.VisualStudio.Community [18.0,) amd64
├── SQuiLPackage.cs                          ← AsyncPackage, background load
├── SQuiLPackageGuids.cs                     ← package + cmd-set GUIDs, content-type name
├── Properties/AssemblyInfo.cs
├── VSPackage/
│   ├── SQuiLPackage.vsct                    ← command table (New / Preview / Build / Open Guide)
│   └── VSPackage.resx
├── ContentType/                             ← "squil" content type + .squil file-extension mapping
├── Classification/                          ← syntax highlighting (20 SQuiL scopes)
├── Completion/                              ← IntelliSense, @-trigger filter, Ctrl+Space backup, sample-data marker
├── QuickInfo/                               ← async hover info
├── Tagging/                                 ← IErrorTag squigglies
├── Commands/                                ← OleMenuCommand handlers + InsertSampleDataCommand
├── Preview/                                 ← C# preview generator
├── Parsing/                                 ← parser, linter, SQL→C# type map
├── SampleData/                              ← Insert-block generator + row-count dialog
├── Guide/                                   ← WebView2-hosted writing guide tool window
└── Resources/                               ← icons, writing guide HTML, license
```

`SQuiL.Editor.Shared/` (sibling folder) is the canonical home of the grammar
JSON, language configuration, and writing guide. **Edit them there** — this
extension, the SSMS extension, and the VS Code extension all copy from it at
build time.
