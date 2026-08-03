<#
.SYNOPSIS
  Verifies the sealed Microsoft Store SKU: builds the STORE flavor and asserts the stripped surfaces
  are ABSENT from the produced binaries — not merely disabled. Run before any Store submission; wired
  into release-msstore.yml so the seal can't silently regress.

.DESCRIPTION
  STORE = the sealed core SKU (Configuration=Store leaves FULL undefined). Two things must be gone:
    - the off-Store plugin loader (PluginHost / PluginFeedSource / WirePluginFeed) — App-side #if FULL
    - the EAC-disable mechanism (AntiCheat.cs: the .626off bootstrapper swap + AntiCheatState) — Core #if FULL

  The check reads each produced dll as raw bytes and searches for each forbidden term in BOTH its UTF-8
  form (metadata type/member names) and its UTF-16LE form (string literals), via a Latin1 1:1 byte->char
  mapping so a native String.Contains does a fast exact byte-subsequence match.

  Exit 0 = sealed. Exit 1 = a forbidden symbol leaked into the Store build (or the build failed).
#>
param(
    [string]$Configuration = "Store",
    [string]$Platform = "x64",
    # CI builds the versioned bundle first, then runs the seal with -SkipBuild to scan that exact output
    # (no redundant rebuild at a different version). Local runs omit it and build fresh.
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

if (-not $SkipBuild) {
    Write-Host "Building STORE flavor ($Configuration/$Platform)..."
    dotnet build "$repo/src/ModManager.App/ModManager.App.csproj" -c $Configuration -p:Platform=$Platform --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "STORE build failed."; exit 1 }
}

$appDll = Get-ChildItem -Path "$repo/src/ModManager.App/bin/$Platform/$Configuration" -Recurse -Filter "ModManager.App.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $appDll) { Write-Error "Couldn't find the STORE build output (ModManager.App.dll)."; exit 1 }
$outDir = $appDll.DirectoryName

function Count-Symbol([string]$hay, [string]$term, [System.Text.Encoding]$latin1) {
    $n = 0
    foreach ($enc in @([System.Text.Encoding]::UTF8, [System.Text.Encoding]::Unicode)) {
        $needle = $latin1.GetString($enc.GetBytes($term))
        $n += ([regex]::Matches($hay, [regex]::Escape($needle))).Count
    }
    return $n
}

# A term leaks if it appears MORE often than the legitimate names that merely contain it as a substring.
#
# Why the subtraction exists: "PluginHost" is a substring of the CONTRACT interface IPluginHostServices,
# which the Store build legitimately ships — the host services that hand a mod source its HttpClient and
# perform authorized sends are shared by both SKUs. A plain substring match flagged that interface as if
# the loader itself had leaked. Subtracting the known-benign container keeps the original intent (the
# loader TYPE must not ship) without the false positive.
#
# This deliberately does NOT weaken the check: the loader is independently pinned by markers that cannot
# appear without it — its own private method (LoadVerified), the feed that downloads plugins
# (PluginFeedSource), the hot-load hook (WirePluginFeed), and AssemblyLoadContext, which IS the capability
# that makes runtime code loading possible at all. Any one of them appearing fails the seal.
function Test-SymbolPresent([string]$dllPath, [string]$term, [string[]]$benignContainers) {
    # ISO-8859-1 (28591) is a 1:1 byte<->char mapping available on both Windows PowerShell 5.1 and
    # pwsh 7 (unlike ::Latin1, which is .NET 5+ only), so matching becomes a fast exact byte search.
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $hay = $latin1.GetString([System.IO.File]::ReadAllBytes($dllPath))

    $hits = Count-Symbol $hay $term $latin1
    foreach ($b in $benignContainers) { $hits -= (Count-Symbol $hay $b $latin1) }
    return ($hits -gt 0)
}

$checks = @(
    @{ Dll = "ModManager.App.dll"
       Terms = @("PluginFeedSource", "PluginHost", "WirePluginFeed", "LoadVerified", "AssemblyLoadContext")
       Benign = @{ "PluginHost" = @("IPluginHostServices") } },
    @{ Dll = "ModManager.Core.dll"; Terms = @(".626off", "AntiCheatState") }
)

$leaks = @()
foreach ($c in $checks) {
    $dll = Join-Path $outDir $c.Dll
    if (-not (Test-Path $dll)) { Write-Error "Missing $($c.Dll) in $outDir"; exit 1 }
    foreach ($t in $c.Terms) {
        $benign = @()
        if ($c.ContainsKey("Benign") -and $c.Benign.ContainsKey($t)) { $benign = $c.Benign[$t] }
        if (Test-SymbolPresent $dll $t $benign) { $leaks += "$($c.Dll): '$t'" }
    }
}

if ($leaks.Count -gt 0) {
    Write-Host "STORE SEAL FAILED - forbidden symbols leaked into the Store build:" -ForegroundColor Red
    $leaks | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "STORE seal OK - plugin loader + EAC-disable mechanism are absent from the Store binaries." -ForegroundColor Green
Write-Host "  scanned: $outDir" -ForegroundColor DarkGray
exit 0
