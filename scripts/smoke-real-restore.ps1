<#
.SYNOPSIS
    Restore into REAL game folders (profile archive, steps 3-4) - the run the automated smoke
    deliberately would not do.

.DESCRIPTION
    Every other restore test points at throwaway registrations in temp folders, because a restore
    writes into save and mod folders and proving a checkbox works is not worth putting junk in
    somebody's saves. That leaves the highest-consequence path in the feature never having touched
    a real game.

    This closes it, on the one design that is safe on real folders: back up NOW, restore THAT
    backup, and the machine must come back byte-identical - the bytes being written are the bytes
    already there. A single file is perturbed first, so a restore that quietly no-ops cannot pass.

    Guarded by scratchpad/guard.py, which keeps an independent copy and a sha256 manifest of every
    folder these tests can reach. Independent on purpose: a bug in the thing under test must not
    also be the thing that undoes it.

      -Stage BackUp                back everything up
      -Stage Restore -Only <id.part>   tick exactly one game+part, arm, confirm
#>
param(
    [Parameter(Mandatory)][ValidateSet("BackUp", "Restore")][string]$Stage,
    [Parameter(Mandatory)][string]$ArchivePath,
    [string]$Only
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\uia-lib.ps1"

$pass = 0; $fail = 0
function Check([string]$what, [bool]$ok, [string]$detail = "") {
    if ($ok) { $script:pass++; Write-Host "  PASS  $what" -f Green }
    else     { $script:fail++; Write-Host "  FAIL  $what $detail" -f Red }
}

$root = Get-AppRoot
if (-not $root) { throw "The launcher is not running." }
[void](Wait-Ready $root)
function T { Get-Tree $root }

if ($Stage -eq "BackUp") {
    Invoke-Node (Find-ById (T) "SettingsButton")
    Start-Sleep -Milliseconds 1500
    Check "Settings opened" ([bool](Find-ById (T) "ArchiveCreateButton"))

    if (Test-Path $ArchivePath) { Remove-Item $ArchivePath -Force }
    Invoke-Node (Find-ById (T) "ArchiveCreateButton")
    [void](Submit-FileDialog (Get-FileDialog) @($ArchivePath))

    $deadline = [datetime]::UtcNow.AddMinutes(15); $last = -1; $stable = 0
    while ([datetime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 3
        $len = if (Test-Path $ArchivePath) { (Get-Item $ArchivePath).Length } else { 0 }
        if ($len -gt 0 -and $len -eq $last) { $stable++ } else { $stable = 0 }
        if ($stable -ge 3) { break }
        $last = $len
    }
    Check "the backup was written" ((Test-Path $ArchivePath) -and (Get-Item $ArchivePath).Length -gt 0)
}
else {
    if (-not $Only) { throw "-Only <gameId>.<part> is required for -Stage Restore" }

    # Re-open the report each time, so each part is a fresh decision rather than a leftover
    # selection from the press before.
    Invoke-Node (Find-ById (T) "SettingsButton")
    Start-Sleep -Milliseconds 1500
    Invoke-Node (Find-ById (T) "ArchiveInspectButton")
    [void](Submit-FileDialog (Get-FileDialog) @($ArchivePath))
    Start-Sleep -Seconds 5
    [void](Wait-Ready $root)
    Write-Host "  headline: $(Get-Text (Find-ById (T) 'ArchiveReportHeadline'))" -f DarkGray

    $boxes = Find-AllByIdPrefix (T) "ArchiveRestorePart."
    Check "the report offers parts" ($boxes.Count -gt 0) "found $($boxes.Count)"

    $wantId = "ArchiveRestorePart.$Only"
    $found = $false
    foreach ($b in $boxes) {
        $want = ($b.Current.AutomationId -eq $wantId)
        if ($want) { $found = $true }
        if ((Get-ToggleState $b) -ne $want) { Set-Toggle $b $want }
    }
    Check "the one part asked for is offered ($Only)" $found

    Invoke-Node (Find-ById (T) "ArchiveRestoreButton")
    Start-Sleep -Milliseconds 900
    $armed = Get-Text (Find-ById (T) "ArchiveRestoreButton")
    Check "one press arms rather than acting" ($armed -match "Confirm") "button says '$armed'"

    Invoke-Node (Find-ById (T) "ArchiveRestoreButton")
    Start-Sleep -Seconds 8
    [void](Wait-Ready $root)
    $status = Get-Text (Find-ById (T) "ArchiveRestoreStatus")
    Write-Host "  status: $status" -f Cyan
    Check "it restored rather than skipping" ($status -match "Restored 1 game")
    Check "it wrote something (a silent no-op would also leave things identical)" `
          ($status -notmatch "Restored 1 game — 0 file")
}

Write-Host ""
Write-Host "$pass passed, $fail failed" -f $(if ($fail) { "Red" } else { "Green" })
exit $(if ($fail) { 1 } else { 0 })
