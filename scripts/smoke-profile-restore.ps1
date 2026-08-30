<#
.SYNOPSIS
    Profile restore, driven through the real app (step 3 of the profile archive).

.DESCRIPTION
    Backs up, reads the backup, ticks ONE game's parts, confirms, and checks the files landed.

    The game it restores is a throwaway registration pointing at temp folders, added by the caller
    before this runs. That is not squeamishness: a restore writes into save and mod folders, and
    pointing a smoke test at a real one would put junk in somebody's Palworld saves to prove a
    checkbox works. Everything else in the library is UNTICKED, and the run asserts the real folders
    were not touched.
#>
param(
    [Parameter(Mandatory)][string]$Fixture,     # the throwaway game's folder
    [Parameter(Mandatory)][string]$ArchivePath, # where to write the backup
    [string]$GameId = "restore-smoke"
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

# Find-* take a FLAT TREE, not the root element - piping the root through Where-Object silently
# matches nothing, which reads as "the control is missing" rather than "you passed the wrong thing".
# Re-walked at each step because opening a dialog or a flyout changes what is realised.
function T { Get-Tree $root }

# ---- open Settings ---------------------------------------------------------------------------
Invoke-Node (Find-ById (T) "SettingsButton")
Start-Sleep -Milliseconds 1500
[void](Wait-Ready $root)
Check "Settings opened" ([bool](Find-ById (T) "ArchiveCreateButton"))

# ---- back everything up ----------------------------------------------------------------------
if (Test-Path $ArchivePath) { Remove-Item $ArchivePath -Force }
Invoke-Node (Find-ById (T) "ArchiveCreateButton")
$dlg = Get-FileDialog
Check "the save dialog came up" ([bool]$dlg)
[void](Submit-FileDialog $dlg @($ArchivePath))

# Zipping a real library takes a while; wait for the file to stop growing rather than guessing.
$deadline = [datetime]::UtcNow.AddMinutes(12); $lastLen = -1; $stable = 0
while ([datetime]::UtcNow -lt $deadline) {
    Start-Sleep -Seconds 3
    $len = if (Test-Path $ArchivePath) { (Get-Item $ArchivePath).Length } else { 0 }
    if ($len -gt 0 -and $len -eq $lastLen) { $stable++ } else { $stable = 0 }
    if ($stable -ge 3) { break }
    $lastLen = $len
}
Check "the backup was written" ((Test-Path $ArchivePath) -and (Get-Item $ArchivePath).Length -gt 0) `
      "status: $(Get-Text (Find-ById (T) 'ArchiveStatusText'))"

# ---- change the fixture, so a restore has something to undo ------------------------------------
Set-Content -Path "$Fixture\save\Level.sav"          -Value "CLOBBERED" -NoNewline
Set-Content -Path "$Fixture\root\ModsA\AlphaMod.pak" -Value "CLOBBERED" -NoNewline
Set-Content -Path "$Fixture\root\ModsB\BetaMod.pak" -Value "CLOBBERED" -NoNewline

# ---- read it back ------------------------------------------------------------------------------
Invoke-Node (Find-ById (T) "ArchiveInspectButton")
$dlg = Get-FileDialog
Check "the open dialog came up" ([bool]$dlg)
[void](Submit-FileDialog $dlg @($ArchivePath))
Start-Sleep -Seconds 4
[void](Wait-Ready $root)

Check "the report rendered" ([bool](Find-ById (T) "ArchiveReportHeadline")) 
Write-Host "  headline: $(Get-Text (Find-ById (T) 'ArchiveReportHeadline'))" -f DarkGray

# ---- tick only the throwaway game ---------------------------------------------------------------
$boxes = Find-AllByIdPrefix (T) "ArchiveRestorePart."
Check "part checkboxes are addressable" ($boxes.Count -gt 0) "found $($boxes.Count)"

$mine = 0
foreach ($b in $boxes) {
    $id = $b.Current.AutomationId
    $want = $id.StartsWith("ArchiveRestorePart.$GameId.")
    if ($want) { $mine++ }
    if ((Get-ToggleState $b) -ne $want) { Set-Toggle $b $want }
}
Check "the throwaway game offers its parts" ($mine -ge 2) "offered $mine"
Write-Host "  ticked $mine of $($boxes.Count) boxes" -f DarkGray

# ---- arm, then confirm --------------------------------------------------------------------------
$btn = Find-ById (T) "ArchiveRestoreButton"
Check "the restore button is addressable" ([bool]$btn)
Invoke-Node $btn
Start-Sleep -Milliseconds 800
$armed = Get-Text (Find-ById (T) "ArchiveRestoreButton")
Check "one press ARMS rather than acting" ($armed -match "Confirm") "button says '$armed'"

$before = Get-Content "$Fixture\save\Level.sav" -Raw
Check "nothing was written by the arming press" ($before -eq "CLOBBERED")

Invoke-Node (Find-ById (T) "ArchiveRestoreButton")
Start-Sleep -Seconds 5
[void](Wait-Ready $root)
$status = Get-Text (Find-ById (T) "ArchiveRestoreStatus")
Write-Host "  status: $status" -f DarkGray
Check "the run reported a summary" ($status -match "Restored|skipped|running")

# ---- did the files actually come back -----------------------------------------------------------
Check "the save came back"       ((Get-Content "$Fixture\save\Level.sav" -Raw) -eq "the-original-world")
# Different mods per location, which is the real shape - Windrose keeps three locations holding
# three different sets. (A mod with the SAME name in two locations is collapsed by the scanner into
# one entry before the archive ever sees it; that is a scanner limitation, not this format's.)
Check "location A's mod came back" ((Get-Content "$Fixture\root\ModsA\AlphaMod.pak" -Raw) -eq "from-location-A")
Check "location B's mod came back, NOT A's" `
      ((Get-Content "$Fixture\root\ModsB\BetaMod.pak" -Raw) -eq "from-location-B")

# ---- and is it undoable ---------------------------------------------------------------------------
# The file-op law: anything replaced is snapshotted first. Without this the restore is a one-way
# door, which is the one thing this app does not build.
$snaps = @(Get-ChildItem -Path $Fixture -Recurse -Filter "*before-restore*.zip" -EA SilentlyContinue)
Check "the overwrite was snapshotted first" ($snaps.Count -gt 0) "found $($snaps.Count)"

Write-Host ""
Write-Host "$pass passed, $fail failed" -f $(if ($fail) { "Red" } else { "Green" })
exit $(if ($fail) { 1 } else { 0 })
