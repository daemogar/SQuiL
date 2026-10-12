<# :
@echo off & setlocal
:: =====================================================================
:: SQuiL extension installer  (SQuiL-Install.cmd) — SSMS 22+, Visual Studio 2026
:: and Visual Studio Code
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
::   1. The batch half works out which PHASE to run and hands its own full
::      path to PowerShell, which runs the PowerShell body of THIS file in
::      memory:
::          powershell ... "iex (${%~f0} | Out-String)"
::      Executing in-memory text (rather than a *.ps1 sitting on disk) is
::      itself immune to the execution policy and the Mark of the Web.
::   2. Phase "user" (double-clicked, not elevated): installs the VS Code
::      extension AS YOU, into every VS Code / VS Code Insiders found. VS Code
::      extensions are per-user, so this must happen before elevating: an
::      elevated run under someone else's admin credentials would install into
::      THEIR profile. Then, only if SSMS 22+ or Visual Studio 2026 is present,
::      it relaunches THIS file through a UAC prompt with --elevated.
::   3. Phase "admin" (that --elevated relaunch): uses vswhere to discover
::      SSMS 22+ and Visual Studio 2026 (18+) Community/Professional/
::      Enterprise, resolves the matching .vsix for each (explicit path > a
::      copy next to this file > download from the GitHub release whose tag is
::      baked in at publish time), then runs the gated steps: force-close the
::      affected apps, install each VSIX into its instance, and Ssms.exe
::      /setup to merge the SSMS package registration.
::   4. Phase "all" (started already elevated, e.g. "Run as administrator"):
::      does both, but installs the VS Code extension only when the elevated
::      account is the one signed in to this desktop.
:: =====================================================================

:: --- 1. Pick the phase ---
::   fltmc (filter-manager control) is admin-only and returns instantly, so it
::   doubles as the elevation check. 'net session' was used before but can
::   stall for several seconds enumerating SMB sessions.
if /i "%~1"=="--elevated" (
    set "SQUIL_PHASE=admin"
    goto :run
)
fltmc >nul 2>&1
if %errorlevel% EQU 0 (set "SQUIL_PHASE=all") else (set "SQUIL_PHASE=user")

:: --- 2. Pass our own path to PowerShell and run the PowerShell body of THIS
::        file. The echo gives the window instant text while PowerShell
::        cold-starts (~1s). ---
:run
set "SQUIL_CMD_PATH=%~f0"
set "SQUIL_CMD_DIR=%~dp0"
echo Starting SQuiL installer, please wait...
powershell -NoProfile -ExecutionPolicy Bypass "iex (${%~f0} | Out-String)"
exit /b %errorlevel%
: end batch portion / begin PowerShell #>

# ─────────────────────────────────────────────────────────────────────────
# PowerShell body — runs in memory via the batch header above, in the phase
# the header chose ($env:SQUIL_PHASE: user / admin / all). It installs the
# SQuiL extension into every supported product found on the machine (VS Code
# + Insiders, SSMS 22+, VS 2026 Community/Pro/Enterprise). Treat it like a
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
$Repo             = 'daemogar/SQuiL'
$SsmsVsixPath     = $null
$VsVsixPath       = $null
$VsCodeVsixPath   = $null
$VsCodeAssetName  = 'SQuiL-VSCode.vsix'

# Release tag baked in at publish time by .github/workflows/publish.yml, which
# replaces the placeholder below with the release version. This lets a
# downloaded copy of just this file install the exact matching .vsix with no
# version argument. When the placeholder is still present (e.g. a local build),
# $releaseTag is left null and we fall back to the latest GitHub release.
$BakedReleaseTag = '__SQUIL_RELEASE_TAG__'
$releaseTag = if ($BakedReleaseTag -match '^__.*__$') { $null } else { $BakedReleaseTag }

# ── Discover installed products via vswhere ────────────────────────────────
# vswhere ships with the VS installer engine, which both VS 2017+ and SSMS 22
# install — so if neither candidate path exists, no SSMS/VS product can be
# present either (a VS Code-only machine has no vswhere, and that is fine).
function Get-VsWherePath {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\Installer\vswhere.exe"
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    return $null
}

# One object per applicable instance: which product it is, where its
# VSIXInstaller lives, which .vsix asset it needs, and which process to close.
function Get-SquilTargets {
    $vswhere = Get-VsWherePath
    if (-not $vswhere) { return ,@() }
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
                AssetName   = 'SQuiL-SSMS.vsix'
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
                AssetName   = 'SQuiL-VisualStudio.vsix'
                Process     = 'devenv'
            }
        }
    }
    return ,$targets
}

# ── Discover VS Code (stable + Insiders) ───────────────────────────────────
# vswhere can't see VS Code, so probe for its CLI: PATH first, then the
# per-user and system install folders. One CLI per flavour is enough — a
# per-user and a system install of the same flavour share one extensions
# folder (%USERPROFILE%\.vscode[-insiders]\extensions). Each candidate must sit
# beside the flavour's own Code.exe, which keeps forks (Cursor, VSCodium, ...)
# that ship a look-alike `code` CLI out.
function Get-VsCodeTargets {
    $flavours = @(
        @{ Name = 'Visual Studio Code';            Cli = 'code';          Exe = 'Code.exe';            Folder = 'Microsoft VS Code' },
        @{ Name = 'Visual Studio Code - Insiders'; Cli = 'code-insiders'; Exe = 'Code - Insiders.exe'; Folder = 'Microsoft VS Code Insiders' }
    )

    $targets = @()
    foreach ($f in $flavours) {
        $candidates = @()
        $onPath = Get-Command "$($f.Cli).cmd" -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty Source
        if ($onPath) { $candidates += $onPath }
        foreach ($root in @("$env:LOCALAPPDATA\Programs", $env:ProgramFiles)) {
            if ($root) { $candidates += Join-Path $root "$($f.Folder)\bin\$($f.Cli).cmd" }
        }

        $cli = $candidates |
            Where-Object { (Test-Path $_) -and (Test-Path (Join-Path (Split-Path (Split-Path $_)) $f.Exe)) } |
            Select-Object -First 1
        if ($cli) {
            $targets += [pscustomobject]@{ Name = $f.Name; Cli = $cli }
        }
    }
    return ,$targets
}

# True when this process runs as the account signed in to this desktop (the
# owner of explorer.exe in our session). Phase "all" uses it to avoid putting
# the VS Code extension into another account's profile. Unknown = $false.
function Test-IsDesktopUser {
    try {
        $me = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $session = (Get-Process -Id $PID).SessionId
        $shell = Get-CimInstance Win32_Process -Filter "Name='explorer.exe' AND SessionId=$session" |
            Select-Object -First 1
        if (-not $shell) { return $false }
        $owner = Invoke-CimMethod -InputObject $shell -MethodName GetOwnerSid
        return ($owner.Sid -eq $me)
    }
    catch { return $false }
}

# ── Resolve a .vsix: explicit path > local-next-to-script > download ───────
function Get-SquilVsix {
    param([string]$Repo, [string]$Tag, [string]$AssetName)

    # Windows PowerShell 5.1 may default to TLS 1.0; GitHub requires 1.2+.
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    $apiHeaders = @{ 'User-Agent' = 'SQuiL-Install.cmd'; 'Accept' = 'application/vnd.github+json' }

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
        Invoke-WebRequest -Headers @{ 'User-Agent' = 'SQuiL-Install.cmd' } `
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

# ── VS Code: install the extension into each flavour found ─────────────────
# Runs as the invoking user. `code --install-extension` works while VS Code is
# open (it picks the extension up on reload), so nothing is closed. --force
# installs over an existing copy without prompting. Returns failures.
function Install-VsCodeTargets {
    param([object[]]$Targets, [string]$ScriptDir)

    Write-Host '== VS Code: installing extension ==' -ForegroundColor Cyan
    $vsix = Resolve-SquilVsix -Explicit $VsCodeVsixPath -AssetName $VsCodeAssetName -ScriptDir $ScriptDir

    $failures = @()
    foreach ($t in $Targets) {
        Write-Host "  Installing into $($t.Name)..." -ForegroundColor Cyan
        # code.cmd is a batch file, and cmd mangles quotes when both it and the
        # .vsix path contain spaces. `cmd /s /c ""…" … "…""` strips only the outer
        # pair, so an exact argument string survives; 2>&1 merges stderr in cmd.
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $env:ComSpec
        $psi.Arguments = "/d /s /c `"`"$($t.Cli)`" --install-extension `"$vsix`" --force 2>&1`""
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $proc = [System.Diagnostics.Process]::Start($psi)
        $output = $proc.StandardOutput.ReadToEnd()
        $proc.WaitForExit()
        $code = $proc.ExitCode
        foreach ($line in ($output -split "`r?`n" | Where-Object { $_ })) {
            Write-Host "    $line" -ForegroundColor DarkGray
        }
        if ($code -ne 0) {
            $failures += "$($t.Name): extension install failed with exit code $code."
            Write-Host "  FAILED (exit code $code)." -ForegroundColor Red
        }
        else {
            Write-Host '  Installed. Reload any open VS Code window to activate it.' -ForegroundColor Green
        }
    }
    return ,$failures
}

# ── SSMS / VS 2026: the gated close -> VSIXInstaller -> /setup steps ───────
# Needs elevation. Returns failures; throws if the apps can't be closed or
# every install failed.
function Install-VsixTargets {
    param([object[]]$Targets, [string]$ScriptDir)

    # ── Resolve one .vsix per product family that is present ───────────────
    $vsixByAsset = @{}
    foreach ($assetName in ($Targets | Select-Object -ExpandProperty AssetName -Unique)) {
        $explicit = if ($assetName -eq 'SQuiL-SSMS.vsix') { $SsmsVsixPath } else { $VsVsixPath }
        $vsixByAsset[$assetName] = Resolve-SquilVsix -Explicit $explicit -AssetName $assetName -ScriptDir $ScriptDir
    }

    # ── Prompt: let the user save open work before apps are force-closed ───
    # To cancel before anything destructive happens, just close the window —
    # nothing has been changed at this point.
    $appNames = ($Targets | Select-Object -ExpandProperty Name -Unique) -join ', '
    Write-Host ''
    Write-Host "Step 1 will force-close: $appNames" -ForegroundColor Yellow
    Write-Host 'Save any open work in those applications now.' -ForegroundColor Yellow
    Wait-AnyKey 'Press any key to continue, or close this window to cancel.'

    # ── Step 1: force-close every affected product ──────────────────────────
    Write-Host '== Step 1: Closing applications ==' -ForegroundColor Cyan
    foreach ($proc in ($Targets | Select-Object -ExpandProperty Process -Unique)) {
        $name = ($Targets | Where-Object Process -eq $proc | Select-Object -First 1).Name
        Stop-ProductProcesses -ProcessName $proc -DisplayName $name
    }
    Write-Host 'Step 1 complete (applications closed).' -ForegroundColor Green

    # ── Step 2: install each VSIX into its own instance ────────────────────
    # /instanceIds scopes the install: VSIXInstaller delegates to the shared
    # VS installer engine, which otherwise offers the package to every
    # compatible instance on the machine.
    $failures = @()
    Write-Host '== Step 2: VSIXInstaller /quiet (installing extension) ==' -ForegroundColor Cyan
    foreach ($t in $Targets) {
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
    if ($failures.Count -eq $Targets.Count) {
        throw "Step 2 failed for every product:`n  $($failures -join "`n  ")"
    }

    # ── Step 3: SSMS only — force pkgdef merge, only where install worked ──
    # Without /setup SSMS won't see the .squil binding even though
    # VSIXInstaller reported success. VS needs no equivalent step.
    $ssmsTargets = @($Targets | Where-Object { $_.Kind -eq 'SSMS' -and ($failures -notmatch [regex]::Escape($_.Name)) })
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
    return ,$failures
}

$phase = if ($env:SQUIL_PHASE) { $env:SQUIL_PHASE } else { 'all' }
$exitCode = 0
try {
    # The batch header exported the .cmd's own folder; use it to find a
    # sibling .vsix (the old install.ps1 used $PSScriptRoot, which is empty
    # when this body runs in-memory via iex).
    $scriptDir = if ($env:SQUIL_CMD_DIR) { $env:SQUIL_CMD_DIR } else { $PSScriptRoot }

    # Defensive admin check. Only the phases that run VSIXInstaller need it;
    # this fires if someone runs this PowerShell body directly without elevation.
    if ($phase -ne 'user') {
        $principal = New-Object Security.Principal.WindowsPrincipal(
            [Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw "Not running elevated. Run SQuiL-Install.cmd (it self-elevates via UAC) rather than invoking this script body directly."
        }
    }

    # ── Discover what's installed ──────────────────────────────────────────
    $vsCodeTargets = @()
    if ($phase -ne 'admin') {
        Write-Host '== Looking for supported products (VS Code, SSMS 22+, Visual Studio 2026) ==' -ForegroundColor Cyan
        $vsCodeTargets = Get-VsCodeTargets
        foreach ($t in $vsCodeTargets) {
            Write-Host "  Found: $($t.Name)  [$($t.Cli)]" -ForegroundColor Green
        }
    }
    else {
        Write-Host '== Looking for supported products (SSMS 22+, Visual Studio 2026) ==' -ForegroundColor Cyan
    }
    $vsixTargets = Get-SquilTargets
    foreach ($t in $vsixTargets) {
        Write-Host "  Found: $($t.Name)  [$($t.InstanceId)]" -ForegroundColor Green
    }
    if ($vsCodeTargets.Count -eq 0 -and $vsixTargets.Count -eq 0) {
        throw "No supported product found. SQuiL's editor extensions need Visual Studio Code (or Insiders), SSMS 22 (or later), and/or Visual Studio 2026 (18+) Community/Professional/Enterprise."
    }

    $failures = @()

    # ── VS Code (phases user + all) ────────────────────────────────────────
    if ($vsCodeTargets.Count -gt 0) {
        if ($phase -eq 'all' -and -not (Test-IsDesktopUser)) {
            # Elevated as a different (or unconfirmable) account: installing
            # here would land in that account's profile, not the desktop user's.
            Write-Host ''
            Write-Host 'Skipping VS Code: this window runs as a different account than the one signed in,' -ForegroundColor Yellow
            Write-Host 'so the extension would land in the wrong profile. To install it for you, run' -ForegroundColor Yellow
            Write-Host 'SQuiL-Install.cmd again by double-clicking it (not "Run as administrator").' -ForegroundColor Yellow
        }
        else {
            $failures += Install-VsCodeTargets -Targets $vsCodeTargets -ScriptDir $scriptDir
        }
    }

    # ── SSMS / VS 2026 ─────────────────────────────────────────────────────
    if ($vsixTargets.Count -gt 0) {
        if ($phase -eq 'user') {
            # Relaunch THIS file elevated for the VSIXInstaller steps and wait,
            # so this window can report the combined result.
            Write-Host ''
            Write-Host 'SSMS / Visual Studio need administrator rights - approve the UAC prompt.' -ForegroundColor Yellow
            try {
                $elevated = Start-Process -FilePath $env:SQUIL_CMD_PATH -ArgumentList '--elevated' `
                    -Verb RunAs -Wait -PassThru
                if ($elevated.ExitCode -ne 0) {
                    $failures += "SSMS / Visual Studio: the administrator window reported errors (exit code $($elevated.ExitCode))."
                }
                else {
                    Write-Host 'SSMS / Visual Studio: installed (administrator window finished cleanly).' -ForegroundColor Green
                }
            }
            catch {
                # Most often: the UAC prompt was declined.
                $failures += "SSMS / Visual Studio: not installed - elevation failed or was declined ($($_.Exception.Message))."
            }
        }
        else {
            $failures += Install-VsixTargets -Targets $vsixTargets -ScriptDir $scriptDir
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
    # Each console is its own window; pause so the user can read the result
    # before it closes. The --elevated window returns to the first one, which
    # reports the outcome, so it pauses only when it has an error to show.
    if ($phase -ne 'admin' -or $exitCode -ne 0) {
        Write-Host ''
        Wait-AnyKey 'Press any key to close...'
    }
}

exit $exitCode
