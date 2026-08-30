<#
.SYNOPSIS
    Holding a game's contents until the game comes back (profile archive, step 4).

.DESCRIPTION
    Run in stages, because the middle of it is a game disappearing from the library and coming back
    somewhere else - which needs the app restarted, and the registry edited between runs.

      -Stage BackUp    back everything up, with the throwaway game registered
      -Stage Hold      the game is GONE from the library; hold its contents out of the backup
      -Stage PutBack   the game is back AT A DIFFERENT PATH; the chip offers it, take the offer

    The point of the third stage is the one that cannot be faked: nothing about where the game lives
    was recorded when it was held, so it must land wherever the game is NOW.
#>
param(
    [Parameter(Mandatory)][ValidateSet("BackUp", "Hold", "PutBack")][string]$Stage,
    [Parameter(Mandatory)][string]$ArchivePath,
    [string]$Fixture,
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
function T { Get-Tree $root }

switch ($Stage) {

# ---------------------------------------------------------------------------------------------
"BackUp" {
    Invoke-Node (Find-ById (T) "SettingsButton")
    Start-Sleep -Milliseconds 1500
    Check "Settings opened" ([bool](Find-ById (T) "ArchiveCreateButton"))

    if (Test-Path $ArchivePath) { Remove-Item $ArchivePath -Force }
    Invoke-Node (Find-ById (T) "ArchiveCreateButton")
    $dlg = Get-FileDialog
    [void](Submit-FileDialog $dlg @($ArchivePath))

    $deadline = [datetime]::UtcNow.AddMinutes(12); $lastLen = -1; $stable = 0
    while ([datetime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 3
        $len = if (Test-Path $ArchivePath) { (Get-Item $ArchivePath).Length } else { 0 }
        if ($len -gt 0 -and $len -eq $lastLen) { $stable++ } else { $stable = 0 }
        if ($stable -ge 3) { break }
        $lastLen = $len
    }
    Check "the backup was written" ((Test-Path $ArchivePath) -and (Get-Item $ArchivePath).Length -gt 0)
}

# ---------------------------------------------------------------------------------------------
"Hold" {
    Invoke-Node (Find-ById (T) "SettingsButton")
    Start-Sleep -Milliseconds 1500
    Invoke-Node (Find-ById (T) "ArchiveInspectButton")
    $dlg = Get-FileDialog
    [void](Submit-FileDialog $dlg @($ArchivePath))
    Start-Sleep -Seconds 4
    [void](Wait-Ready $root)

    Write-Host "  headline: $(Get-Text (Find-ById (T) 'ArchiveReportHeadline'))" -f DarkGray

    # The game is no longer in the library, so it must appear under "waiting", offering to be HELD
    # rather than restored - there is nowhere to restore it to.
    Check "the missing game is listed as waiting" ([bool](Find-ById (T) "ArchiveGamesNotHere"))
    $box = Find-ById (T) "ArchiveHold.$GameId"
    Check "it offers to be held, not restored" ([bool]$box)
    Check "and offers no restore parts" (-not (Find-ById (T) "ArchiveRestorePart.$GameId.saves"))

    Set-Toggle $box $true
    Invoke-Node (Find-ById (T) "ArchiveHoldButton")
    Start-Sleep -Seconds 6
    $status = Get-Text (Find-ById (T) "ArchiveHoldStatus")
    Write-Host "  status: $status" -f DarkGray
    Check "it says what it kept" ($status -match "Kept 1 game")

    $heldDir = Join-Path $env:LOCALAPPDATA "ModManagerBuilder\held"
    Check "a one-game backup is on disk" (Test-Path (Join-Path $heldDir "$GameId.626profile"))
}

# ---------------------------------------------------------------------------------------------
"PutBack" {
    # The game is registered again, at a path that did not exist when the backup was made.
    #
    # Navigate ONLY if we are not already on it. The library list is virtualized and is not realised
    # at all while a game is open, so "the game is missing from the library" is what a walk reports
    # for a game that is already on screen - the absence trap .claude/rules/automation-ids.md names.
    # A first cut of this failed here and read exactly like the registration had not taken.
    $chip = Find-ById (T) "StateChip.backup-waiting"
    if (-not $chip) {
        $card = Find-ById (T) "RecentCard.$GameId"
        if (-not $card) { $card = Find-ByName (T) "*Restore Smoke*" -Like }
        Check "the game is reachable in the library" ([bool]$card)
        Invoke-Node $card
        Start-Sleep -Seconds 3
        [void](Wait-Ready $root)
        $chip = Find-ById (T) "StateChip.backup-waiting"
    }
    Check "the BACKUP chip is on the game's strip" ([bool]$chip) "(ids: $(((T) | ForEach-Object { $_.Current.AutomationId } | Where-Object { $_ -like 'StateChip*' }) -join ', '))"

    Invoke-Node $chip                       # expand it, so the sentence and its action are reachable
    Start-Sleep -Milliseconds 900
    $detail = Get-Text (Find-ById (T) "StateChipDetail")
    Write-Host "  chip says: $detail" -f DarkGray
    Check "it says what is waiting, in counts" ($detail -match "file")

    # The action button is a SINGLETON on the detail row, not one per condition: only one chip is
    # expanded at a time. (.claude/rules/automation-ids.md still describes it as
    # StateChipAction.<condition>, which is what sent this looking for an id that never existed.)
    Invoke-Node (Find-ById (T) "StateChipAction")
    Start-Sleep -Seconds 2

    $modal = Test-ModalOpen $root
    Check "it confirms before writing" ([bool]$modal) "modal: $modal"
    $ok = Find-ByName (T) "Put it back"
    if ($ok) { Invoke-Node $ok }
    Start-Sleep -Seconds 6
    [void](Wait-Ready $root)

    $status = Get-Text (Find-ById (T) "AppStatusText")
    Write-Host "  status: $status" -f DarkGray
    Check "the run reported a summary" ($status -match "Restored")

    # THE point of the whole stage: nothing about where this game lived was recorded when it was
    # held, so its files must have landed wherever the game is NOW.
    Check "the save landed at the NEW path" `
          ((Test-Path "$Fixture\save\Level.sav") -and (Get-Content "$Fixture\save\Level.sav" -Raw) -eq "the-original-world")
    Check "location A's mod landed at the NEW path" `
          ((Test-Path "$Fixture\root\ModsA\AlphaMod.pak") -and (Get-Content "$Fixture\root\ModsA\AlphaMod.pak" -Raw) -eq "from-location-A")
    Check "location B's mod landed at the NEW path" `
          ((Test-Path "$Fixture\root\ModsB\BetaMod.pak") -and (Get-Content "$Fixture\root\ModsB\BetaMod.pak" -Raw) -eq "from-location-B")

    $heldDir = Join-Path $env:LOCALAPPDATA "ModManagerBuilder\held"
    Check "the held copy is let go once it landed" (-not (Test-Path (Join-Path $heldDir "$GameId.626profile")))
}

}

Write-Host ""
Write-Host "$pass passed, $fail failed" -f $(if ($fail) { "Red" } else { "Green" })
exit $(if ($fail) { 1 } else { 0 })
