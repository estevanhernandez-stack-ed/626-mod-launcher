<#
.SYNOPSIS
    Where the launcher stands right now: what it does, what verifies it, and what is open against it.

.DESCRIPTION
    A GENERATOR, not a document. The repo's own rule says it:

        "Don't write a snapshot list (current sprint / recent decisions / what's in flight) into this
         file. Snapshots rot. Describe how to find state."

    Written after a day that proved it three times over. Three cases were marked "needs a human" for
    reasons that were TRUE when written and had quietly stopped being true - a Vortex fixture nobody
    noticed was already installed, a view that turned out fully drivable, a game launch that was three
    questions wearing one label. Nobody had lied; the notes had simply outlived their causes.

    So this reads the sources that cannot drift instead:
      - docs/smoke-tests/smoke.json   what is verified, by whom, when   (guarded by SmokeCatalogueTests)
      - scripts/smoke-run.ps1         what a harness actually executes  (guarded by the same tests)
      - docs/2026-08-05-backlog.md    what is known broken
      - artifacts/smoke/results.json  the last run, if there was one

    Run it. Do not transcribe it.

.PARAMETER Markdown
    Emit markdown instead of console text - for pasting into a PR or an issue.
#>
[CmdletBinding()]
param([switch]$Markdown)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$catalogPath = Join-Path $repo 'docs/smoke-tests/smoke.json'
$backlogPath = Join-Path $repo 'docs/2026-08-05-backlog.md'
$resultsPath = Join-Path $repo 'artifacts/smoke/results.json'

if (-not (Test-Path $catalogPath)) { throw "no smoke catalogue at $catalogPath" }
$catalog = Get-Content $catalogPath -Raw | ConvertFrom-Json
$cases = @($catalog.cases)

# ---------------------------------------------------------------- verification, by kind
$byStatus = $cases | Group-Object status | Sort-Object Count -Descending
$byCoverage = $cases | Group-Object coverage | Sort-Object Count -Descending

# Who actually stands behind the verified ones. This is the number that matters: "verified" without a
# witness is the unchecked-box ambiguity in a nicer format.
$verified = @($cases | Where-Object { $_.status -eq 'verified' })
$byWitness = $verified | ForEach-Object {
    $by = if ($_.lastVerified -and $_.lastVerified.by) { $_.lastVerified.by } else { '(unrecorded)' }
    [pscustomobject]@{ Witness = $by }
} | Group-Object Witness | Sort-Object Count -Descending

# ---------------------------------------------------------------- what is open against the app
$open = @()
if (Test-Path $backlogPath) {
    foreach ($line in (Get-Content $backlogPath)) {
        # A heading carrying DONE is closed. The first cut of this listed A10 and A13 as open on the
        # day they shipped - a report that overstates what is broken gets ignored exactly as fast as
        # one that understates it.
        if ($line -match '^###\s+([A-E]\d+)\.\s+(.+?)\s+·' -and $line -notmatch 'DONE') {
            $open += [pscustomobject]@{ Id = $Matches[1]; Title = $Matches[2] }
        }
    }
}

# ---------------------------------------------------------------- the last harness run
$run = $null
if (Test-Path $resultsPath) {
    $r = Get-Content $resultsPath -Raw | ConvertFrom-Json
    $run = [pscustomobject]@{
        When  = (Get-Item $resultsPath).LastWriteTime
        Pass  = @($r | Where-Object Status -eq 'PASS').Count
        Fail  = @($r | Where-Object Status -eq 'FAIL').Count
        Human = @($r | Where-Object Status -eq 'NEEDS-HUMAN').Count
    }
}

# ---------------------------------------------------------------- emit
function Line($s) { if ($Markdown) { Write-Output $s } else { Write-Host $s } }
function Head($s) {
    if ($Markdown) { Write-Output ''; Write-Output "## $s"; Write-Output '' }
    else { Write-Host ''; Write-Host "  $s" -ForegroundColor Cyan }
}

if ($Markdown) { Write-Output "# Where the launcher stands"; Write-Output ''; Write-Output ("Generated {0}. Do not paste this into a document - re-run it." -f (Get-Date -Format 'yyyy-MM-dd HH:mm')) }
else { Write-Host ''; Write-Host '  WHERE THE LAUNCHER STANDS' -ForegroundColor White; Write-Host ("  generated {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm')) -ForegroundColor DarkGray }

Head 'Verification'
Line ("  {0} catalogue cases" -f $cases.Count)
foreach ($g in $byStatus) { Line ("    {0,-10} {1}" -f $g.Name, $g.Count) }
Line ''
Line '  how each is reached:'
foreach ($g in $byCoverage) { Line ("    {0,-10} {1}" -f $g.Name, $g.Count) }

Head 'Who stands behind the verified cases'
Line '  "verified" with no witness is an unchecked box in a nicer format.'
Line ''
foreach ($g in $byWitness) { Line ("    {0,-46} {1}" -f $g.Name, $g.Count) }

Head 'What only a person can do'
$human = @($cases | Where-Object { $_.coverage -eq 'human' -and $_.status -ne 'obsolete' })
Line ("  {0} cases, and the list should never reach zero - if it does, it is likelier someone deleted" -f $human.Count)
Line '  the awkward entries than that a harness learned to play a game.'
Line ''
foreach ($c in ($cases | Where-Object { $_.id -in @('ban-risk-ack-gate', 'nexus-download-endorse', 'vanilla-vs-modded', 'bnd4-save-walk') })) {
    Line ("    {0,-24} {1}" -f $c.id, $c.humanReason)
}

Head 'Last harness run'
if ($run) {
    Line ("  {0}" -f $run.When)
    Line ("    {0} passed, {1} failed, {2} require a human" -f $run.Pass, $run.Fail, $run.Human)
    Line ("    {0} of {1} catalogue cases executed" -f ($run.Pass + $run.Fail), $cases.Count)
    if ($run.Fail -gt 0) { Line '    FAILURES PRESENT - read artifacts/smoke/results.json' }
} else {
    Line '  never run on this machine (or artifacts/smoke was cleaned).'
    Line '  pwsh scripts/smoke-run.ps1'
}

Head 'Known open against the app'
Line ("  {0} entries in the backlog. Open one before assuming a surface is healthy." -f $open.Count)
Line ''
foreach ($o in ($open | Select-Object -First 12)) { Line ("    {0,-5} {1}" -f $o.Id, $o.Title) }
if ($open.Count -gt 12) { Line ("    ... and {0} more in docs/2026-08-05-backlog.md" -f ($open.Count - 12)) }

Head 'Sources'
Line '  docs/smoke-tests/smoke.json      what is verified, by whom, when (SmokeCatalogueTests guards it)'
Line '  scripts/smoke-run.ps1            what a harness actually executes'
Line '  docs/2026-08-05-backlog.md       what is known broken'
Line '  artifacts/smoke/results.json     the last run'
Line ''
Line '  All four are checked in and none of them is a summary. If this report is wrong, one of them is'
Line '  wrong, and that is a bug you can fix rather than a document you have to remember to edit.'
Line ''
