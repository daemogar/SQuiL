# Installing SQuiL for SSMS 22.6

You may have up to three files:

- `SQuiL.SsmsExtension.vsix`  ← the extension package (optional — `install.cmd` downloads it if absent)
- `install.cmd`              ← runs the install for you. **You can download just this one file and double-click it** — it self-elevates (UAC prompt) and fetches the `.vsix` from GitHub if it isn't sitting next to it.
- `INSTALL.md`                ← this file

## Things to know before you install

- **SSMS 22.6 required** (or any 22.x). Won't load on SSMS 21 or earlier —
  they're on a different Visual Studio shell. The VSIX manifest targets
  `Microsoft.VisualStudio.Ssms [22.0,)`.
- **WebView2 Runtime** is needed *only* for the Writing Guide pane. Every
  other feature works without it. Modern Windows 11 + Edge ships it; older
  Windows 10 boxes can install it from
  <https://developer.microsoft.com/microsoft-edge/webview2/>.
- **.NET SDK on `PATH`** is needed *only* if you want the **Build SQuiL
  Project** command (it shells out to `dotnet build`).
- **`.squil` files only.** Plain `.sql` files are completely unaffected
  — SSMS's normal SQL behaviour is untouched for them.
- **Install is per-user.** Don't pass `/admin` to VSIXInstaller — that
  changes the install scope and isn't needed; `VSIXInstaller /quiet
  path-to-vsix.vsix` is enough. (Note: the bundled `install.cmd` still
  self-elevates so it can reliably force-close SSMS — but it does **not** pass
  `/admin` to VSIXInstaller.)

## Install (easiest)

**Download `install.cmd` and double-click it.** It prompts for administrator
rights (UAC), then runs the three gated steps for you — force-close SSMS,
install the VSIX, and `Ssms.exe /setup`. **Before it closes SSMS it pauses and
asks you to save any open work**, so nothing is lost (press Enter to continue,
or Ctrl+C to cancel). You don't need the `.vsix` locally; if it isn't sitting
next to `install.cmd`, the matching release is downloaded automatically (the
release tag is baked into the file at publish time).

`install.cmd` is a single file that is both a batch script and a PowerShell
script ("polyglot"). Because it runs as a `.cmd`, Windows does **not** block it
under the PowerShell execution policy and there is no Mark-of-the-Web prompt —
so there's no `Set-ExecutionPolicy`/`-ExecutionPolicy Bypass` dance and no
manual "Run as administrator". Just download and run.

## Install (manual, if you prefer)

Close SSMS first. Then run all four steps in **PowerShell**:

```powershell
$installer = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe"
$ssms      = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe"
$vsix      = "SQuiL.SsmsExtension.vsix"   # or full path to the .vsix you received

# 1. Install the VSIX:
& $installer /quiet $vsix
# Exit code 0 = installed.

# 2. **Required** — force SSMS to merge the new pkgdef into its private
#    registry.  Without this step SSMS won't see the .squil binding and
#    the Tools menu entries won't appear, even though VSIXInstaller
#    reported success.
& $ssms /setup
```

> **Tip:** you don't have to run these by hand — `install.cmd` (see
> **Install (easiest)** above) performs exactly these three gated steps for
> you, prompting you to save open work first and aborting if any step fails
> (e.g. `/setup` never runs unless the VSIX install returned 0). The manual
> steps here are just for reference or troubleshooting.

Open any `.squil` file — SSMS's SQL toolbar (F5 / connection picker /
results pane) appears, plus the SQuiL overlay (coral `@Param_*`, etc.).

The Tools menu now lists **SQuiL Writing Guide**, **Preview Generated C#**,
and **Build SQuiL Project**.

## Verify the install worked

If anything seems off, verify each step:

```powershell
# (1) VSIX files staged under your SSMS instance?
$ssmsLocal = (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\SSMS" -Directory |
              Where-Object Name -like '22.0_*' | Select-Object -First 1).FullName
Get-ChildItem "$ssmsLocal\Extensions" -Recurse -Filter 'SQuiL.SsmsExtension.dll' |
  Select-Object FullName, LastWriteTime
# Expect ONE result under a randomly-named subfolder.

# (2) pkgdef merged into the private registry?
$bytes = [System.IO.File]::ReadAllBytes("$ssmsLocal\privateregistry.bin")
$utf16 = [System.Text.Encoding]::Unicode.GetString($bytes)
if ($utf16 -match 'B5A506EB.*squil|squil.*B5A506EB') {
  "OK: .squil is bound to SSMS's SQL Query Editor."
} else {
  "MISSING: pkgdef hasn't been merged. Run 'Ssms.exe /setup' again."
}
```

## What you get

- F5 / connect / execute work like a normal `.sql` file
- **Tools menu** has three SQuiL entries:
  **SQuiL Writing Guide**, **Preview Generated C#**, **Build SQuiL Project**
- Hover any `@Param_X` or `@Return_X` for role + type info
- Type `@` for SQuiL prefixes; type anything else for SSMS's native SQL IntelliSense
- Error squiggles for missing/duplicate `USE`, unknown variable prefixes, missing semicolons
- Sample-data ⊕ entry in the `@` completion list when sitting under a
  `Declare @Params_X table(...)` declaration

## Uninstall

```powershell
$installer = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe"
Get-Process ssms -ErrorAction SilentlyContinue | Stop-Process -Force
& $installer /quiet /uninstall:SQuiL.SsmsExtension.5b1e9a6e-3c4f-4c1f-9a3e-2a8f8c1e7d20
```

## If the install seems to succeed but the extension never appears

A clean reset — uninstall, clear caches, reinstall, force pkgdef merge,
relaunch:

```powershell
$installer = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe"
$ssms      = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe"
$vsix      = "$PSScriptRoot\SQuiL.SsmsExtension.vsix"   # adjust if elsewhere

# Find your SSMS instance folder (it ends in _<id> — e.g. _f16a3f6a):
$ssmsLocal = (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\SSMS" -Directory |
              Where-Object Name -like '22.0_*' | Select-Object -First 1).FullName

Get-Process ssms -ErrorAction SilentlyContinue | Stop-Process -Force
& $installer /quiet /uninstall:SQuiL.SsmsExtension.5b1e9a6e-3c4f-4c1f-9a3e-2a8f8c1e7d20

Get-ChildItem "$ssmsLocal\ComponentModelCache" -File | Remove-Item -Force
Remove-Item "$ssmsLocal\privateregistry.bin" -Force -ErrorAction SilentlyContinue

& $installer /quiet $vsix
& $ssms /setup      # force pkgdef merge
& $ssms             # relaunch
```

## Common reasons the install doesn't take

| Symptom | Cause | Fix |
|---|---|---|
| `VSIXInstaller` exit code 2003 | Wrong SSMS version (probably 21 or older) | Upgrade to SSMS 22.x |
| Install succeeds, no Tools entries, `.squil` opens as plain text | Skipped step 3 (`Ssms.exe /setup`) | Run `Ssms.exe /setup` and relaunch |
| Install succeeds, still nothing after `/setup` | Stale `privateregistry.bin` from previous attempts | Run the "clean reset" above |
| `VSIXInstaller` exit code 2001 | Passed `/admin` | Drop `/admin`; just `/quiet path.vsix` |
| Writing Guide pane shows "WebView2 Runtime missing" | WebView2 not installed | Install from <https://developer.microsoft.com/microsoft-edge/webview2/> |
