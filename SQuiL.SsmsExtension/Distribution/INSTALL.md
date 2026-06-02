# Installing SQuiL for SSMS 22.6

You may have up to three files:

- `SQuiL.SsmsExtension.vsix`  ← the extension package (optional — `install.ps1` downloads it if absent)
- `install.ps1`              ← runs the install for you; run elevated. **You can download just this file** — it fetches the `.vsix` from GitHub if it isn't sitting next to it.
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
  path-to-vsix.vsix` is enough. (Note: the bundled `install.ps1` still asks
  you to launch PowerShell elevated so it can reliably force-close SSMS — but
  it does **not** pass `/admin` to VSIXInstaller.)

## Install

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

> **Tip:** the included `install.ps1` runs these steps for you. Open
> PowerShell with **Run as administrator** and run `.\install.ps1` from this
> folder. It prompts you to save open work, then runs three gated steps —
> (1) force-close SSMS and wait for it to exit, (2) install the VSIX,
> (3) `Ssms.exe /setup` — and each step must complete successfully before the
> next begins (e.g. `/setup` never runs unless the VSIX install returned 0).
>
> You don't even need the `.vsix` locally: if it isn't next to the script,
> `install.ps1` downloads the release it was published for automatically — no
> version argument needed (the release tag is baked into the script). Point at
> a specific local file with `-VsixPath <path>` if you ever need to.
>
> **PowerShell won't run the downloaded script** ("...is not digitally
> signed")? Downloaded `.ps1` files are blocked by the execution policy.
> Launch it elevated with the policy bypassed for just that process — this
> also satisfies the "run as administrator" requirement:
>
> ```powershell
> Start-Process powershell -Verb RunAs -ArgumentList '-NoExit','-NoProfile','-ExecutionPolicy','Bypass','-File',"$HOME\Downloads\install.ps1"
> ```
>
> (adjust the path if you saved it elsewhere). Already in an elevated prompt?
> Run `Set-ExecutionPolicy -Scope Process Bypass -Force` once, then
> `.\install.ps1`. `-Scope Process` is session-only and reverts when you close
> the window.

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
