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

# The catalogue is the source of truth for WHAT EXISTS; this script is the source of truth for
# what runs. SmokeCatalogueTests fails the build if the two disagree about the executable cases,
# and the human-only bucket lives only in the catalogue so it cannot be quietly trimmed here.
$catalogPath = Join-Path $repo 'docs/smoke-tests/smoke.json'
if (-not (Test-Path $catalogPath)) { throw "smoke catalogue not found: $catalogPath" }
$catalog = Get-Content $catalogPath -Raw | ConvertFrom-Json

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

# The mod view lives in the visual tree WHILE THE LIBRARY HOME IS SHOWING - the home is a host
# swapped in over it - and its ModListView even reports IsOffscreen=False. So "ModRow.* is in the
# tree" says nothing about what is on screen. Receipt: on the 2026-08-18 run where navigate-to-game
# FAILED, game-toolbar-present, mod-list-populated, status-line-readable and loadout-segments all
# passed anyway. Four greens about a surface the harness never reached.
#
# HomeButton is the discriminator: absent on the library home, present once a game is open.
function Assert-OnGameView {
    param($Tree)
    if (-not (Find-ById $Tree 'HomeButton')) {
        throw "not on a game view (no HomeButton) - this case would have been a false green"
    }
}

# ---------------------------------------------------------------- start
Write-Host ''
Write-Host '  626 smoke harness' -ForegroundColor Cyan
Write-Host "  exe: $Exe" -ForegroundColor DarkGray
Write-Host ''

Get-Process ModManager.App -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep -Seconds 2
if (-not (Test-Path $Exe)) { throw "launcher not found: $Exe" }
Start-Process $Exe
Start-Sleep -Seconds 4

$root = Get-AppRoot
if (-not $root) { throw "app did not present a window" }

# Wait for the window to finish building rather than guessing at it. See Wait-Ready.
$built = Wait-Ready $root
Write-Host ("  window settled at {0} elements" -f $built) -ForegroundColor DarkGray

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
    Assert-OnGameView $t
    $need = @('EnableAllButton','DisableAllButton','AddModsButton','RefreshButton','ProfilesButton','SavesButton','ModFilterBox','ModListView')
    $miss = @($need | Where-Object { -not (Find-ById $t $_) })
    Assert-True ($miss.Count -eq 0) "missing: $($miss -join ', ')"
    "all $($need.Count) toolbar controls present"
}

Case 'mod-list-populated' 'ReloadModsAsync unification' {
    $t = Get-Tree $root
    Assert-OnGameView $t
    $mods = @(Find-AllByIdPrefix $t 'ModRow.')
    Assert-True ($mods.Count -gt 0) "no ModRow.* realised"
    "$($mods.Count) mod rows"
}

Case 'status-line-readable' 'Mod dashboard' {
    $t = Get-Tree $root
    Assert-OnGameView $t
    $s = Get-Text (Find-ById $t 'AppStatusText')
    Assert-True (-not [string]::IsNullOrWhiteSpace($s)) "AppStatusText empty"
    "'$s'"
}

Case 'loadout-segments' 'Loadout MP/SP' {
    $t = Get-Tree $root
    Assert-OnGameView $t
    $miss = @(@('LoadoutAllSegment','LoadoutMpSegment','LoadoutSpSegment') | Where-Object { -not (Find-ById $t $_) })
    Assert-True ($miss.Count -eq 0) "missing: $($miss -join ', ')"
    "All / MP / SP present"
}

Case 'theme-picker-reads-current' 'road-to-zero B2 / D2' {
    $t = Get-Tree $root
    Assert-OnGameView $t
    $tp = Find-ById $t 'ThemePicker'
    Assert-True ($null -ne $tp) "ThemePicker absent"
    "current theme reads '$(Get-Text $tp)'"
}

Case 'group-combo-selection-without-opening' 'Group the mod list' {
    $t = Get-Tree $root
    Assert-OnGameView $t
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
    # Scoped to the ROWS, not the whole tree. The library home is a host that stays in the tree
    # behind this view, and it legitimately renders 'Unknown' as the recency of a never-launched game.
    # A first cut of this assertion scanned everything and failed on that - a false RED, which is the
    # safe direction to be wrong in, and took two minutes to clear because the case named what it hit.
    $rowText = @($rows | ForEach-Object { Get-Tree $_ } | ForEach-Object { try { $_.Current.Name } catch { '' } })
    $unknown = @($rowText | Where-Object { $_ -like '*unknown*' }).Count
    # A backwards arrow is the A27 defect, on screen: '1.0.1 -> 1.0.0' invited an update to an older
    # version. The row text is readable, so assert on it rather than eyeballing a screenshot.
    $backwards = @($rowText | Where-Object { $_ -match '\d\s*→\s*\d' })
    if ($back) { Invoke-Node $back; Wait-Idle 2000 }
    # This case printed '0 update rows' and passed for as long as it existed, because the rows carried
    # no AutomationId and nothing asserted they did (A28). A number nobody checks is a case that
    # cannot fail.
    Assert-True ($rows.Count -gt 0) "no UpdateRow.* realised - are the row ids bound?"
    Assert-True ($unknown -eq 0) "$unknown elements still render 'unknown' (A10)"
    foreach ($b in $backwards) { Assert-True $false "arrow pair on screen (A27): $b" }
    "$($rows.Count) update rows, no 'unknown', no bare arrow pairs"
}

Write-Host ''
Write-Host '  -- intake + uninstall, end to end --' -ForegroundColor White

# The drag-and-drop gesture is the one thing UIA cannot synthesise into a WinUI window. Everything
# BEHIND it is reachable: a drop and the + Add mods picker both land in AddModsAsync(paths), so
# driving the picker exercises the ban-risk gate, classification, validate-then-extract and the
# provenance write - the whole chain minus the mouse. Say that plainly rather than claim drag-drop
# coverage we do not have.
#
# Target is Windrose deliberately: folder lane (so uninstall exists), ban risk None (so the gate
# does not block an unattended run), and a real library rather than a fixture.
$probe = Join-Path $repo 'artifacts\smoke\SmokePicker626.pak'
New-Item -ItemType Directory -Force -Path (Split-Path $probe) | Out-Null
Set-Content -Path $probe -Value 'SMOKE626 probe payload, inert' -Encoding ascii
$wrData = 'C:\Program Files (x86)\Steam\_626mods\windrose'
$wrMods = 'C:\Program Files (x86)\Steam\steamapps\common\Windrose\R5\Content\Paks\~mods'

# Start from a state where the probe is NOT installed. A leftover from a previous run sends intake
# down the REPLACEMENT path instead, which raises 'Update installed mods?' - and then the next case
# reports 'no confirm dialog' while a perfectly good modal sits on screen. That is the
# check-for-ANY-modal trap in .claude/rules/automation-ids.md, walked into by the person who wrote
# the harness that documents it.
Remove-Item (Join-Path $wrMods 'SmokePicker626.pak') -Force -EA SilentlyContinue
Remove-Item (Join-Path $wrData 'installs\SmokePicker626.json') -Force -EA SilentlyContinue

Case 'intake-via-picker' 'A25/A26 - intake records what it placed' {
    $t = Get-Tree $root
    if (-not (Find-ById $t 'AddModsButton')) {
        $home = Find-ById $t 'HomeButton'
        if ($home) { Invoke-Node $home; Wait-Idle 2500; $t = Get-Tree $root }
    }
    # Explicitly Windrose - not whichever game the earlier navigation happened to land on.
    $row = Find-ById (Get-Tree $root) 'GameRow.windrose'
    if ($row) { Invoke-Node $row; Wait-Idle 4000 }
    $t = Get-Tree $root
    Assert-OnGameView $t

    $before = @(Find-AllByIdPrefix $t 'ModRow.').Count
    Invoke-Node (Find-ById $t 'AddModsButton')
    $dlg = Get-FileDialog
    Assert-True ($null -ne $dlg) "the + Add mods picker never appeared"
    Assert-True (Submit-FileDialog -Dialog $dlg -Paths @($probe)) "the picker did not close"
    Wait-Idle 6000
    # Nothing unexpected may be left on screen - a replacement prompt, a framework nudge, anything.
    Assert-NoModal $root

    $t2 = Get-Tree $root
    $row = Find-ById $t2 'ModRow.SmokePicker626'
    Assert-True ($null -ne $row) "no ModRow.SmokePicker626 after installing through the picker"

    # The record, not the row. A25 was invisible for exactly as long as nobody looked here.
    $manifest = Join-Path $wrData 'installs\SmokePicker626.json'
    Assert-True (Test-Path $manifest) "installed but wrote no install manifest (A25)"
    $claimed = (Get-Content $manifest -Raw | ConvertFrom-Json).files
    Assert-True ($claimed -contains 'SmokePicker626.pak') "the manifest does not claim the file it placed"
    "installed through the picker, row realised, manifest claims $($claimed.Count) file(s)"
}

Case 'uninstall-confirm-and-forget' 'A26 - the record goes with the file' {
    Assert-NoModal $root
    $t = Get-Tree $root
    # Scoped to the ROW, and matched on the pattern rather than the exact string: the row prettifies
    # its key for display, so 'SmokePicker626' surfaces as 'Smoke Picker 626' and an exact-name lookup
    # for the key finds nothing. The row id is the stable handle; the button is a child of it.
    $row = Find-ById $t 'ModRow.SmokePicker626'
    Assert-True ($null -ne $row) "the probe row is not in the list to uninstall"
    $btn = @(Get-Tree $row | Where-Object { try { $_.Current.Name -like 'Uninstall *' } catch { $false } })[0]
    Assert-True ($null -ne $btn) "no uninstall affordance on the probe row"
    Invoke-Node $btn

    # A destructive confirm is a DECISION, and driving it is the point: the dialog is where the
    # launcher states what it is about to do, and an assertion beats a screenshot nobody reads.
    $dt = Get-ContentDialog $root 'Uninstall mod?' -ButtonName 'Uninstall'
    Assert-True ($null -ne $dt) "no confirm dialog before a permanent delete"
    # The copy is part of the contract: a permanent delete has to say so. Assert it, do not screenshot it.
    $body = @(Get-Tree $dt | ForEach-Object { try { $_.Current.Name } catch { '' } })
    Assert-True (@($body | Where-Object { $_ -like "*can't be undone*" }).Count -gt 0) "the confirm never says the delete is permanent"
    $confirm = @(Get-Tree $dt | Where-Object { try { $_.Current.Name -eq 'Uninstall' } catch { $false } })[0]
    Assert-True ($null -ne $confirm) "the confirm dialog has no Uninstall button"
    Invoke-Node $confirm; Wait-Idle 5000

    $t2 = Get-Tree $root
    Assert-True ($null -eq (Find-ById $t2 'ModRow.SmokePicker626')) "the row survived its uninstall"
    $gone = -not (Test-Path (Join-Path $wrData 'installs\SmokePicker626.json'))
    Assert-True $gone "the install record outlived the file it claimed (A26)"
    "uninstalled through its confirm dialog; row, file and record all gone"
}

Case 'loadout-segments-filter-only' 'Wave 6 - the segments filter, they do not move files' {
    # The whole of wave 6, as an assertion. These three buttons used to call Scanner.ApplyMode, which
    # enables every mod matching the mode and disables the rest - a bulk file operation behind a
    # control shaped like a view filter. If the enabled count ever moves when a segment is clicked,
    # the file op is back.
    $t = Get-Tree $root
    Assert-OnGameView $t
    $before = Get-Text (Find-ById $t 'AppStatusText')
    $allRows = @(Find-AllByIdPrefix $t 'ModRow.').Count
    Assert-True ($allRows -gt 0) "no rows to filter"

    Invoke-Node (Find-ById $t 'LoadoutMpSegment'); Wait-Idle 2500
    $t2 = Get-Tree $root
    $mpRows = @(Find-AllByIdPrefix $t2 'ModRow.').Count
    $after = Get-Text (Find-ById $t2 'AppStatusText')

    Invoke-Node (Find-ById (Get-Tree $root) 'LoadoutAllSegment'); Wait-Idle 2500
    $restored = @(Find-AllByIdPrefix (Get-Tree $root) 'ModRow.').Count

    Assert-True ($after -eq $before) "the enabled count changed when a segment was clicked - the segments are moving files again (wave 6)"
    Assert-True ($restored -eq $allRows) "ALL did not restore the full list"
    "MP listed $mpRows of $allRows rows; enabled count unchanged at $before"
}

# ---------------------------------------------------------------- what a harness cannot do
Write-Host ''
Write-Host '  -- cases this harness cannot run --' -ForegroundColor White

foreach ($c in $catalog.cases | Where-Object { $_.coverage -eq 'human' }) {
    HumanOnly $c.id $c.surface $c.humanReason
}

# ---------------------------------------------------------------- report
Get-Process ModManager.App -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue

$pass  = @($script:Results | Where-Object Status -eq 'PASS').Count
$fail  = @($script:Results | Where-Object Status -eq 'FAIL').Count
$human = @($script:Results | Where-Object Status -eq 'NEEDS-HUMAN').Count

# Every number below is against the CATALOGUE total, not against what this script happened to
# run. A percentage of the cases a harness chose for itself is not coverage, and reporting one
# is how a green run comes to mean less than it looks like it means.
$total     = @($catalog.cases).Count
$untriaged = @($catalog.cases | Where-Object { $_.coverage -eq 'untriaged' }).Count

Write-Host ''
Write-Host '  ============================================' -ForegroundColor Cyan
Write-Host ("   {0} verified, {1} failed, {2} require a human" -f $pass, $fail, $human) -ForegroundColor Cyan
Write-Host ("   {0} of {1} catalogue cases were executed" -f ($pass + $fail), $total) -ForegroundColor Cyan
if ($untriaged -gt 0) {
    Write-Host ("   {0} still awaiting triage - neither run nor claimed" -f $untriaged) -ForegroundColor DarkYellow
}
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
