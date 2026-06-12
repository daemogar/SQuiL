<# :
@echo off & setlocal
:: =====================================================================
:: SQuiL extension installer  (install.cmd) — SSMS 22+ and Visual Studio 2026
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
::   3. The PowerShell body uses vswhere to discover every supported product
::      on this machine — SSMS 22+ and Visual Studio 2026 (18+) Community/
::      Professional/Enterprise — resolves the matching .vsix for each
::      (explicit path > a copy next to this file > download from the GitHub
::      release whose tag is baked in at publish time), then runs the gated
::      steps: force-close the affected apps, install each VSIX into its
::      instance, and Ssms.exe /setup to merge the SSMS package registration.
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
# This installs the SQuiL extension into every supported product found on
# the machine (SSMS 22+, VS 2026 Community/Pro/Enterprise). Treat it like a
# normal .ps1.
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
# arguments, so they are plain variables now. Set them only when testing
# specific local builds (one per product's package).
$Repo         = 'daemogar/SQuiL'
$SsmsVsixPath = $null
$VsVsixPath   = $null

# Release tag baked in at publish time by .github/workflows/publish.yml, which
# replaces the placeholder below with the release version. This lets a
# downloaded copy of just this file install the exact matching .vsix with no
# version argument. When the placeholder is still present (e.g. a local build),
# $releaseTag is left null and we fall back to the latest GitHub release.
$BakedReleaseTag = '__SQUIL_RELEASE_TAG__'
$releaseTag = if ($BakedReleaseTag -match '^__.*__$') { $null } else { $BakedReleaseTag }

# ── Discover installed products via vswhere ────────────────────────────────
# vswhere ships with the VS installer engine, which both VS 2017+ and SSMS 22
# install — so if neither candidate path exists, no supported product can be
# present either.
function Get-VsWherePath {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\Installer\vswhere.exe"
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    throw "vswhere.exe not found. Install SSMS 22+ or Visual Studio 2026 first (either one provides the Visual Studio Installer that vswhere ships with)."
}

# One object per applicable instance: which product it is, where its
# VSIXInstaller lives, which .vsix asset it needs, and which process to close.
function Get-SquilTargets {
    $vswhere = Get-VsWherePath
    $instances = & $vswhere -products * -prerelease -format json | ConvertFrom-Json
    $vsProductIds = @(
        'Microsoft.VisualStudio.Product.Community',
        'Microsoft.VisualStudio.Product.Professional',
        'Microsoft.VisualStudio.Product.Enterprise'
    )

    $targets = @()
    foreach ($i in $instances) {
        $major = 0
        [void][int]::TryParse(($i.installationVersion -split '\.')[0], [ref]$major)
        $installer = Join-Path $i.installationPath 'Common7\IDE\VSIXInstaller.exe'
        if (-not (Test-Path $installer)) { continue }

        if ($i.productId -eq 'Microsoft.VisualStudio.Product.Ssms' -and $major -ge 22) {
            $targets += [pscustomobject]@{
                Kind        = 'SSMS'
                Name        = $i.displayName
                InstanceId  = $i.instanceId
                Installer   = $installer
                ProductPath = $i.productPath          # Ssms.exe — needed for /setup
                AssetName   = 'SQuiL.SsmsExtension.vsix'
                Process     = 'ssms'
            }
        }
        elseif ($vsProductIds -contains $i.productId -and $major -ge 18) {
            $targets += [pscustomobject]@{
                Kind        = 'VS'
                Name        = $i.displayName
                InstanceId  = $i.instanceId
                Installer   = $installer
                ProductPath = $i.productPath          # devenv.exe
                AssetName   = 'SQuiL.VisualStudioExtension.vsix'
                Process     = 'devenv'
            }
        }
    }
    return ,$targets
}

# ── Resolve a .vsix: explicit path > local-next-to-script > download ───────
function Get-SquilVsix {
    param([string]$Repo, [string]$Tag, [string]$AssetName)

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
            Where-Object { $_.assets.name -contains $AssetName } |
            Select-Object -First 1
    }
    if (-not $release) {
        throw "No release with a $AssetName asset was found in '$Repo'."
    }

    $asset = $release.assets |
        Where-Object { $_.name -eq $AssetName } |
        Select-Object -First 1
    if (-not $asset) {
        throw "Release '$($release.tag_name)' has no $AssetName asset."
    }

    $base = [IO.Path]::GetFileNameWithoutExtension($AssetName)
    $dest = Join-Path ([IO.Path]::GetTempPath()) "$base-$($release.tag_name).vsix"
    Write-Host "Downloading $AssetName from release '$($release.tag_name)'..." -ForegroundColor Cyan
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

function Resolve-SquilVsix {
    param([string]$Explicit, [string]$AssetName, [string]$ScriptDir)

    if ($Explicit) {
        if (-not (Test-Path $Explicit)) { throw "VSIX not found: $Explicit" }
        $vsix = (Resolve-Path $Explicit).Path
        Write-Host "Using VSIX: $vsix" -ForegroundColor Cyan
        return $vsix
    }
    $local = Join-Path $ScriptDir $AssetName
    if (Test-Path $local) {
        Write-Host "Using VSIX next to this installer: $local" -ForegroundColor Cyan
        return $local
    }
    Write-Host "No local $AssetName found - fetching it from GitHub releases." -ForegroundColor Cyan
    $vsix = Get-SquilVsix -Repo $Repo -Tag $releaseTag -AssetName $AssetName
    Write-Host "Downloaded VSIX: $vsix" -ForegroundColor Green
    return $vsix
}

# ── Force-close one product's processes (re-query and kill until none) ─────
# Capturing the process list once and waiting on those handles proved
# brittle (a lingering/child or a slow exit left the final check seeing a
# stray process and threw). Instead, loop: re-query each pass and force-kill
# the whole tree with taskkill /T until none remain, or 30s.
function Stop-ProductProcesses {
    param([string]$ProcessName, [string]$DisplayName)

    if (@(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue).Count -eq 0) {
        Write-Host "  $DisplayName is not running." -ForegroundColor DarkGray
        return
    }
    # A VS-shell app can take a few seconds to fully exit. Print a growing row
    # of dots while we wait so it is obviously still working.
    Write-Host "  Force-closing $DisplayName, please wait" -NoNewline -ForegroundColor Yellow
    $deadline = (Get-Date).AddSeconds(30)
    while (@(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue).Count -gt 0) {
        # taskkill /F = force, /T = whole process tree. Native exit code
        # (e.g. "process not found") does not throw, so no -ErrorAction.
        & taskkill /F /T /IM "$ProcessName.exe" *> $null
        Write-Host '.' -NoNewline -ForegroundColor Yellow
        Start-Sleep -Milliseconds 750
        if ((Get-Date) -ge $deadline) {
            Write-Host ''
            throw "Step 1 failed: $DisplayName is still running after 30s. Close it manually, then re-run. (Nothing has been installed.)"
        }
    }
    Write-Host ' closed.' -ForegroundColor Yellow
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

    # The batch header exported the .cmd's own folder; use it to find a
    # sibling .vsix (the old install.ps1 used $PSScriptRoot, which is empty
    # when this body runs in-memory via iex).
    $scriptDir = if ($env:SQUIL_CMD_DIR) { $env:SQUIL_CMD_DIR } else { $PSScriptRoot }

    # ── Discover what's installed ──────────────────────────────────────────
    Write-Host '== Looking for supported products (SSMS 22+, Visual Studio 2026) ==' -ForegroundColor Cyan
    $targets = Get-SquilTargets
    if ($targets.Count -eq 0) {
        throw "No supported product found. SQuiL's editor extensions need SSMS 22 (or later) and/or Visual Studio 2026 (18+) Community/Professional/Enterprise."
    }
    foreach ($t in $targets) {
        Write-Host "  Found: $($t.Name)  [$($t.InstanceId)]" -ForegroundColor Green
    }

    # ── Resolve one .vsix per product family that is present ───────────────
    $vsixByAsset = @{}
    foreach ($assetName in ($targets | Select-Object -ExpandProperty AssetName -Unique)) {
        $explicit = if ($assetName -eq 'SQuiL.SsmsExtension.vsix') { $SsmsVsixPath } else { $VsVsixPath }
        $vsixByAsset[$assetName] = Resolve-SquilVsix -Explicit $explicit -AssetName $assetName -ScriptDir $scriptDir
    }

    # ── Prompt: let the user save open work before apps are force-closed ───
    # To cancel before anything destructive happens, just close the window —
    # nothing has been changed at this point.
    $appNames = ($targets | Select-Object -ExpandProperty Name -Unique) -join ', '
    Write-Host ''
    Write-Host "Step 1 will force-close: $appNames" -ForegroundColor Yellow
    Write-Host 'Save any open work in those applications now.' -ForegroundColor Yellow
    Wait-AnyKey 'Press any key to continue, or close this window to cancel.'

    # ── Step 1: force-close every affected product ──────────────────────────
    Write-Host '== Step 1: Closing applications ==' -ForegroundColor Cyan
    foreach ($proc in ($targets | Select-Object -ExpandProperty Process -Unique)) {
        $name = ($targets | Where-Object Process -eq $proc | Select-Object -First 1).Name
        Stop-ProductProcesses -ProcessName $proc -DisplayName $name
    }
    Write-Host 'Step 1 complete (applications closed).' -ForegroundColor Green

    # ── Step 2: install each VSIX into its own instance ────────────────────
    # /instanceIds scopes the install: VSIXInstaller delegates to the shared
    # VS installer engine, which otherwise offers the package to every
    # compatible instance on the machine.
    $failures = @()
    Write-Host '== Step 2: VSIXInstaller /quiet (installing extension) ==' -ForegroundColor Cyan
    foreach ($t in $targets) {
        $vsix = $vsixByAsset[$t.AssetName]
        Write-Host "  Installing into $($t.Name)..." -ForegroundColor Cyan
        $step = Start-Process -FilePath $t.Installer `
            -ArgumentList '/quiet', "/instanceIds:$($t.InstanceId)", "`"$vsix`"" -Wait -PassThru
        if ($step.ExitCode -ne 0) {
            $failures += "$($t.Name): VSIX install failed with exit code $($step.ExitCode)."
            Write-Host "  FAILED (exit code $($step.ExitCode))." -ForegroundColor Red
        }
        else {
            Write-Host "  Installed (exit code 0)." -ForegroundColor Green
        }
    }
    if ($failures.Count -eq $targets.Count) {
        throw "Step 2 failed for every product:`n  $($failures -join "`n  ")"
    }

    # ── Step 3: SSMS only — force pkgdef merge, only where install worked ──
    # Without /setup SSMS won't see the .squil binding even though
    # VSIXInstaller reported success. VS needs no equivalent step.
    $ssmsTargets = @($targets | Where-Object { $_.Kind -eq 'SSMS' -and ($failures -notmatch [regex]::Escape($_.Name)) })
    if ($ssmsTargets.Count -gt 0) {
        Write-Host '== Step 3: Ssms.exe /setup (merging pkgdef) ==' -ForegroundColor Cyan
        foreach ($t in $ssmsTargets) {
            $step = Start-Process -FilePath $t.ProductPath -ArgumentList '/setup' -Wait -PassThru
            if ($step.ExitCode -ne 0) {
                $failures += "$($t.Name): Ssms.exe /setup failed with exit code $($step.ExitCode)."
                Write-Host "  $($t.Name): /setup FAILED (exit code $($step.ExitCode))." -ForegroundColor Red
            }
            else {
                Write-Host "  $($t.Name): /setup complete (exit code 0)." -ForegroundColor Green
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Install finished with errors:`n  $($failures -join "`n  ")"
    }
    Write-Host 'Install finished. Launch the application(s) and open a .squil file to verify.' -ForegroundColor Green
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
