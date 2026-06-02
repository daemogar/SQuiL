<#
.SYNOPSIS
    Installs the SQuiL SSMS extension. Downloads the .vsix from GitHub if it
    isn't already present, so only THIS script needs to be downloaded and run.

.DESCRIPTION
    Must be run from an elevated (Administrator) PowerShell session.

    VSIX resolution order:
      1. -VsixPath <file>                  (explicit local .vsix), else
      2. SQuiL.SsmsExtension.vsix next to this script, else
      3. download the baked-in release from GitHub.

    The release version is baked into this script at publish time (see
    $BakedReleaseTag below), so no version ever needs to be supplied. When the
    script is run from a local build — the placeholder is still present — it
    falls back to the latest GitHub release.

    Then:
      Step 1: Force-close SSMS (after prompting you to save your work).
      Step 2: VSIXInstaller /quiet <vsix>  — install the extension package.
      Step 3: Ssms.exe /setup              — force SSMS to merge the new pkgdef
                                             into its private registry.

    Step 3 does NOT run until step 2 has fully completed and reported success.
    The VSIX install is launched with -Wait and its exit code is checked; a
    non-zero exit aborts the script before /setup, so a failed install never
    falls through to the pkgdef merge.

.PARAMETER VsixPath
    Explicit path to a local .vsix to install. Skips the download.

.PARAMETER Repo
    GitHub <owner>/<repo> to download releases from. Defaults to daemogar/SQuiL.

.EXAMPLE
    .\install.ps1
#>
[CmdletBinding()]
param(
    [string]$VsixPath,
    [string]$Repo = 'daemogar/SQuiL'
)

$ErrorActionPreference = 'Stop'

# Release tag baked in at publish time by .github/workflows/publish.yml, which
# replaces the placeholder below with the release version. This lets a
# downloaded copy of just this script install the exact matching .vsix with no
# version argument. When the placeholder is still present (e.g. a local build),
# $releaseTag is left null and we fall back to the latest GitHub release.
$BakedReleaseTag = '__SQUIL_RELEASE_TAG__'
$releaseTag = if ($BakedReleaseTag -match '^__.*__$') { $null } else { $BakedReleaseTag }

# ── Require an elevated session (run as Administrator) ────────────────────
$principal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must be run as Administrator. Re-open PowerShell with 'Run as administrator' and run .\install.ps1 again."
}

# SSMS tool paths (as documented in INSTALL.md).
$installer = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe"
$ssms      = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe"
if (-not (Test-Path $installer)) { throw "VSIXInstaller not found: $installer" }
if (-not (Test-Path $ssms))      { throw "Ssms.exe not found: $ssms" }

# ── Resolve the .vsix: explicit path > local-next-to-script > download ─────
function Get-SquilVsix {
    param([string]$Repo, [string]$Tag)

    # Windows PowerShell 5.1 may default to TLS 1.0; GitHub requires 1.2+.
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    $apiHeaders = @{ 'User-Agent' = 'SQuiL-install.ps1'; 'Accept' = 'application/vnd.github+json' }

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
        Invoke-WebRequest -Headers @{ 'User-Agent' = 'SQuiL-install.ps1' } `
            -Uri $asset.browser_download_url -OutFile $dest
    }
    finally { $ProgressPreference = $prev }

    Unblock-File $dest   # strip mark-of-the-web so VSIXInstaller won't balk
    return $dest
}

$localVsix = Join-Path $PSScriptRoot 'SQuiL.SsmsExtension.vsix'
if ($VsixPath) {
    if (-not (Test-Path $VsixPath)) { throw "VSIX not found: $VsixPath" }
    $vsix = (Resolve-Path $VsixPath).Path
    Write-Host "Using VSIX: $vsix" -ForegroundColor Cyan
}
elseif (Test-Path $localVsix) {
    $vsix = $localVsix
    Write-Host "Using VSIX next to this script: $vsix" -ForegroundColor Cyan
}
else {
    Write-Host 'No local .vsix found — fetching it from GitHub releases.' -ForegroundColor Cyan
    $vsix = Get-SquilVsix -Repo $Repo -Tag $releaseTag
    Write-Host "Downloaded VSIX: $vsix" -ForegroundColor Green
}

# ── Prompt: let the user save open work before SSMS is force-closed ───────
Write-Host 'Step 1 will force-close SSMS to install the SQuiL extension.' -ForegroundColor Yellow
Read-Host 'Save any open work in SSMS now, then press Enter to continue (Ctrl+C to cancel)' | Out-Null

# ── Step 1: force-close SSMS (and WAIT until every instance has exited) ───
Write-Host '== Step 1: Closing SSMS ==' -ForegroundColor Cyan
$ssmsProcs = Get-Process ssms -ErrorAction SilentlyContinue
if ($ssmsProcs) {
    $ssmsProcs | Stop-Process -Force
    # Stop-Process only signals termination; block until the processes are
    # actually gone so step 2's install never races a still-locked SSMS.
    $ssmsProcs | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
}
if (Get-Process ssms -ErrorAction SilentlyContinue) {
    throw "Step 1 failed: SSMS is still running after 30s. Close it manually, then re-run. (Step 2 not started.)"
}
Write-Host 'Step 1 complete (SSMS closed).' -ForegroundColor Green

# ── Step 2: install the VSIX (wait for full completion) ───────────────────
Write-Host '== Step 2: VSIXInstaller /quiet (installing extension) ==' -ForegroundColor Cyan
$step2 = Start-Process -FilePath $installer -ArgumentList '/quiet', "`"$vsix`"" -Wait -PassThru
if ($step2.ExitCode -ne 0) {
    throw "Step 2 (VSIX install) failed with exit code $($step2.ExitCode). Aborting before step 3 (/setup)."
}
Write-Host 'Step 2 complete (exit code 0).' -ForegroundColor Green

# ── Step 3: force pkgdef merge — only after step 2 succeeded ──────────────
Write-Host '== Step 3: Ssms.exe /setup (merging pkgdef) ==' -ForegroundColor Cyan
$step3 = Start-Process -FilePath $ssms -ArgumentList '/setup' -Wait -PassThru
if ($step3.ExitCode -ne 0) {
    throw "Step 3 (Ssms.exe /setup) failed with exit code $($step3.ExitCode)."
}
Write-Host 'Step 3 complete (exit code 0).' -ForegroundColor Green

Write-Host 'Install finished. Launch SSMS and open a .squil file to verify.' -ForegroundColor Green
