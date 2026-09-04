# Settings -> Folders left behind: the holding folders no registered game owns.
# READ-ONLY. It never clicks Show files, Save a copy or Remove.
#
# Two things this harness learned the hard way, both worth keeping:
#  * The section sits below the fold and its rows are virtualized, so the ids realise a few at a time.
#    Accumulate across the whole scroll walk. Breaking at the first position with any rows reported
#    two of seven and called the other five missing.
#  * The row id is on the NAME TextBlock, not on the row Grid -- a Grid is a layout panel and never
#    reaches the control view. So an id-bearing element is a LABEL, not a container: look for the
#    row's buttons across the section, never as descendants of the id.
$ErrorActionPreference='Stop'
. "$PSScriptRoot\uia-lib.ps1"
$pass=0;$fail=0
function Check($n,$b){try{&$b;Write-Host "PASS  $n";$script:pass++}catch{Write-Host "FAIL  $n -- $_";$script:fail++}}

$root = Get-AppRoot; if(-not $root){throw "app not running"}; [void](Wait-Ready $root)
$opened=$false
try {
  # Guarded: a second ContentDialog.ShowAsync on one XamlRoot throws, and before MainWindow gained
  # its _settingsOpen guard that took the whole app down.
  if(-not (Find-ById (Get-Tree $root) 'SettingsGroup.leftovers')){
    Invoke-Node (Find-ById (Get-Tree $root) 'SettingsButton'); Wait-Idle 5000
    $opened=$true
  }

  $svps=@()
  foreach($n in (Get-Tree $root)){
    $p=$null
    if($n.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern,[ref]$p)){
      if($p.Current.VerticallyScrollable){ $svps += $p }
    }
  }
  if($svps.Count -eq 0){ throw "no vertically scrollable region in the settings dialog" }

  $ids  = New-Object System.Collections.Generic.HashSet[string]
  $seen = New-Object System.Collections.Generic.HashSet[string]
  foreach($svp in $svps){
    for($pct=0; $pct -le 100; $pct+=10){
      try { $svp.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, $pct) } catch { break }
      Wait-Idle 450
      $t = Get-Tree $root
      foreach($r in (Find-AllByIdPrefix $t 'Leftover.')){
        [void]$ids.Add(($r.Current.AutomationId -replace '^Leftover\.',''))
      }
      $sec = Find-ById $t 'SettingsGroup.leftovers'
      if($sec){ foreach($n in (Get-Tree $sec)){ try{ if($n.Current.Name){ [void]$seen.Add($n.Current.Name) } }catch{} } }
    }
  }
  Write-Host "      found across the scroll: $(($ids | Sort-Object) -join ', ')"

  Check 'the section is there under its stable id, with the corrected heading' {
    $sec = Find-ById (Get-Tree $root) 'SettingsGroup.leftovers'
    if(-not $sec){ throw 'SettingsGroup.leftovers not in the tree' }
    if($sec.Current.Name -ne 'Folders left behind'){ throw "heading is '$($sec.Current.Name)'" }
  }

  Check 'it lists the seven orphans and NONE of the fifteen registered games' {
    foreach($r in @('elden-ring','windrose','witchfire','cyberpunk-2077','death-stranding-2-on-the-beach',
                    'palworld','sons-of-the-forest','gas-station-simulator','content-warning','r-e-p-o',
                    'marvel-s-spider-man-2','monster-hunter-wilds','crime-simulator','big-ambitions','how-to-fish')){
      if($ids.Contains($r)){ throw "REGISTERED GAME LISTED: $r" }
    }
    foreach($e in @('demonologist','phasmophobia','ready-or-not','repo','captain-of-industry','schedule-i','marvel-s-spider-man-2-2')){
      if(-not $ids.Contains($e)){ throw "missing expected orphan: $e" }
    }
    if($ids.Count -ne 7){ throw "expected 7, got $($ids.Count)" }
  }

  Check 'every detail line states a count, a size and what is actually inside' {
    # NOT a count of rows. $seen is a set and five of these folders hold the same one file at the
    # same size, so their detail strings are byte-identical and collapse to one entry. Counting them
    # reported "only 3 rows state a file count" about a section where all seven did.
    $details = @($seen | Where-Object { $_ -match '^\d+ file' })
    if($details.Count -eq 0){ throw 'no row states a file count at all' }
    foreach($d in ($details | Sort-Object)){
      if($d -notmatch '^\d+ files? , ?|^\d+ files?,'){ throw "detail does not lead with a count: '$d'" }
      if($d -notmatch '\d+\s*(B|KB|MB|GB)'){ throw "detail states no size: '$d'" }
      if($d -notmatch '-'){ throw "detail names nothing inside: '$d'" }
      Write-Host "      $d"
    }
  }

  Check 'the detail proves these folders are not only mods' {
    # The section's whole naming argument: a folder here holds profiles and metadata too, so the row
    # has to show that rather than let "mod folder" stand for all of it.
    if(-not ($seen | Where-Object { $_ -match '\.json' })){
      throw 'no row names a non-mod file, so the "not only mods" claim is unevidenced on this machine'
    }
  }

  Check 'every row offers exactly the three actions' {
    foreach($f in $ids){
      foreach($verb in @("Show files for $f","Save a copy of $f","Remove $f")){
        if(-not $seen.Contains($verb)){ throw "missing '$verb'" }
      }
    }
  }

  Check 'no bulk action anywhere in the section' {
    foreach($bad in @('Remove all','Clear all','Delete all','Remove everything')){
      if($seen.Contains($bad)){ throw "bulk action present: $bad" }
    }
  }
}
finally {
  if($opened){ try{ $c = Find-ByName (Get-Tree $root) 'Close'
                    if(-not $c){ $c = Find-ByName (Get-Tree $root) 'Done' }
                    if($c){ Invoke-Node $c; Wait-Idle 1200 } }catch{ Write-Host "WARN close failed: $_" } }
}
Write-Host ""; Write-Host "leftovers smoke: $pass passed, $fail failed"
if($fail -gt 0){ exit 1 }
