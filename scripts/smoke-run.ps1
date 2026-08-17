<#
.SYNOPSIS
    Runs the automatable part of docs/smoke-tests/pending.md against the live app, and says plainly
    what it could not run.

.DESCRIPTION
    Backlog E2/E3. Two channels, per .claude/rules/automation-ids.md:
      - UIA assertions over AutomationIds     - deterministic, catches what we thought to assert
      - a screenshot per case                 - open-ended, catches what nobody thought to assert

    The report ALWAYS ends "N verified, M require a human, here is M". A harness that runs the
    automatable part, reports all green, and stays quiet about the rest is the cherry-picked
    denominator wearing a different hat.

.PARAMETER Exe
    Launcher to drive. Defaults to the local Debug build.

.PARAMETER OutDir
    Where per-case screenshots and the JSON result land.
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'uia-lib.ps1')

if (-not $Exe)    { $Exe = Join-Path $repo 'src/ModManager.App/bin/x64/Debug/net10.0-windows10.0.19041.0/ModManager.App.exe' }
if (-not $OutDir) { $OutDir = Join-Path $repo 'artifacts/smoke' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$script:Results = New-Object System.Collections.Generic.List[object]
$script:Seq = 0

function Case {
    param([string]$Id, [string]$Section, [scriptblock]$Body)
    $script:Seq++
    $shot = Join-Path $OutDir ("{0:d2}-{1}.png" -f $script:Seq, ($Id -replace '[^A-Za-z0-9\-]','_'))
    $status = 'PASS'; $detail = ''
    try {
        $detail = & $Body
        if ($detail -is [array]) { $detail = ($detail -join '; ') }
    } catch {
        $status = 'FAIL'; $detail = $_.Exception.Message
    }
    try { Save-Shot -Path $shot | Out-Null } catch { $shot = '' }
    $script:Results.Add([pscustomobject]@{
        Case = $Id; Section = $Section; Status = $status; Detail = "$detail"; Shot = (Split-Path $shot -Leaf)
    })
    $colour = if ($status -eq 'PASS') { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1,-38} {2}" -f $status, $Id, $detail) -ForegroundColor $colour
}

function HumanOnly {
    param([string]$Id, [string]$Section, [string]$Why)
    $script:Results.Add([pscustomobject]@{
        Case = $Id; Section = $Section; Status = 'NEEDS-HUMAN'; Detail = $Why; Shot = ''
    })
    Write-Host ("  [NEEDS-HUMAN] {0,-30} {1}" -f $Id, $Why) -ForegroundColor DarkYellow
}

function Assert-True { param([bool]$Cond, [string]$Msg) if (-not $Cond) { throw $Msg } }

# ---------------------------------------------------------------- start
Write-Host ''
Write-Host '  626 smoke harness' -ForegroundColor Cyan
Write-Host "  exe: $Exe" -ForegroundColor DarkGray
Write-Host ''

Get-Process ModManager.App -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep -Seconds 2
if (-not (Test-Path $Exe)) { throw "launcher not found: $Exe" }
Start-Process $Exe
Start-Sleep -Seconds 13

$root = Get-AppRoot
if (-not $root) { throw "app did not present a window" }

Write-Host '  -- library home --' -ForegroundColor White

Case 'app-starts' 'App launch' {
    $t = Get-Tree $root
    Assert-True ($t.Count -gt 100) "tree suspiciously small: $($t.Count)"
    "window up, $($t.Count) elements"
}

Case 'home-renders-games' 'feat/game-library-home Task 7' {
    $t = Get-Tree $root
    $rows = @(Find-AllByIdPrefix $t 'GameRow.')
    Assert-True ($rows.Count -gt 0) "no GameRow.* in tree"
    "$($rows.Count) game rows"
}

Case 'home-recent-strip' 'feat/game-library-home Task 7' {
    $t = Get-Tree $root
    $cards = @(Find-AllByIdPrefix $t 'RecentCard.')
    Assert-True ($cards.Count -gt 0) "no RecentCard.* in tree"
    "$($cards.Count) recent cards"
}

Case 'home-search-box' 'feat/game-library-home Task 7' {
    $t = Get-Tree $root
    $v = Test-Visible (Find-ById $t 'LibrarySearchBox')
    Assert-True $v.Realised "LibrarySearchBox absent"
    "present, visible=$($v.Visible)"
}

Case 'home-updates-entry' 'feat/updates-surface' {
    $t = Get-Tree $root
    $e = Find-ById $t 'LibraryUpdatesEntry'
    if (-not $e) { return "absent - correct when nothing is pending" }
    "present: '$(Get-Text $e)'"
}

Write-Host '  -- discovery lane (v0.18.1 fix) --' -ForegroundColor White

Case 'discovery-lane-present' 'fix/discovery-visible-on-first-run' {
    $t = Get-Tree $root
    $exp = Find-ById $t 'LibraryDiscoveryExpander'
    Assert-True ($null -ne $exp) "LibraryDiscoveryExpander absent"
    "present, offscreen=$($exp.Current.IsOffscreen)"
}

Case 'discovery-lane-expands' 'fix/discovery-visible-on-first-run' {
    $t = Get-Tree $root
    $exp = Find-ById $t 'LibraryDiscoveryExpander'
    Expand-Node $exp; Wait-Idle 1400
    $sub = Get-Tree $exp
    $adds = @($sub | Where-Object { try { $_.Current.Name -like 'Add *' } catch { $false } })
    Collapse-Node $exp; Wait-Idle 600
    Assert-True ($adds.Count -gt 0) "expanded but no '+ Add' rows realised"
    "$($adds.Count) discovered games, state restored"
}

Write-Host '  -- navigate to a game (v0.18.0 repaint surface) --' -ForegroundColor White

$script:gameId = $null
Case 'navigate-to-game' 'fix/library-repaint-after-add' {
    # Choose the target by READING each row's mod-state text before navigating, rather than opening
    # games until one sticks. Blind row-0 landed on a game with no mods, which failed the next two
    # cases for a fixture reason; walking back out via HomeButton then hit a control that does not
    # expose Invoke. Reading first needs neither.
    $rows = @(Find-AllByIdPrefix (Get-Tree $root) 'GameRow.')
    Assert-True ($rows.Count -gt 0) "no game rows to open"

    $target = $null; $targetLabel = ''
    foreach ($r in $rows) {
        $texts = @(Get-Tree $r | ForEach-Object { try { $_.Current.Name } catch { '' } })
        $state = $texts | Where-Object { $_ -match '^\d+ mods? ' } | Select-Object -First 1
        if ($state) { $target = $r; $targetLabel = $state; break }
    }
    Assert-True ($null -ne $target) "no registered game reports any mods - nothing to exercise"

    $script:gameId = ($target.Current.AutomationId -replace '^GameRow\.','')
    Invoke-Node $target; Wait-Idle 4000
    $t2 = Get-Tree $root
    Assert-True ($null -ne (Find-ById $t2 'HomeButton')) "HomeButton absent after navigation"
    "opened '$($script:gameId)' ($targetLabel), game-view chrome realised"
}

Case 'game-toolbar-present' 'Mod dashboard' {
    $t = Get-Tree $root
    $need = @('EnableAllButton','DisableAllButton','AddModsButton','RefreshButton','ProfilesButton','SavesButton','ModFilterBox','ModListView')
    $miss = @($need | Where-Object { -not (Find-ById $t $_) })
    Assert-True ($miss.Count -eq 0) "missing: $($miss -join ', ')"
    "all $($need.Count) toolbar controls present"
}

Case 'mod-list-populated' 'ReloadModsAsync unification' {
    $t = Get-Tree $root
    $mods = @(Find-AllByIdPrefix $t 'ModRow.')
    Assert-True ($mods.Count -gt 0) "no ModRow.* realised"
    "$($mods.Count) mod rows"
}

Case 'status-line-readable' 'Mod dashboard' {
    $t = Get-Tree $root
    $s = Get-Text (Find-ById $t 'AppStatusText')
    Assert-True (-not [string]::IsNullOrWhiteSpace($s)) "AppStatusText empty"
    "'$s'"
}

Case 'loadout-segments' 'Loadout MP/SP' {
    $t = Get-Tree $root
    $miss = @(@('LoadoutAllSegment','LoadoutMpSegment','LoadoutSpSegment') | Where-Object { -not (Find-ById $t $_) })
    Assert-True ($miss.Count -eq 0) "missing: $($miss -join ', ')"
    "All / MP / SP present"
}

Case 'theme-picker-reads-current' 'road-to-zero B2 / D2' {
    $t = Get-Tree $root
    $tp = Find-ById $t 'ThemePicker'
    Assert-True ($null -ne $tp) "ThemePicker absent"
    "current theme reads '$(Get-Text $tp)'"
}

Case 'group-combo-selection-without-opening' 'Group the mod list' {
    $t = Get-Tree $root
    $sel = Get-Selection (Find-ById $t 'GroupModeCombo')
    Assert-True (-not [string]::IsNullOrWhiteSpace($sel)) "no selection readable"
    "selection='$sel' (popup never opened)"
}

Write-Host '  -- reversible state change --' -ForegroundColor White

Case 'mod-toggle-round-trip' 'Toggle = move-to-holding, reversible' {
    $t = Get-Tree $root
    $tog = @($t | Where-Object { try { $null -ne (Get-ToggleState $_) -and ($_.Current.Name -like 'Disable *' -or $_.Current.Name -like 'Enable *') } catch { $false } }) | Select-Object -First 1
    Assert-True ($null -ne $tog) "no mod toggle found"
    $name = $tog.Current.Name
    $before = Get-ToggleState $tog
    Set-Toggle $tog; Wait-Idle 4000
    $t2 = Get-Tree $root
    $tog2 = @($t2 | Where-Object { try { $null -ne (Get-ToggleState $_) } catch { $false } }) | Select-Object -First 1
    $mid = Get-ToggleState $tog2
    Assert-True ($mid -ne $before) "toggle did not change state ($before -> $mid)"
    Set-Toggle $tog2; Wait-Idle 4000
    $t3 = Get-Tree $root
    $tog3 = @($t3 | Where-Object { try { $null -ne (Get-ToggleState $_) } catch { $false } }) | Select-Object -First 1
    $after = Get-ToggleState $tog3
    Assert-True ($after -eq $before) "NOT RESTORED: $before -> $mid -> $after"
    "'$name' $before -> $mid -> $after (restored)"
}

Write-Host '  -- dialogs --' -ForegroundColor White

function Close-Dialog {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Wait-Idle 1500
}

Case 'settings-dialog' 'Settings surfaces / D1' {
    $t = Get-Tree $root
    Invoke-Node (Find-ById $t 'SettingsButton'); Wait-Idle 2500
    $t2 = Get-Tree $root
    $need = @('PickImageButton','BackdropBox','UseAsIconCheck','DeriveThemeCheck')
    $found = @($need | Where-Object { Find-ById $t2 $_ })
    Close-Dialog
    Assert-True ($found.Count -gt 0) "no settings controls realised"
    "$($found.Count)/$($need.Count) probed controls present"
}

Case 'add-game-dialog' 'fix/duplicate-add-guard' {
    $t = Get-Tree $root
    Invoke-Node (Find-ById $t 'AddGameButton'); Wait-Idle 3000
    $t2 = Get-Tree $root
    $need = @('GameNameBox','EngineBox','FolderBox','BrowseButton','ModPathBox','SteamAppIdBox')
    $found = @($need | Where-Object { Find-ById $t2 $_ })
    Close-Dialog
    Assert-True ($found.Count -eq $need.Count) "only $($found.Count)/$($need.Count) fields present"
    "all $($need.Count) manual fields present"
}

Case 'profiles-dialog' 'Profiles / loadouts' {
    $t = Get-Tree $root
    Invoke-Node (Find-ById $t 'ProfilesButton'); Wait-Idle 2500
    $t2 = Get-Tree $root
    $ok = ($null -ne (Find-ById $t2 'ProfileList')) -or ($null -ne (Find-ById $t2 'ProfileNameBox'))
    Close-Dialog
    Assert-True $ok "ProfilesDialog controls absent"
    "opened and closed"
}

Case 'saves-dialog' 'Save snapshots' {
    $t = Get-Tree $root
    Invoke-Node (Find-ById $t 'SavesButton'); Wait-Idle 3000
    $t2 = Get-Tree $root
    $need = @('SnapshotList','BackupNowButton','AutoBackupCheck','SavesFolderBox')
    $found = @($need | Where-Object { Find-ById $t2 $_ })
    Close-Dialog
    Assert-True ($found.Count -gt 0) "no saves controls realised"
    "$($found.Count)/$($need.Count) probed controls present"
}

Write-Host '  -- cross-game views --' -ForegroundColor White

Case 'updates-view' 'feat/updates-surface (A10/A11 surface)' {
    $t = Get-Tree $root
    $homeBtn = Find-ById $t 'HomeButton'
    if ($homeBtn) { Invoke-Node $homeBtn; Wait-Idle 2500 }
    $t2 = Get-Tree $root
    $entry = Find-ById $t2 'LibraryUpdatesEntry'
    if (-not $entry) { return "no pending updates - view not reachable from home" }
    Invoke-Node $entry; Wait-Idle 3500
    $t3 = Get-Tree $root
    $rows = @(Find-AllByIdPrefix $t3 'UpdateRow.')
    $back = Find-ById $t3 'UpdatesBackButton'
    $unknown = @($t3 | Where-Object { try { $_.Current.Name -like '*unknown*' } catch { $false } }).Count
    if ($back) { Invoke-Node $back; Wait-Idle 2000 }
    "$($rows.Count) update rows; $unknown elements rendering 'unknown' (A10)"
}

# ---------------------------------------------------------------- what a harness cannot do
Write-Host ''
Write-Host '  -- cases this harness cannot run --' -ForegroundColor White

HumanOnly 'bnd4-save-walk'        'PR #49'  'Needs a real Elden Ring / Seamless .co2 save on disk'
HumanOnly 'framework-intake-elm'  'PR ??'   'Needs the Elden Mod Loader zip to drop'
HumanOnly 'tool-intake-wse'       'PR ??'   'Needs the WSE tool zip to drop'
HumanOnly 'dependency-chip-ue4ss' 'PR #51'  'Needs UE4SS.dll deleted from the game folder, then restored'
HumanOnly 'safe-clear-round-trip' 'Phase 1B' 'Destructive against a real game folder; two-drive setup. Explicit go-ahead only'
HumanOnly 'safe-clear-refusal'    'Task 2'  'Needs the game actually running'
HumanOnly 'vanilla-vs-modded'     'feat/vanilla-modded-launch' 'Needs the game launched and observed in-engine'
HumanOnly 'nexus-oauth-connect'   'Task 11' 'Needs a live Nexus sign-in in a browser'
HumanOnly 'nexus-download-endorse' 'Nexus endorse' 'Needs a live Nexus account action against the real site'
HumanOnly 'steam-build-warning'   'Phase 2' 'Needs Steam to actually update a game'
HumanOnly 'vortex-takeover'       '2026-06-02' 'Needs a Vortex-managed game staged on this box'
HumanOnly 'ban-risk-ack-gate'     '2026-06-15' 'Agent must REACH the gate and never satisfy it - the ack is human-only by design'

# ---------------------------------------------------------------- report
Get-Process ModManager.App -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue

$pass  = @($script:Results | Where-Object Status -eq 'PASS').Count
$fail  = @($script:Results | Where-Object Status -eq 'FAIL').Count
$human = @($script:Results | Where-Object Status -eq 'NEEDS-HUMAN').Count

Write-Host ''
Write-Host '  ============================================' -ForegroundColor Cyan
Write-Host ("   {0} verified, {1} failed, {2} require a human" -f $pass, $fail, $human) -ForegroundColor Cyan
Write-Host '  ============================================' -ForegroundColor Cyan
if ($fail -gt 0) {
    Write-Host ''
    Write-Host '  FAILURES:' -ForegroundColor Red
    $script:Results | Where-Object Status -eq 'FAIL' | ForEach-Object { Write-Host "   $($_.Case): $($_.Detail)" -ForegroundColor Red }
}
Write-Host ''
Write-Host '  REQUIRE A HUMAN (not covered by any green above):' -ForegroundColor DarkYellow
$script:Results | Where-Object Status -eq 'NEEDS-HUMAN' | ForEach-Object { Write-Host "   $($_.Case) - $($_.Detail)" -ForegroundColor DarkYellow }

$json = Join-Path $OutDir 'results.json'
$script:Results | ConvertTo-Json -Depth 4 | Set-Content $json -Encoding UTF8
Write-Host ''
Write-Host "  results: $json" -ForegroundColor DarkGray
Write-Host "  shots:   $OutDir" -ForegroundColor DarkGray
Write-Host ''
if ($fail -gt 0) { exit 1 }
