# 626 Mod Launcher -- build a Velopack Setup.exe + delta package for GitHub Releases.
#
# Produces a self-contained, no-cert, no-runtime-install installer that anyone can download
# from the Releases page. SmartScreen will warn ("More info -> Run anyway") on first install --
# that's the v1 trade for shipping without a code-sign cert.
#
# Output (under dist/release/):
#   - 626ModLauncher-win-Setup.exe        installer users download
#   - 626ModLauncher-<v>-win-full.nupkg   full package (auto-updater consumes this)
#   - 626ModLauncher-<v>-win-delta.nupkg  delta vs prior release (only after release #2)
#   - releases.win.json                   manifest the Velopack auto-updater pings
#
# Run from repo root:
#   pwsh scripts/build-velopack-release.ps1 -Version 0.2.0
#
# Requires:
#   - .NET 10 SDK (`dotnet --list-sdks` shows 10.x)
#   - vpk         (install: `dotnet tool install -g vpk`)
#
# Upload flow (CI does this; if running locally for a smoke build, see RELEASE.md):
#   1. Tag: `git tag v<version> && git push origin v<version>`
#   2. CI runs this script + uploads every file from dist/release/ as Release assets.
#   3. The auto-updater pings releases.win.json + downloads the *-full.nupkg or *-delta.nupkg.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,

    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$ReleaseNotes,
    [switch]$SkipPublish,
    # CI: leave dist/release/ contents in place (prior nupkgs from `vpk download github`)
    # so vpk pack can compute a delta against the previous release.
    [switch]$NoClean
)

$ErrorActionPreference = 'Stop'

# Velopack's --packVersion requires 3-part SemVer2. The assembly + tag use the .NET-style
# 4-part version (0.2.0.0); strip the 4th segment for vpk only.
$packVersion = ($Version -split '\.')[0..2] -join '.'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectFile = Join-Path $repoRoot 'src\ModManager.App\ModManager.App.csproj'
$publishDir = Join-Path $repoRoot 'dist\publish'
$releaseDir = Join-Path $repoRoot 'dist\release'
$iconPath = Join-Path $repoRoot 'src\ModManager.App\Assets\icon.ico'

Write-Host "[velopack] 626 Mod Launcher v$Version ($Runtime, $Configuration) -- vpk packVersion=$packVersion" -ForegroundColor Cyan

# ----- 1. Pre-flight ----------------------------------------------------------

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk not found on PATH. Install with: dotnet tool install -g vpk"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not found on PATH."
}
if (-not (Test-Path $projectFile)) {
    throw "Project file not found: $projectFile"
}
if (-not (Test-Path $iconPath)) {
    throw "Icon not found: $iconPath. Velopack needs an .ico for the installer's branding."
}

# ----- 2. dotnet publish ------------------------------------------------------
# Self-contained so users don't need to install .NET 10 runtime first.
# Single-file off so Velopack can hash + delta-patch individual files (it walks the publish dir).

if (-not $SkipPublish) {
    if (Test-Path $publishDir) {
        Remove-Item -Recurse -Force $publishDir
    }
    Write-Host "[publish] dotnet publish ($Runtime, self-contained)..." -ForegroundColor Cyan
    & dotnet publish $projectFile `
        -c $Configuration `
        -r $Runtime `
        -p:Platform=x64 `
        --self-contained true `
        -o $publishDir `
        /p:Version=$Version `
        /p:PublishSingleFile=false `
        /p:DebugType=embedded
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit $LASTEXITCODE)"
    }
}

if (-not (Test-Path (Join-Path $publishDir 'ModManager.App.exe'))) {
    throw "Publish output missing ModManager.App.exe at $publishDir. Re-run without -SkipPublish."
}

# ----- 3. vpk pack ------------------------------------------------------------

if (-not $NoClean -and (Test-Path $releaseDir)) {
    Remove-Item -Recurse -Force $releaseDir
}
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

# The packId becomes part of the installer filename + the auto-update channel. Keep it
# stable across releases - changing it would orphan existing installs from updates.
$vpkArgs = @(
    'pack'
    '--packId',      '626ModLauncher'
    '--packVersion', $packVersion
    '--packDir',     $publishDir
    '--mainExe',     'ModManager.App.exe'
    '--packTitle',   '626 Mod Launcher'
    '--packAuthors', '626 Labs'
    '--icon',        $iconPath
    '--outputDir',   $releaseDir
    '--delta',       'BestSpeed'
)
if ($ReleaseNotes -and (Test-Path $ReleaseNotes)) {
    $vpkArgs += @('--releaseNotes', (Resolve-Path $ReleaseNotes).Path)
}

Write-Host "[vpk] vpk $($vpkArgs -join ' ')" -ForegroundColor Cyan
& vpk @vpkArgs
if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed (exit $LASTEXITCODE)"
}

# ----- 4. Summary -------------------------------------------------------------

$artifacts = Get-ChildItem -Path $releaseDir -File | Sort-Object Name
Write-Host ""
Write-Host "Built v$Version -> $releaseDir" -ForegroundColor Green
foreach ($a in $artifacts) {
    $size = [math]::Round($a.Length / 1MB, 1)
    Write-Host ("  {0,-50} {1,8} MB" -f $a.Name, $size)
}
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. git tag v$Version && git push origin v$Version    (CI does the rest)"
Write-Host "  2. Or, manually: github.com -> Releases -> Draft from the tag, upload every file in dist/release/."
