<# :
@echo off & setlocal
:: =====================================================================
:: SQuiL SSMS extension installer  (install.cmd)
::
:: WHY THIS FILE IS A .cmd AND NOT A .ps1
::   Browsers tag every download with the "Mark of the Web", and Windows
::   PowerShell refuses to run a downloaded *.ps1 under the default
::   execution policy ("...is not digitally signed"). That used to force
::   users to type an ExecutionPolicy-bypass one-liner by hand and to
::   right-click > Run as administrator. A *.cmd has neither restriction,
::   so this file can be downloaded straight from the browser and just
::   double-clicked.
::
:: WHAT THIS FILE ACTUALLY IS  (a batch / PowerShell "polyglot")
::   This single file is simultaneously a VALID BATCH SCRIPT and a VALID
::   POWERSHELL SCRIPT:
::     * cmd.exe runs the batch lines below. The ":: ..." lines are batch
::       comments; the very first line of the file is a harmless label to
::       cmd (and the start of a block comment to PowerShell).
::     * PowerShell sees everything from the block-comment OPEN marker on the
::       first line down to the matching CLOSE marker near the bottom of this
::       header as ONE big block comment, so it skips every batch line and
::       begins executing at the real PowerShell code beneath that close
::       marker.
::   The two interpreters read the same bytes and each ignores the other's
::   half. Nothing is duplicated or kept in sync by hand.
::
:: WHAT HAPPENS WHEN YOU RUN IT
::   1. The batch half checks for admin rights. A machine-wide SSMS
::      extension install needs them, so if we are NOT elevated it
::      relaunches THIS same file through a UAC prompt and exits the
::      unelevated copy.
::   2. Once elevated, it hands its own full path to PowerShell and runs
::      the PowerShell body of THIS file in memory:
::          powershell ... "iex (${%~f0} | Out-String)"
::      Executing in-memory text (rather than a *.ps1 sitting on disk) is
::      itself immune to the execution policy and the Mark of the Web.
::   3. The PowerShell body resolves the matching .vsix (explicit path >
::      a copy next to this file > download from the GitHub release whose
::      tag is baked in at publish time), then runs three gated steps:
::      force-close SSMS, install the VSIX, and Ssms.exe /setup to merge
::      the package registration. Each step must succeed before the next.
::
:: MAINTAINER NOTE
::   Edit the install logic in the PowerShell section below exactly as you
::   would a normal .ps1 — no batch escaping applies there. Only the small
::   batch header up here keeps the polyglot working: do not change the very
::   first line of this file, nor the comment-terminator line immediately
::   below this header (the line that ends the batch section).
::   CAUTION: do NOT write the PowerShell block-comment OPEN or CLOSE markers
::   literally anywhere in this header. PowerShell ends the block comment at
::   the FIRST close marker it sees, so a literal one in prose would end this
::   comment early and break the in-memory dispatch. (That is exactly the bug
::   this note used to contain.) Describe them in words instead.
:: =====================================================================

:: --- 1. Self-elevate: relaunch this file through UAC if not admin ---
::   Admin check uses fltmc (filter-manager control), which is admin-only and
::   returns instantly. 'net session' was used before but can stall for several
::   seconds enumerating SMB sessions, which made the post-UAC window feel hung.
fltmc >nul 2>&1
if %errorlevel% NEQ 0 (
    echo Requesting administrator privileges...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

:: --- 2. Admin confirmed. Pass our own path to PowerShell and run the
::        PowerShell body of THIS file. The echo gives the freshly-elevated
::        window instant text while PowerShell cold-starts (~1s). ---
set "SQUIL_CMD_PATH=%~f0"
set "SQUIL_CMD_DIR=%~dp0"
echo Starting SQuiL installer, please wait...
powershell -NoProfile -ExecutionPolicy Bypass "iex (${%~f0} | Out-String)"
exit /b %errorlevel%
: end batch portion / begin PowerShell #>

# ─────────────────────────────────────────────────────────────────────────
# PowerShell body — runs elevated, in memory, via the batch header above.
# This is the SSMS extension installer proper. Treat it like a normal .ps1.
# ─────────────────────────────────────────────────────────────────────────

$ErrorActionPreference = 'Stop'

# Disable the console's QuickEdit mode. With QuickEdit on (the Windows default),
# clicking anywhere in the window enters text-SELECTION mode, which FREEZES the
# app until you press Esc / right-click — so prompts appear dead and no key (or
# pause, or Read-Host) responds. Turning it off means a click just focuses the
# window. Best-effort: wrapped so a failure never blocks the install.
try {
    Add-Type -Namespace SQuiLInstaller -Name Con -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
public static extern System.IntPtr GetStdHandle(int nStdHandle);
[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
public static extern bool GetConsoleMode(System.IntPtr hConsoleHandle, out uint lpMode);
[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
public static extern bool SetConsoleMode(System.IntPtr hConsoleHandle, uint dwMode);
'@ -ErrorAction Stop
    $stdIn = [SQuiLInstaller.Con]::GetStdHandle(-10)   # STD_INPUT_HANDLE
    $mode = [uint32]0
    if ([SQuiLInstaller.Con]::GetConsoleMode($stdIn, [ref]$mode)) {
        $ENABLE_QUICK_EDIT = [uint32]0x0040
        $ENABLE_EXTENDED   = [uint32]0x0080
        $new = ($mode -band (-bnot $ENABLE_QUICK_EDIT)) -bor $ENABLE_EXTENDED
        [void][SQuiLInstaller.Con]::SetConsoleMode($stdIn, $new)
    }
}
catch { }

# These were `param()` in the old install.ps1. The .cmd never forwards
# arguments, so they are plain variables now. Set $VsixPath only when
# testing a specific local build.
$Repo     = 'daemogar/SQuiL'
$VsixPath = $null

# Release tag baked in at publish time by .github/workflows/publish.yml, which
# replaces the placeholder below with the release version. This lets a
# downloaded copy of just this file install the exact matching .vsix with no
# version argument. When the placeholder is still present (e.g. a local build),
# $releaseTag is left null and we fall back to the latest GitHub release.
$BakedReleaseTag = '__SQUIL_RELEASE_TAG__'
$releaseTag = if ($BakedReleaseTag -match '^__.*__$') { $null } else { $BakedReleaseTag }

# ── Resolve the .vsix: explicit path > local-next-to-script > download ─────
function Get-SquilVsix {
    param([string]$Repo, [string]$Tag)

    # Windows PowerShell 5.1 may default to TLS 1.0; GitHub requires 1.2+.
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    $apiHeaders = @{ 'User-Agent' = 'SQuiL-install.cmd'; 'Accept' = 'application/vnd.github+json' }

    if ($Tag) {
        $release = Invoke-RestMethod -Headers $apiHeaders `
            -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag"
    }
    else {
        # /releases is newest-first and includes prereleases (our -beta builds);
        # /releases/latest would skip them. Take the newest that has our asset.
        $release = Invoke-RestMethod -Headers $apiHeaders `
            -Uri "https://api.github.com/repos/$Repo/releases" |
            Where-Object { $_.assets.name -contains 'SQuiL.SsmsExtension.vsix' } |
            Select-Object -First 1
    }
    if (-not $release) {
        throw "No release with a SQuiL.SsmsExtension.vsix asset was found in '$Repo'."
    }

    $asset = $release.assets |
        Where-Object { $_.name -eq 'SQuiL.SsmsExtension.vsix' } |
        Select-Object -First 1
    if (-not $asset) {
        throw "Release '$($release.tag_name)' has no SQuiL.SsmsExtension.vsix asset."
    }

    $dest = Join-Path ([IO.Path]::GetTempPath()) "SQuiL.SsmsExtension-$($release.tag_name).vsix"
    Write-Host "Downloading SQuiL.SsmsExtension.vsix from release '$($release.tag_name)'..." -ForegroundColor Cyan
    $prev = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'   # IWR is far faster without the progress bar
    try {
        Invoke-WebRequest -Headers @{ 'User-Agent' = 'SQuiL-install.cmd' } `
            -Uri $asset.browser_download_url -OutFile $dest
    }
    finally { $ProgressPreference = $prev }

    Unblock-File $dest   # strip mark-of-the-web so VSIXInstaller won't balk
    return $dest
}

# Pause for ANY key. Reads a single keypress straight from the console input
# buffer (CONIN$) via [Console]::ReadKey. That matters here: this body runs as
# `cmd -> powershell "iex ..."`, and in that chain STDIN does NOT receive the
# keystrokes — so Read-Host (Enter-only, reads stdin) and cmd's `pause` (also
# stdin) both hang. ReadKey reads the console directly and works. Do NOT set
# TreatControlCAsInput around it: that call throws in this host and would drop
# us into the silent Read-Host fallback (the bug behind earlier "won't close").
function Wait-AnyKey([string]$Message) {
    Write-Host $Message -ForegroundColor Yellow
    try {
        [void][System.Console]::ReadKey($true)
    }
    catch {
        # No interactive console (e.g. stdin redirected during testing).
        Write-Host '(press Enter)' -ForegroundColor DarkGray
        [void](Read-Host)
    }
}

$exitCode = 0
try {
    # Defensive admin check. The .cmd header already elevates, so this only
    # fires if someone runs this PowerShell body directly without elevation.
    $principal = New-Object Security.Principal.WindowsPrincipal(
        [Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Not running elevated. Run install.cmd (it self-elevates via UAC) rather than invoking this script body directly."
    }

    # SSMS tool paths (as documented in INSTALL.md).
    # TODO: these are hardcoded to '...Studio 22\Release\...'; discovery over
    # version/channel (Release|Preview) is a known follow-up.
    $installer = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe"
    $ssms      = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe"
    if (-not (Test-Path $installer)) { throw "VSIXInstaller not found: $installer" }
    if (-not (Test-Path $ssms))      { throw "Ssms.exe not found: $ssms" }

    # The batch header exported the .cmd's own folder; use it to find a
    # sibling .vsix (the old install.ps1 used $PSScriptRoot, which is empty
    # when this body runs in-memory via iex).
    $scriptDir = if ($env:SQUIL_CMD_DIR) { $env:SQUIL_CMD_DIR } else { $PSScriptRoot }
    $localVsix = Join-Path $scriptDir 'SQuiL.SsmsExtension.vsix'

    if ($VsixPath) {
        if (-not (Test-Path $VsixPath)) { throw "VSIX not found: $VsixPath" }
        $vsix = (Resolve-Path $VsixPath).Path
        Write-Host "Using VSIX: $vsix" -ForegroundColor Cyan
    }
    elseif (Test-Path $localVsix) {
        $vsix = $localVsix
        Write-Host "Using VSIX next to this installer: $vsix" -ForegroundColor Cyan
    }
    else {
        Write-Host 'No local .vsix found — fetching it from GitHub releases.' -ForegroundColor Cyan
        $vsix = Get-SquilVsix -Repo $Repo -Tag $releaseTag
        Write-Host "Downloaded VSIX: $vsix" -ForegroundColor Green
    }

    # ── Prompt: let the user save open work before SSMS is force-closed ───
    # To cancel before anything destructive happens, just close the window —
    # nothing has been changed at this point.
    Write-Host ''
    Write-Host 'Step 1 will force-close SSMS to install the SQuiL extension.' -ForegroundColor Yellow
    Write-Host 'Save any open work in SSMS now.' -ForegroundColor Yellow
    Wait-AnyKey 'Press any key to continue, or close this window to cancel.'

    # ── Step 1: force-close SSMS (re-query and kill until none remain) ─────
    # Capturing the process list once and waiting on those handles proved
    # brittle (a lingering/child or a slow exit left the final check seeing a
    # stray 'ssms' and threw). Instead, loop: re-query each pass and force-kill
    # the whole tree with taskkill /T until no ssms process remains, or 30s.
    Write-Host '== Step 1: Closing SSMS ==' -ForegroundColor Cyan
    if (@(Get-Process -Name ssms -ErrorAction SilentlyContinue).Count -eq 0) {
        Write-Host '  SSMS is not running.' -ForegroundColor DarkGray
    }
    else {
        # SSMS (a VS-shell app) can take a few seconds to fully exit. Print a
        # growing row of dots while we wait so it is obviously still working,
        # rather than sitting silent. Re-query and force-kill the whole tree
        # each pass until none remain, or 30s.
        Write-Host '  Force-closing SSMS, please wait' -NoNewline -ForegroundColor Yellow
        $deadline = (Get-Date).AddSeconds(30)
        while (@(Get-Process -Name ssms -ErrorAction SilentlyContinue).Count -gt 0) {
            # taskkill /F = force, /T = whole process tree. Native exit code
            # (e.g. "process not found") does not throw, so no -ErrorAction.
            & taskkill /F /T /IM Ssms.exe *> $null
            Write-Host '.' -NoNewline -ForegroundColor Yellow
            Start-Sleep -Milliseconds 750
            if ((Get-Date) -ge $deadline) {
                Write-Host ''
                throw "Step 1 failed: SSMS is still running after 30s. Close it manually, then re-run. (Step 2 not started.)"
            }
        }
        Write-Host ' closed.' -ForegroundColor Yellow
    }
    Write-Host 'Step 1 complete (SSMS closed).' -ForegroundColor Green

    # ── Step 2: install the VSIX (wait for full completion) ───────────────
    Write-Host '== Step 2: VSIXInstaller /quiet (installing extension) ==' -ForegroundColor Cyan
    $step2 = Start-Process -FilePath $installer -ArgumentList '/quiet', "`"$vsix`"" -Wait -PassThru
    if ($step2.ExitCode -ne 0) {
        throw "Step 2 (VSIX install) failed with exit code $($step2.ExitCode). Aborting before step 3 (/setup)."
    }
    Write-Host 'Step 2 complete (exit code 0).' -ForegroundColor Green

    # ── Step 3: force pkgdef merge — only after step 2 succeeded ──────────
    Write-Host '== Step 3: Ssms.exe /setup (merging pkgdef) ==' -ForegroundColor Cyan
    $step3 = Start-Process -FilePath $ssms -ArgumentList '/setup' -Wait -PassThru
    if ($step3.ExitCode -ne 0) {
        throw "Step 3 (Ssms.exe /setup) failed with exit code $($step3.ExitCode)."
    }
    Write-Host 'Step 3 complete (exit code 0).' -ForegroundColor Green

    Write-Host 'Install finished. Launch SSMS and open a .squil file to verify.' -ForegroundColor Green
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
finally {
    # The elevated console is a fresh window; pause so the user can read the
    # result before it closes. Accept any key (not just Enter).
    Write-Host ''
    Wait-AnyKey 'Press any key to close...'
}

exit $exitCode
