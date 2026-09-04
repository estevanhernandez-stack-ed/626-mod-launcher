# The Add Game quick-pick: it offers every curated game, ranks the ones you have first, and a later
# pick takes back the folder the last one filled. READ-ONLY — cancels the dialog, registers nothing.
#
# Every question here is asked through ItemContainerPattern, never by walking the rendered tree. The
# picker is virtualized: a tree walk on this machine realised 105 of 116 entries and handed back a
# window that STARTED at list index 38, so the first cut of this harness compared "the first option"
# against the middle of the list and passed while proving nothing.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\uia-lib.ps1"

$pass = 0; $fail = 0
function Check([string]$name, [scriptblock]$body) {
    try { & $body; Write-Host "PASS  $name"; $script:pass++ }
    catch { Write-Host "FAIL  $name -- $_"; $script:fail++ }
}

$root = Get-AppRoot
if (-not $root) { throw "launcher is not running" }
[void](Wait-Ready $root)

$opened = $false
try {
    $addGame = Find-ByName (Get-Tree $root) '+ Game'
    if (-not $addGame) { throw "no '+ Game' button in the tree" }
    Invoke-Node $addGame; Wait-Idle 1800
    $opened = $true

    function DialogTree { Get-Tree (Get-ContentDialog $root '*Add*game*' 'Cancel' 10) }
    function Picker     { $b = Find-ById (DialogTree) 'PopularGamesBox'
                          if (-not $b) { throw 'PopularGamesBox went missing' }; $b }
    function BoxValue($id) {
        $e = Find-ById (DialogTree) $id; if (-not $e) { return $null }
        $v = $null
        if ($e.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$v)) { return $v.Current.Value }
        return $null
    }

    $box   = Picker
    $all   = Get-ItemsInOrder $box 400          # the whole source, in true order
    $total = $all.Count
    $head  = @($all | Select-Object -First 20 | ForEach-Object { Get-ItemLabel $_ })
    function ItemNamed([string]$label) {
        Get-ItemsInOrder (Picker) 400 | Where-Object { (Get-ItemLabel $_) -eq $label } | Select-Object -First 1
    }
    # Reading the item source works with the popup shut; SELECTING does not — Select() on an item of
    # a closed ComboBox throws a bare "Unrecognized error". Open it, then realize, then select.
    function PickGame([string]$label) {
        $b = Picker
        Expand-Node $b; Wait-Idle 1000
        $it = Get-ItemsInOrder $b 400 | Where-Object { (Get-ItemLabel $_) -eq $label } | Select-Object -First 1
        if (-not $it) { throw "no option labelled '$label'" }
        Select-Realized $it
        Wait-Idle 1600
    }

    Check 'step 1: the cap is gone -- the picker is not limited to the legacy tagged set' {
        if ($total -le 18) { throw "$total options; the old popular-games tag carried 18" }
        Write-Host "      $total curated games offered"
    }

    Check 'step 1: the games on this machine lead the list' {
        Write-Host "      top of the list: $($head[0]), $($head[1]), $($head[2])"
        # A ranked-in game fills the folder box on pick; that is the observable proof of "installed".
        PickGame $head[0]
        $f = BoxValue 'FolderBox'
        if ([string]::IsNullOrEmpty($f)) {
            throw "top-ranked '$($head[0])' filled no folder, so ranking put an undetected game first"
        }
        Write-Host "      $($head[0]) -> $f"
    }

    Check 'step 2: Minecraft is offered, though it is on no store we can read' {
        if (-not (ItemNamed 'Minecraft')) { throw "Minecraft absent from $total options" }
    }

    Check 'step 4: an undetectable game is lower down, never missing' {
        if ($head -contains 'Minecraft') { throw 'Minecraft ranked as installed, which we cannot know' }
    }

    # The regression the whole-branch review found. An installed pick has just filled the folder;
    # switching to Minecraft, which can never fill it, must clear it. Before the fix the first
    # game's path stayed and registration accepted it: the wrong game root, the right manifest id.
    PickGame 'Minecraft'

    Check 'step 2: picking Minecraft fills the mod path' {
        $mp = BoxValue 'ModPathBox'
        if ($mp -ne 'mods') { throw "mod path is '$mp', expected 'mods'" }
    }
    Check 'step 2: picking Minecraft leaves the Steam app id empty, not a fake value' {
        $s = BoxValue 'SteamAppIdBox'
        if (-not [string]::IsNullOrEmpty($s)) { throw "steam id is '$s', expected empty" }
    }
    Check 'REGRESSION: a later pick takes back the folder the last one filled' {
        $f = BoxValue 'FolderBox'
        if (-not [string]::IsNullOrEmpty($f)) {
            throw "folder still reads '$f' -- that is the previously picked game's root"
        }
    }
}
finally {
    # Restore the fixture even when an assertion throws, or every later case reads as broken.
    if ($opened) {
        try {
            $c = Find-ByName (Get-Tree $root) 'Cancel'
            if ($c) { Invoke-Node $c; Wait-Idle 900 }
        } catch { Write-Host "WARN  could not close the dialog: $_" }
    }
}

Write-Host ""
Write-Host "picker smoke: $pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
