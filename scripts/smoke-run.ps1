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

.PARAMETER Only
    Run just these case ids. Every other case is skipped, not reported. For re-running one surface
    without driving the rest - several cases write to Windrose's real ~mods, so a scoped run is the
    only way to exercise the read-only cases on their own.
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [string]$OutDir,
    [string[]]$Only
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
    if ($Only -and $Only -notcontains $Id) { return }
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

Write-Host '  -- one library list (B6) --' -ForegroundColor White

# The discovery expander is gone (B6): installed games 626 doesn't manage are rows in the one list,
# prefixed UnmanagedGame. so they never mix into the GameRow. walk below, which expects mod state.
Case 'library-lists-unmanaged-games' 'B6 one list' {
    $rows = @(Find-AllByIdPrefix (Get-Tree $root) 'UnmanagedGame.')
    Assert-True ($rows.Count -gt 0) "no unmanaged game rows - every installed game is managed, or the list lost them"
    $managed = @(Find-AllByIdPrefix (Get-Tree $root) 'GameRow.')
    "$($rows.Count) unmanaged and $($managed.Count) managed rows in one list"
}

Case 'unmanaged-row-offers-play-and-manage' 'B6 one list' {
    $row = @(Find-AllByIdPrefix (Get-Tree $root) 'UnmanagedGame.') | Select-Object -First 1
    Assert-True ($null -ne $row) "no unmanaged row to inspect"
    $names = @(Get-Tree $row | ForEach-Object { try { $_.Current.Name } catch { '' } })
    $manage = @($names | Where-Object { $_ -like 'Start managing *' })
    $play = @($names | Where-Object { $_ -like 'Play *' })
    $id = $row.Current.AutomationId
    Assert-True ($manage.Count -eq 1) "$id has no 'Start managing' button"
    # Steam and EA installs launch through their store; a row for any other store has no Play at all.
    if ($id -like 'UnmanagedGame.steam.*' -or $id -like 'UnmanagedGame.ea.*') {
        Assert-True ($play.Count -eq 1) "$id is a Steam/EA install with no Play button"
    }
    Assert-True (-not ($names | Where-Object { $_ -match '^\d+ mods? ' })) "$id shows mod state for a game 626 doesn't manage"
    "$id offers '$($manage[0])' and $($play.Count) Play"
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

Case 'state-strip-coherent' 'Wave 7 game-state strip' {
    # The invariant that matters, and the one the first build of this strip broke: a chip must never
    # be the ONLY thing said. Gating the expanded sentence on Danger severity collapsed Windrose's
    # "Steam updated this game" - a full-width banner with a one-click Mark as rechecked - down to a
    # four-letter chip reading UPDATED with the sentence and the button both a tap away. Fewer
    # surfaces has to mean less clutter, never less information.
    $t = Get-Tree $root
    Assert-OnGameView $t
    $chips = @(Find-AllByIdPrefix $t 'StateChip.')
    if ($chips.Count -eq 0) { return "no conditions hold on '$($script:gameId)' - strip correctly absent" }

    Assert-True ($null -ne (Find-ById $t 'GameStateStrip')) "chips present but no GameStateStrip container"
    $detail = Find-ById $t 'StateChipDetail'
    Assert-True ($null -ne $detail) "$($chips.Count) chip(s) and no expanded sentence - a label is not information"
    $sentence = Get-Text $detail
    Assert-True (-not [string]::IsNullOrWhiteSpace($sentence)) "StateChipDetail is empty"
    "$($chips.Count) chip(s), leading with '$sentence'"
}

Case 'ban-risk-chip-addressable' 'Wave 7 game-state strip' {
    # The assertion that would have caught the original fault. Before this wave the highest-
    # consequence line in the app had AutomationProperties.HelpText and NO AutomationId, so a harness
    # could assert the low-stakes banners existed and could not cleanly assert this one did. It is
    # keyed on the FACT (StateChip.ban-risk), which is why the same assertion held before and after
    # the move.
    $t = Get-Tree $root
    if (Find-ById $t 'HomeButton') { Invoke-Node (Find-ById $t 'HomeButton'); Wait-Idle 3000 }

    $rows = @(Find-AllByIdPrefix (Get-Tree $root) 'GameRow.')
    $target = $null
    foreach ($r in $rows) {
        $texts = @(Get-Tree $r | ForEach-Object { try { $_.Current.Name } catch { '' } })
        if ($texts -contains 'BAN RISK') { $target = $r; break }
    }
    if ($null -eq $target) { throw "SKIP: no ban-risk game registered on this machine" }

    $id = ($target.Current.AutomationId -replace '^GameRow\.','')
    try {
        Invoke-Node $target; Wait-Idle 5000
        $t2 = Get-Tree $root
        Assert-OnGameView $t2
        $chip = Find-ById $t2 'StateChip.ban-risk'
        Assert-True ($null -ne $chip) "'$id' is flagged ban-risk in the library and shows no ban-risk chip"

        # First in the strip, not merely present. The whole fault was a true statement rendered small.
        $chips = @(Find-AllByIdPrefix $t2 'StateChip.')
        Assert-True ($chips[0].Current.AutomationId -eq 'StateChip.ban-risk') "ban risk is not the first chip"

        # And the sentence on show is ITS sentence. This is what caught the bug: Palworld is both
        # ban-risk and Steam-updated, and arriving from Windrose - where the Steam chip was expanded -
        # kept the Steam sentence showing while BAN RISK sat first in the row, unread.
        $said = Get-Text (Find-ById $t2 'StateChipDetail')
        Assert-True ($said -match 'anti-cheat') "the leading sentence does not mention anti-cheat: '$said'"
        "'$id': ban risk leads the strip, reading '$said'"
    }
    finally {
        # Put the fixture back, INCLUDING on failure. This case navigates to a game of its own
        # choosing, and the first run left the app parked on a no-mods game after the assertion threw:
        # five later cases went red for a fixture reason and read as five new bugs. A harness that
        # reorganises the app it just verified is its own bug; one that tidies up only when everything
        # passed is worse, because it hides the mess exactly when there is one.
        if ($script:gameId -and $id -ne $script:gameId) {
            $h = Find-ById (Get-Tree $root) 'HomeButton'
            if ($h) { Invoke-Node $h; Wait-Idle 3000 }
            $back = Find-ById (Get-Tree $root) ("GameRow." + $script:gameId)
            if ($back) { Invoke-Node $back; Wait-Idle 5000 }
        }
    }
}

Case 'browse-door-is-present-and-labelled' 'Wave 8 item 3' {
    # It used to bind CatalogVisibility and VANISH whenever in-app browsing was unavailable, which
    # made the app present as though the in-app storefront had never been built. It now shows whenever
    # the game has a Nexus domain, and the label says WHERE YOU LAND rather than what it searches -
    # it read "Browse Nexus" beside a button reading "Find mods" and neither said which one left the
    # app.
    $t = Get-Tree $root
    Assert-OnGameView $t
    $inApp = Find-ById $t 'BrowseNexusButton'
    $browser = Find-ById $t 'FindModsButton'

    Assert-True ($null -ne $browser) "FindModsButton absent - the browser door should never be gated"
    if ($null -eq $inApp) { return "no Nexus domain for '$($script:gameId)' - correctly absent" }

    $a = Get-Text $inApp
    $b = Get-Text $browser
    Assert-True ($a -ne $b) "both doors read the same: '$a'"
    Assert-True ($a -match 'in-app') "the in-app door does not say so: '$a'"
    Assert-True ($b -match 'browser') "the browser door does not say so: '$b'"
    "'$a' / '$b'"
}

Case 'empty-list-is-never-wordless' 'Wave 8 item 4' {
    # Filter to something that cannot match and assert the app SAYS so. Before wave 8 the zero-match
    # branch required search text, so wave 6's MP/SP filter could empty the list silently - and a
    # blank list under a view control reads as "the mods are gone".
    $t = Get-Tree $root
    Assert-OnGameView $t
    $box = Find-ById $t 'ModFilterBox'
    Assert-True ($null -ne $box) "ModFilterBox absent"

    try {
        # ValuePattern, not SendKeys: it needs no assembly load, does not depend on what the SHELL
        # thinks is foreground, and cannot land the keystrokes in another window.
        Set-EditValue $box 'zzqqxx'
        Wait-Idle 1200
        $t2 = Get-Tree $root
        $rows = @(Find-AllByIdPrefix $t2 'ModRow.')
        Assert-True ($rows.Count -eq 0) "filter matched $($rows.Count) rows - pick a less likely string"
        $said = Get-Text (Find-ById $t2 'ModListEmptyText')
        Assert-True (-not [string]::IsNullOrWhiteSpace($said)) "the list emptied and said nothing"
        Assert-True ($said -match 'zzqqxx') "the message does not name the query: '$said'"
        "empty list said '$said'"
    }
    finally {
        # Put the search box back, including on failure - every later case reads this mod list.
        $b2 = Find-ById (Get-Tree $root) 'ModFilterBox'
        if ($b2) { Set-EditValue $b2 ''; Wait-Idle 1200 }
    }
}

Case 'needs-chip-is-invokable' 'Wave 8 item 5' {
    # The chip was a HyperlinkButton to a GitHub releases page - so the app's answer to "you need
    # UE4SS" was a list of files a first-time modder cannot choose between. It is a Button now, onto
    # an offer that includes the install the launcher can already perform. Assert it is REACHABLE and
    # addressable; opening the picker behind it is a human case.
    $t = Get-Tree $root
    Assert-OnGameView $t
    $chips = @(Find-AllByIdPrefix $t 'ModNeeds.')
    if ($chips.Count -eq 0) { return "nothing on '$($script:gameId)' is missing a framework" }

    $names = @($chips | ForEach-Object { $_.Current.Name })
    Assert-True ($names[0] -match 'Install') "the chip does not offer an install: '$($names[0])'"
    "$($chips.Count) NEEDS chip(s), first reads '$($names[0])'"
}

Case 'group-combo-selection-without-opening' 'Group the mod list' {
    $t = Get-Tree $root
    Assert-OnGameView $t
    $sel = Get-Selection (Find-ById $t 'GroupModeCombo')
    Assert-True (-not [string]::IsNullOrWhiteSpace($sel)) "no selection readable"
    "selection='$sel' (popup never opened)"
}

Write-Host '  -- reversible state change --' -ForegroundColor White

# Find one mod's toggle by the MOD, never by position. Its name flips between "Disable X" and
# "Enable X" with its state, so match both.
function Find-ModToggle {
    param($Root, [string]$ModName)
    @(Get-Tree $Root | Where-Object {
        try { $null -ne (Get-ToggleState $_) -and ($_.Current.Name -eq "Disable $ModName" -or $_.Current.Name -eq "Enable $ModName") }
        catch { $false }
    }) | Select-Object -First 1
}

Case 'mod-toggle-round-trip' 'Toggle = move-to-holding, reversible' {
    # This case used to re-find the toggle as "the first toggle in the tree" after each flip. The list
    # sorts enabled mods first, so turning the top mod off moves it down and the first toggle becomes
    # a DIFFERENT mod, still on. The case read that as "did not change", threw before the flip back,
    # and left the real mod off. Four runs on 2026-09-14 turned off four of Elden Ring's mods that way
    # before anyone looked. It keys on the mod's name now, and puts the mod back in a finally.
    $t = Get-Tree $root
    Assert-OnGameView $t
    $tog = @($t | Where-Object { try { $null -ne (Get-ToggleState $_) -and $_.Current.Name -like 'Disable *' } catch { $false } }) | Select-Object -First 1
    Assert-True ($null -ne $tog) "no enabled mod toggle found"
    $mod = $tog.Current.Name -replace '^Disable ', ''
    $before = Get-ToggleState $tog
    $mid = $null; $after = $null
    try {
        Set-Toggle $tog; Wait-Idle 4000
        $modal = Test-ModalOpen $root
        if ($modal) {
            # A confirm (the loader-disable warning, say) is the app asking a person. Cancel is its
            # no-op answer: nothing has touched disk. Do not answer yes on the user's behalf.
            Add-Type -AssemblyName System.Windows.Forms
            [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Wait-Idle 2000
            throw "turning off '$mod' opened a confirm ('$modal') - cancelled it; this case needs a mod that toggles without one"
        }
        $mid = Get-ToggleState (Find-ModToggle $root $mod)
        Assert-True ($mid -ne $before) "'$mod' did not change state ($before -> $mid)"
        Set-Toggle (Find-ModToggle $root $mod); Wait-Idle 4000
        $after = Get-ToggleState (Find-ModToggle $root $mod)
        Assert-True ($after -eq $before) "NOT RESTORED: '$mod' $before -> $mid -> $after"
        "'$mod' $before -> $mid -> $after (restored)"
    }
    finally {
        # Put the mod back on every path, including a failed assertion. A harness that tidies up only
        # on success hides the mess exactly when there is one.
        $now = Find-ModToggle $root $mod
        if ($now -and (Get-ToggleState $now) -ne $before -and -not (Test-ModalOpen $root)) {
            Set-Toggle $now; Wait-Idle 4000
            $check = Find-ModToggle $root $mod
            if ($check -and (Get-ToggleState $check) -ne $before) {
                Write-Host "  !! '$mod' is left $((Get-ToggleState $check)) - turn it back on by hand" -ForegroundColor Red
            }
        }
    }
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

Case 'settings-groups-in-consequence-order' 'Wave 9 / D1' {
    # Written BEFORE any XAML moved, and it failed red - which is the point. A group assertion
    # authored after the regroup asserts a brand-new layout against a brand-new expectation: green,
    # and worth nothing. Same discipline as wave 7's ban-risk id.
    $t = Get-Tree $root
    Invoke-Node (Find-ById $t 'SettingsButton'); Wait-Idle 2500
    try {
        $t2 = Get-Tree $root
        $want = @('SettingsGroup.appearance','SettingsGroup.accounts','SettingsGroup.restore','SettingsGroup.reset')
        $miss = @($want | Where-Object { -not (Find-ById $t2 $_) })
        Assert-True ($miss.Count -eq 0) "no such group(s): $($miss -join ', ')"

        # Consequence order, top to bottom. Reset is LAST because bottom-of-scroll is the danger
        # convention - and it is the reason this is one scroll rather than a rail, which would have
        # put the most consequential control in the dialog behind a click.
        #
        # Read the order from the TREE, not from BoundingRectangle. Geometry lies here: Reset sits
        # below the fold of a MaxHeight=640 ScrollViewer, reports IsOffscreen, and hands back a
        # degenerate rect whose Top is 0 - so a geometric comparison said Reset was ABOVE everything.
        # It failed loudly, which was lucky; the same shape one group shorter would have passed.
        $order = @('SettingsGroup.appearance','SettingsGroup.accounts','SettingsGroup.restore','SettingsGroup.reset','SettingsFooter')
        $seen = @($t2 | ForEach-Object { $_.Current.AutomationId } |
                  Where-Object { $order -contains $_ })
        Assert-True ($null -ne (Find-ById $t2 'SettingsFooter')) "no SettingsFooter - About has not been demoted"
        Assert-True (($seen -join '|') -eq ($order -join '|')) "order is: $($seen -join ' -> ')"
        "4 groups in order, About in the footer"
    }
    finally { Close-Dialog; Wait-Idle 800 }
}

Case 'settings-nothing-under-reset-but-reset' 'Wave 9 / D1' {
    # D1 is emphatic: restore points are NOT danger, they are the undo FOR it. Filing them under a
    # danger heading teaches the user to fear the control that saves them. So the reset group holds
    # exactly one interactive control, and the restore list sits in its own group directly above.
    $t = Get-Tree $root
    Invoke-Node (Find-ById $t 'SettingsButton'); Wait-Idle 2500
    try {
        $t2 = Get-Tree $root
        $reset = Find-ById $t2 'SettingsGroup.reset'
        Assert-True ($null -ne $reset) "no SettingsGroup.reset"

        $inside = @(Get-Tree $reset | Where-Object {
            $_.Current.ControlType.ProgrammaticName -match 'Button|CheckBox|Edit|ComboBox'
        })
        Assert-True ($inside.Count -eq 1) "$($inside.Count) controls under Reset, expected exactly 1"
        Assert-True ($inside[0].Current.AutomationId -eq 'ResetLauncherButton') "under Reset: '$($inside[0].Current.AutomationId)'"

        # Tree order, not geometry - see settings-groups-in-consequence-order for why.
        $ids = @($t2 | ForEach-Object { $_.Current.AutomationId })
        $iRestore = [array]::IndexOf($ids, 'SettingsGroup.restore')
        $iReset   = [array]::IndexOf($ids, 'SettingsGroup.reset')
        Assert-True ($iRestore -ge 0) "restore points have no group of their own"
        Assert-True ($iRestore -lt $iReset) "restore points sit below Reset"
        "only ResetLauncherButton under Reset; restore points above it"
    }
    finally { Close-Dialog; Wait-Idle 800 }
}

# 'settings-plugin-button-never-hides' was retired 2026-09-14. 05002a3 compiled Nexus into every
# build and removed the plugin feed, so RefreshPluginButton was deleted on purpose and the case failed
# on every run afterwards. Its catalogue entry went with it: the plugin-delivery surface it pinned is
# gone from the app. ('retired' does not fit it: that value keeps a triaged PROSE section as history,
# requires a pending.md source, and points at the case that carries its verdict. A deleted harness
# case has no prose to keep and nothing to point at.)

Case 'settings-inventories-moved-not-deleted' 'Wave 9 / D1' {
    # The inventories leave Settings, but two of their actions lived ONLY there: framework Uninstall
    # and the direct-inject config Override. Deleting the sections without rehoming those would
    # remove things the user can do. This asserts the new homes exist.
    $t = Get-Tree $root
    Assert-OnGameView $t

    $rows = @(Find-AllByIdPrefix $t 'ModRow.')
    Assert-True ($rows.Count -gt 0) "no mod rows to check"

    $notes = @()

    # Framework uninstall, on the chip. Assert it wherever a framework is installed.
    $fw = @(Find-AllByIdPrefix $t 'FrameworkChip.')
    if ($fw.Count -eq 0) {
        $notes += "NOT ASSERTED: no frameworks installed on '$($script:gameId)'"
    } else {
        $un = @(Find-AllByIdPrefix $t 'FrameworkUninstall.')
        Assert-True ($un.Count -eq $fw.Count) "$($fw.Count) framework chip(s) but $($un.Count) uninstall control(s)"
        $notes += "$($fw.Count) framework chip(s), each with an uninstall"
    }

    # Per-mod config override, on the mod it configures. Only catalog-known direct-inject mods carry
    # one, so a game with none cannot exercise this - and it says so rather than passing quietly.
    # A green that never ran is the cherry-picked denominator wearing a different hat.
    $ovr = @(Find-AllByIdPrefix $t 'ModConfigOverride.')
    if ($ovr.Count -eq 0) {
        $notes += "NOT ASSERTED: no direct-inject mods on '$($script:gameId)' to carry a config override"
    } else {
        $notes += "$($ovr.Count) config-override control(s)"
    }

    $notes -join '; '
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
    #
    # The A27 fix KEPT the arrow where the direction is provable - both sides plain dotted numbers and
    # the right one higher - so '1.9.9 → 2.0.0' is correct and must pass. This case first flagged every
    # arrow, which held only while the live install happened to have no provable update; the day one
    # arrived it went red on the behaviour A27 asked for. Mirror ModUpdateSummary.LatestIsProvablyNewer:
    # every segment digits (a leading v allowed), missing segments count as 0.
    function ConvertTo-DottedNumbers([string]$v) {
        if ([string]::IsNullOrWhiteSpace($v)) { return $null }
        $parts = $v.Trim().TrimStart('v', 'V').Split('.')
        $nums = @()
        foreach ($p in $parts) { if ($p -notmatch '^\d+$') { return $null }; $nums += [int]$p }
        if ($nums.Count -eq 0) { return $null }
        return ,$nums
    }
    function Test-ProvablyNewer([string]$installed, [string]$latest) {
        $a = ConvertTo-DottedNumbers $installed; $b = ConvertTo-DottedNumbers $latest
        if ($null -eq $a -or $null -eq $b) { return $false }
        for ($i = 0; $i -lt [Math]::Max($a.Count, $b.Count); $i++) {
            $x = if ($i -lt $a.Count) { $a[$i] } else { 0 }
            $y = if ($i -lt $b.Count) { $b[$i] } else { 0 }
            if ($x -ne $y) { return $x -lt $y }
        }
        return $false
    }
    $backwards = @($rowText | Where-Object {
        $_ -match '(\S+)\s*→\s*(\S+)' -and -not (Test-ProvablyNewer $Matches[1] $Matches[2])
    })
    if ($back) { Invoke-Node $back; Wait-Idle 2000 }
    # This case printed '0 update rows' and passed for as long as it existed, because the rows carried
    # no AutomationId and nothing asserted they did (A28). A number nobody checks is a case that
    # cannot fail.
    Assert-True ($rows.Count -gt 0) "no UpdateRow.* realised - are the row ids bound?"
    Assert-True ($unknown -eq 0) "$unknown elements still render 'unknown' (A10)"
    foreach ($b in $backwards) { Assert-True $false "arrow that does not provably point to a newer version (A27): $b" }
    $arrows = @($rowText | Where-Object { $_ -match '→' }).Count
    "$($rows.Count) update rows, no 'unknown', $arrows arrow(s) all provably forward"
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
# Skipped on a scoped run that does not include the intake case: a -Only run touches no real game's folder.
if (-not $Only -or $Only -contains 'intake-via-picker') {
    Remove-Item (Join-Path $wrMods 'SmokePicker626.pak') -Force -EA SilentlyContinue
    Remove-Item (Join-Path $wrData 'installs\SmokePicker626.json') -Force -EA SilentlyContinue
}

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

# Two mods whose names Windows reads as one folder. Under Windrose's UE-pak rule 'B4AliasProbe_P.pak' is
# the mod 'B4AliasProbe' and 'B4AliasProbe _P.pak' is 'B4AliasProbe ' - trailing space. Windows strips a
# trailing space from a path segment, so before the holding-name fix the second was held in the FIRST's
# folder. Now it gets '~626~' + the hex of its UTF-8 bytes. Probes go straight into ~mods (not through
# intake, so no install record), and the finally removes them and anything held for these two names ONLY.
$aliasProbes = [ordered]@{ 'B4AliasProbe' = 'B4AliasProbe_P.pak'; 'B4AliasProbe ' = 'B4AliasProbe _P.pak' }
$aliasDisabled = Join-Path $wrData 'disabled'

# Mirrors HoldingName.Folder for these two names: an ordinary name is its own folder; one ending in a dot
# or space is encoded. (The other risky classes - device names, invalid characters - don't apply here.)
function Get-AliasHoldingFolder([string]$Name) {
    if ($Name.EndsWith(' ') -or $Name.EndsWith('.')) {
        return '~626~' + (-join ([Text.Encoding]::UTF8.GetBytes($Name) | ForEach-Object { $_.ToString('x2') }))
    }
    return $Name
}

# The row by its bound id, compared case- AND whitespace-exactly: 'ModRow.B4AliasProbe' and
# 'ModRow.B4AliasProbe ' differ only by the trailing space, and a looser match would hand back the
# wrong row. Realises the row first if the list virtualised it away.
function Find-AliasRow([string]$Name) {
    $id = "ModRow.$Name"
    $null = Test-RowPresent (Get-Tree $root) $id
    @(Get-Tree $root | Where-Object { try { $_.Current.AutomationId -ceq $id } catch { $false } }) | Select-Object -First 1
}

# The row's own toggle, found inside the row: both probes can prettify to the same display name, so a
# tree-wide "Disable <name>" match could pick the other one.
function Find-AliasToggle([string]$Name) {
    $row = Find-AliasRow $Name
    if (-not $row) { return $null }
    @(Get-Tree $row | Where-Object { try { $null -ne (Get-ToggleState $_) } catch { $false } }) | Select-Object -First 1
}

function Reset-AliasProbes {
    foreach ($n in $aliasProbes.Keys) {
        $f = $aliasProbes[$n]
        Remove-Item -LiteralPath (Join-Path $wrMods $f) -Force -EA SilentlyContinue
        $held = Join-Path $aliasDisabled (Get-AliasHoldingFolder $n)
        if (Test-Path -LiteralPath $held) { Remove-Item -LiteralPath $held -Recurse -Force -EA SilentlyContinue }
    }
}

Case 'alias-names-hold-apart' 'fix/toggle-name-alias - holding-folder names Windows cannot alias' {
    Reset-AliasProbes
    $hashes = @{}
    try {
        $i = 0
        foreach ($n in $aliasProbes.Keys) {
            $i++
            $p = Join-Path $wrMods $aliasProbes[$n]
            Set-Content -LiteralPath $p -Value "SMOKE626 alias probe $i, inert" -Encoding ascii
            $hashes[$n] = (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash
        }

        # Explicitly Windrose, then a reload so the probes are read.
        $t = Get-Tree $root
        if (-not (Find-ById $t 'AddModsButton')) {
            $homeBtn = Find-ById $t 'HomeButton'
            if ($homeBtn) { Invoke-Node $homeBtn; Wait-Idle 2500 }
        }
        $wr = Find-ById (Get-Tree $root) 'GameRow.windrose'
        if ($wr) { Invoke-Node $wr; Wait-Idle 4000 }
        Assert-OnGameView (Get-Tree $root)
        Invoke-Node (Find-ById (Get-Tree $root) 'RefreshButton'); Wait-Idle 4000
        Assert-NoModal $root

        $rows = @($aliasProbes.Keys | ForEach-Object { Find-AliasRow $_ })
        Assert-True ($null -ne $rows[0]) "no ModRow.B4AliasProbe after reload"
        Assert-True ($null -ne $rows[1]) "no 'ModRow.B4AliasProbe ' (trailing space) after reload - the id may be trimmed, or the two collapsed into one row"
        Assert-True (-not [System.Windows.Automation.Automation]::Compare($rows[0], $rows[1])) "both ids resolved to the SAME element"

        foreach ($n in $aliasProbes.Keys) {
            $tog = Find-AliasToggle $n
            Assert-True ($null -ne $tog) "no toggle in the row for '$n'"
            Assert-True ((Get-ToggleState $tog) -eq 'On') "'$n' is not on to start with"
            Set-Toggle $tog; Wait-Idle 4000
            Assert-NoModal $root
            Assert-True ((Get-ToggleState (Find-AliasToggle $n)) -eq 'Off') "'$n' did not turn off"
        }

        $folders = @($aliasProbes.Keys | ForEach-Object { Get-AliasHoldingFolder $_ })
        foreach ($k in 0..1) {
            $n = @($aliasProbes.Keys)[$k]
            $heldFile = Join-Path (Join-Path $aliasDisabled $folders[$k]) $aliasProbes[$n]
            Assert-True (Test-Path -LiteralPath $heldFile) "'$n' is not held in disabled\$($folders[$k])"
            Assert-True (-not (Test-Path -LiteralPath (Join-Path $wrMods $aliasProbes[$n]))) "'$n' is still live after turning off"
        }
        $plainHeld = @(Get-ChildItem -LiteralPath (Join-Path $aliasDisabled $folders[0]) -File | Where-Object Name -ne 'meta.json')
        Assert-True ($plainHeld.Count -eq 1) "disabled\$($folders[0]) holds $($plainHeld.Count) probe file(s) - the two were merged"

        foreach ($n in $aliasProbes.Keys) {
            Set-Toggle (Find-AliasToggle $n); Wait-Idle 4000
            Assert-NoModal $root
            Assert-True ((Get-ToggleState (Find-AliasToggle $n)) -eq 'On') "'$n' did not turn back on"
        }
        foreach ($k in 0..1) {
            $n = @($aliasProbes.Keys)[$k]
            $live = Join-Path $wrMods $aliasProbes[$n]
            Assert-True (Test-Path -LiteralPath $live) "'$n' is not back in ~mods"
            Assert-True ((Get-FileHash -LiteralPath $live -Algorithm SHA256).Hash -eq $hashes[$n]) "'$n' came back with different bytes"
            Assert-True (-not (Test-Path -LiteralPath (Join-Path $aliasDisabled $folders[$k]))) "disabled\$($folders[$k]) outlived the turn-on"
        }
        "held apart in disabled\$($folders[0]) and disabled\$($folders[1]); both back byte-identical, both holding folders gone"
    }
    finally {
        # Every path, a failed assertion included: turn on whatever is still off, then remove the probes
        # and anything held for these two names, and reload so Windrose shows as it was found.
        foreach ($n in $aliasProbes.Keys) {
            try {
                $tog = Find-AliasToggle $n
                if ($tog -and (Get-ToggleState $tog) -eq 'Off' -and -not (Test-ModalOpen $root)) { Set-Toggle $tog; Wait-Idle 3000 }
            } catch {}
        }
        Reset-AliasProbes
        try { if (-not (Test-ModalOpen $root)) { Invoke-Node (Find-ById (Get-Tree $root) 'RefreshButton'); Wait-Idle 3000 } } catch {}
    }
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

Write-Host ''
Write-Host '  -- registration repair (Check setup) --' -ForegroundColor White

# The GAME // SETUP dialog edits a game's registration and can move its launcher data. On a REAL game
# these cases only open, read and close it - by AutomationId CloseButton, never by the name 'Close',
# which the window's own close button also carries - and assert games.json did not change. Anything
# that types runs on a throwaway fixture game registered here and removed in a finally.
$gamesJson = Join-Path $env:APPDATA 'ModManagerBuilder\games.json'
function Get-GamesHash { (Get-FileHash $gamesJson -Algorithm SHA256).Hash }

function Open-GameById([string]$Id) {
    $h = Find-ById (Get-Tree $root) 'HomeButton'
    if ($h) { Invoke-Node $h; Wait-Idle 2500 }
    $null = Test-RowPresent (Get-Tree $root) "GameRow.$Id"
    $row = Find-ById (Get-Tree $root) "GameRow.$Id"
    if (-not $row) { throw "SKIP: no GameRow.$Id on this machine" }
    Invoke-Node $row; Wait-Idle 5000
    Assert-OnGameView (Get-Tree $root)
}

function Open-CheckSetup {
    $opts = Find-ById (Get-Tree $root) 'GameOptionsButton'
    Assert-True ($null -ne $opts) "no GameOptionsButton"
    try { Expand-Node $opts } catch { Invoke-Node $opts }
    Wait-Idle 1200
    $item = Find-ById (Get-Tree $root) 'MenuCheckSetup'
    Assert-True ($null -ne $item) "no MenuCheckSetup in the More menu"
    Invoke-Node $item; Wait-Idle 2500
    Assert-True ($null -ne (Find-ByName (Get-Tree $root) 'Edit setup…')) "Check setup did not open the setup dialog"
}

function Close-SetupDialog {
    $c = Find-ById (Get-Tree $root) 'CloseButton'
    if ($c) { Invoke-Node $c; Wait-Idle 2000 }
}

# The diagnosis grid, as the text a user reads: the value that follows a label.
function Get-SetupValue([string]$Label) {
    $texts = @(Get-Tree $root | ForEach-Object {
        try { if ($_.Current.ControlType.ProgrammaticName -eq 'ControlType.Text') { $_.Current.Name } } catch {} })
    $i = [array]::IndexOf($texts, $Label)
    if ($i -lt 0 -or $i + 1 -ge $texts.Count) { return $null }
    return $texts[$i + 1]
}

function Get-SetupTexts {
    @(Get-Tree $root | ForEach-Object { try { $_.Current.Name } catch { '' } }) | Where-Object { $_ }
}

function Open-SetupEditor {
    $x = Find-ByName (Get-Tree $root) 'Edit setup…'
    Assert-True ($null -ne $x) "no Edit setup expander"
    try { Expand-Node $x } catch { Invoke-Node $x }
    Wait-Idle 1200
}

function Get-BoxValue([string]$Id) {
    (Find-ById (Get-Tree $root) $Id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Test-SaveEnabled {
    $p = Find-ById (Get-Tree $root) 'PrimaryButton'
    if (-not $p) { return $false }
    return [bool]$p.Current.IsEnabled
}

# A throwaway UE-pak game: three inert paks in a fake layout under artifacts\, registered through the
# MCP server's register_game (the app's own add path), and removed again by the app's Remove this game.
$fixtureRoot = Join-Path $repo 'artifacts\smoke\repair-fixture'
$fixtureId = 'repair-harness-fixture'
$mcpExe = Join-Path $repo 'src/ModManager.Mcp/bin/Debug/net10.0/ModManager.Mcp.exe'

function Invoke-McpTool([string]$Tool, [hashtable]$Arguments) {
    if (-not (Test-Path $mcpExe)) { throw "SKIP: build src/ModManager.Mcp (Debug) first - the fixture registers through it" }
    $psi = New-Object System.Diagnostics.ProcessStartInfo $mcpExe
    # stderr carries the server's own logging; drained and dropped so it neither floods the report nor
    # fills the pipe and stalls the server.
    $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::Start($psi)
    $p.BeginErrorReadLine()
    try {
        $send = { param($o) $p.StandardInput.WriteLine(($o | ConvertTo-Json -Depth 10 -Compress)); $p.StandardInput.Flush() }
        $read = { param($id) while ($true) { $l = $p.StandardOutput.ReadLine(); if ($null -eq $l) { throw "MCP server closed" }
                  try { $m = $l | ConvertFrom-Json } catch { continue }; if ($m.id -eq $id) { return $m } } }
        & $send @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'smoke-run'; version = '1' } } }
        $null = & $read 1
        & $send @{ jsonrpc = '2.0'; method = 'notifications/initialized' }
        & $send @{ jsonrpc = '2.0'; id = 2; method = 'tools/call'; params = @{ name = $Tool; arguments = $Arguments } }
        return ((& $read 2).result.content | Select-Object -First 1).text | ConvertFrom-Json
    }
    finally { try { $p.StandardInput.Close() } catch {}; if (-not $p.WaitForExit(5000)) { $p.Kill() } }
}

function New-RepairFixture {
    Remove-RepairFixtureFiles
    $mods = Join-Path $fixtureRoot 'FixtureGame\FixtureGame\Content\Paks\~mods'
    New-Item -ItemType Directory -Force -Path $mods | Out-Null
    1..3 | ForEach-Object { Set-Content -LiteralPath (Join-Path $mods "RepairFixture$($_)_P.pak") -Value "SMOKE626 inert $_" -Encoding ascii }
    $r = Invoke-McpTool 'register_game' @{ name = 'Repair Harness Fixture'; gameRoot = (Join-Path $fixtureRoot 'FixtureGame'); engine = 'ue-pak' }
    Assert-True ($r.ok -and $r.gameId -eq $fixtureId) "fixture registration failed: $($r | ConvertTo-Json -Compress)"
    Open-GameById $fixtureId
}

function Remove-RepairFixtureFiles {
    # ONLY the fixture's own folder (its _626mods data dir lives inside it, beside the fake game).
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

function Remove-RepairFixture {
    try {
        if (Test-ModalOpen $root) { Close-SetupDialog }
        $h = Find-ById (Get-Tree $root) 'HomeButton'
        if ($h) { Invoke-Node $h; Wait-Idle 2500 }
        $row = Find-ById (Get-Tree $root) "GameRow.$fixtureId"
        if ($row) {
            Invoke-Node $row; Wait-Idle 4000
            $opts = Find-ById (Get-Tree $root) 'GameOptionsButton'
            try { Expand-Node $opts } catch { Invoke-Node $opts }
            Wait-Idle 1200
            Invoke-Node (Find-ById (Get-Tree $root) 'MenuRemoveGame'); Wait-Idle 1500
            $d = Get-ContentDialog $root 'Remove game?' -ButtonName 'Remove'
            $rb = @(Get-Tree $d | Where-Object { try { $_.Current.Name -eq 'Remove' } catch { $false } })[0]
            Invoke-Node $rb; Wait-Idle 3000
        }
    }
    finally {
        Remove-RepairFixtureFiles
        $still = ((Get-Content $gamesJson -Raw | ConvertFrom-Json).games | Where-Object id -eq $fixtureId)
        if ($still) { Write-Host "  !! $fixtureId is still registered - remove it with More > Remove this game" -ForegroundColor Red }
    }
}

Case 'repair-elden-ring-reads-healthy' 'PR (feat/registration-repair-ui) step 1' {
    # Read-only. The dialog must not imply a repair on a working install: Elden Ring's mods load by
    # direct-inject while its registration describes a Mod Engine 2 folder, and that is drift, not damage.
    Open-GameById 'elden-ring'
    Assert-True ($null -eq (Find-ById (Get-Tree $root) 'StateChip.setup-drift')) "the SETUP chip is showing on a working install"
    $h0 = Get-GamesHash
    try {
        Open-CheckSetup
        $mods = Get-SetupValue 'Mods found'
        $loaded = Get-SetupValue 'Loaded by'
        $verdict = (Get-SetupTexts | Where-Object { $_ -like '*drift, not damage*' } | Select-Object -First 1)
        $look = Get-SetupValue 'Set to look in'
        $stored = @(((Get-Content $gamesJson -Raw | ConvertFrom-Json).games | Where-Object id -eq 'elden-ring').modLocations)[0].path
        Assert-True ($mods -and $mods -ne 'None.') "Mods found reads '$mods'"
        Assert-True ($loaded -like '*Elden Mod Loader*') "Loaded by reads '$loaded'"
        Assert-True ($null -ne $verdict) "no verdict saying the drift is not damage"
        # B1: the registration's own location, even when the game definition corrected its path
        # (stored 'mod', effective 'mods'), is declared - never "added by the launcher".
        Assert-True ($look -and $look -notlike '*added by the launcher*') "Set to look in attributes Elden Ring's own folder to the launcher: '$look'"
        Assert-True (-not $look.Contains('  ')) "Set to look in has a double space: '$look'"
        Assert-True ((-not $stored) -or $look.Contains([string]$stored)) "Set to look in never names the stored path '$stored': '$look'"
        Assert-True (-not (Test-SaveEnabled)) "Save is enabled with nothing changed"
        "'$mods' loaded by '$loaded'; look-in '$look'; verdict says drift, not damage; no SETUP chip"
    }
    finally {
        Close-SetupDialog
        Assert-True ((Get-GamesHash) -eq $h0) "games.json changed while Elden Ring's setup was only read"
    }
}

Case 'repair-windrose-location-count-readonly' 'PR (feat/registration-repair-ui) step 8' {
    # Read-only. The count comes from games.json, never from the row above it: the editor rebuilds
    # the location list, and an earlier revision silently dropped locations 2 and 3.
    $declared = @(((Get-Content $gamesJson -Raw | ConvertFrom-Json).games | Where-Object id -eq 'windrose').modLocations).Count
    if ($declared -lt 2) { throw "SKIP: Windrose declares $declared mod location(s) here - needs two or more" }
    Open-GameById 'windrose'
    $h0 = Get-GamesHash
    try {
        Open-CheckSetup
        $look = Get-SetupValue 'Set to look in'
        Open-SetupEditor
        $label = (Get-SetupTexts | Where-Object { $_ -like 'Mod folder (relative*' } | Select-Object -First 1)
        $picker = Find-ById (Get-Tree $root) 'SetupModLocationBox'
        Assert-True ($null -ne $picker) "no SetupModLocationBox for a game with $declared locations"
        $items = Get-ItemCount $picker
        Assert-True ($items -eq $declared) "the picker offers $items locations; games.json declares $declared"
        Assert-True ($label -match "has $declared;") "the label does not name $declared locations: '$label'"
        Assert-True (-not (Test-SaveEnabled)) "Save is enabled with nothing changed"
        "games.json declares $declared; picker offers $items; label '$label'; look-in '$look'"
    }
    finally {
        Close-SetupDialog
        Assert-True ((Get-GamesHash) -eq $h0) "games.json changed while Windrose's setup was only read"
    }
}

Case 'repair-cancel-is-inert' 'PR (feat/registration-repair-ui) step 7' {
    # Planning reads the filesystem on every pause in typing and must never write. Type into several
    # fields - the folder one included, so a data-dir move gets planned - then Close.
    New-RepairFixture
    try {
        $h0 = Get-GamesHash
        $files0 = @(Get-ChildItem -LiteralPath $fixtureRoot -Recurse -Force -File | ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" })
        Open-CheckSetup
        Open-SetupEditor
        Set-EditValue (Find-ById (Get-Tree $root) 'SetupNameBox') 'Cancel Probe Name'; Wait-Idle 600
        Set-EditValue (Find-ById (Get-Tree $root) 'SetupFolderBox') $env:TEMP; Wait-Idle 600
        Set-EditValue (Find-ById (Get-Tree $root) 'SetupExtensionsBox') 'pak, zip'; Wait-Idle 600
        Set-EditValue (Find-ById (Get-Tree $root) 'SetupSteamAppIdBox') '999999'; Wait-Idle 1500
        $planned = @(Get-SetupTexts | Where-Object { $_ -like '*ask whether to move*' }).Count
        Close-SetupDialog
        Assert-True (-not (Test-ModalOpen $root)) "a dialog is still open after Close"
        Assert-True ((Get-GamesHash) -eq $h0) "games.json changed after Close"
        $files1 = @(Get-ChildItem -LiteralPath $fixtureRoot -Recurse -Force -File | ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" })
        Assert-True (@(Compare-Object $files0 $files1).Count -eq 0) "the fixture's files changed after Close"
        "typed four fields (a move was planned: $($planned -gt 0)), closed; games.json and $($files0.Count) files unchanged"
    }
    finally { Remove-RepairFixture }
}

Case 'repair-save-gating' 'PR (feat/registration-repair-ui) step 3' {
    # Nothing changed: Save off. A blank game folder or a blank mod folder: Save off AND the reason
    # on screen. Put the value back: Save off again, because there is nothing to save.
    New-RepairFixture
    try {
        $h0 = Get-GamesHash
        Open-CheckSetup
        Open-SetupEditor
        Assert-True (-not (Test-SaveEnabled)) "Save is enabled with nothing changed"

        $folder = Get-BoxValue 'SetupFolderBox'
        Set-EditValue (Find-ById (Get-Tree $root) 'SetupFolderBox') ''; Wait-Idle 1200
        Assert-True (-not (Test-SaveEnabled)) "Save is enabled with a blank game folder"
        Assert-True (@(Get-SetupTexts | Where-Object { $_ -like 'A game folder is required*' }).Count -gt 0) "a blank game folder shows no reason"
        Set-EditValue (Find-ById (Get-Tree $root) 'SetupFolderBox') $folder; Wait-Idle 1200

        $mp = Get-BoxValue 'SetupModPathBox'
        Set-EditValue (Find-ById (Get-Tree $root) 'SetupModPathBox') ''; Wait-Idle 1200
        Assert-True (-not (Test-SaveEnabled)) "Save is enabled with a blank mod folder"
        Assert-True (@(Get-SetupTexts | Where-Object { $_ -like "A mod folder can't be blank*" }).Count -gt 0) "a blank mod folder shows no reason"
        Set-EditValue (Find-ById (Get-Tree $root) 'SetupModPathBox') $mp; Wait-Idle 1200
        Assert-True (-not (Test-SaveEnabled)) "Save is enabled after every field went back to its stored value"

        Set-EditValue (Find-ById (Get-Tree $root) 'SetupGroupingBox') 'filename_no_ext'; Wait-Idle 1200
        Assert-True (Test-SaveEnabled) "Save stays disabled on a real change"
        Close-SetupDialog
        Assert-True ((Get-GamesHash) -eq $h0) "games.json changed without a save"
        "off unchanged; off + reason for a blank folder and a blank mod folder; off when restored; on for a real change"
    }
    finally { Remove-RepairFixture }
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
# Retired entries are prose sections kept as history after triage; another case carries each one's
# verdict, so counting them would inflate the total with work that does not exist.
$live      = @($catalog.cases | Where-Object { $_.coverage -ne 'retired' })
$total     = $live.Count
$untriaged = @($live | Where-Object { $_.coverage -eq 'untriaged' }).Count
$agentable = @($live | Where-Object { $_.coverage -eq 'agentable' })

Write-Host ''
Write-Host '  ============================================' -ForegroundColor Cyan
Write-Host ("   {0} verified, {1} failed, {2} require a human" -f $pass, $fail, $human) -ForegroundColor Cyan
Write-Host ("   {0} of {1} catalogue cases were executed" -f ($pass + $fail), $total) -ForegroundColor Cyan
if ($untriaged -gt 0) {
    Write-Host ("   {0} still awaiting triage - neither run nor claimed" -f $untriaged) -ForegroundColor DarkYellow
}
if ($agentable.Count -gt 0) {
    Write-Host ("   {0} could run here but have no harness case yet - neither run nor claimed" -f $agentable.Count) -ForegroundColor DarkYellow
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
if ($agentable.Count -gt 0) {
    Write-Host ''
    Write-Host '  AGENTABLE, NOT YET IN THE HARNESS (not covered by any green above):' -ForegroundColor DarkYellow
    $agentable | ForEach-Object { Write-Host "   $($_.id) - $($_.title)" -ForegroundColor DarkYellow }
}

$json = Join-Path $OutDir 'results.json'
$script:Results | ConvertTo-Json -Depth 4 | Set-Content $json -Encoding UTF8
Write-Host ''
Write-Host "  results: $json" -ForegroundColor DarkGray
Write-Host "  shots:   $OutDir" -ForegroundColor DarkGray
Write-Host ''
if ($fail -gt 0) { exit 1 }
