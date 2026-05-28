# Stop hook: if any src/ModManager.Core/ file changed this session, run the CorePurityTests
# filter and surface failures to Claude as a blocking message. Keeps the pure-core / thin-shell
# law honest without the round-trip of running the full test suite.
#
# Wired in .claude/settings.json under hooks.Stop.

$ErrorActionPreference = 'Stop'

# Detect Core changes vs HEAD (uncommitted + staged).
$coreChanges = git status --porcelain -- src/ModManager.Core/ 2>$null
if (-not $coreChanges) {
    # Nothing in Core changed — silent exit.
    exit 0
}

# Run the purity test filter only. Fast enough to not be annoying at end-of-turn.
$testProject = 'tests/ModManager.Tests/ModManager.Tests.csproj'
if (-not (Test-Path $testProject)) {
    Write-Error "check-core-purity: test project not found at $testProject"
    exit 0  # don't block on environment issues
}

$output = dotnet test $testProject `
    --filter 'FullyQualifiedName~CorePurityTests' `
    --configuration Release `
    --nologo `
    --logger 'console;verbosity=minimal' 2>&1

if ($LASTEXITCODE -ne 0) {
    # Block with the failure message. Claude sees this and can fix before the user follows up.
    $msg = @"
CorePurityTests failed after Core changes this turn.

$($output -join "`n")

Fix: introduce an interface in Core, implement the platform-specific adapter in
src/ModManager.App/Services/. See .claude/agents/core-purity-reviewer.md for the workflow.
"@
    Write-Error $msg
    exit 2
}

exit 0
