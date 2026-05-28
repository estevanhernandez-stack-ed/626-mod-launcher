# PostToolUse hook for Edit/Write/MultiEdit: best-effort grep on the edited file
# for a likely-new System.Text.Json serializer call that doesn't set the camelCase
# policy. Warns (does NOT block) — false positives are possible and we want to
# stay out of the way on legitimate exceptions.
#
# Wired in .claude/settings.json under hooks.PostToolUse with matcher Edit|Write|MultiEdit.

$ErrorActionPreference = 'Stop'

# Read the tool-event JSON from stdin.
$raw = [Console]::In.ReadToEnd()
if (-not $raw) { exit 0 }

try {
    $event = $raw | ConvertFrom-Json
} catch {
    exit 0  # bad input, don't block
}

$filePath = $event.tool_input.file_path
if (-not $filePath) { exit 0 }
if ($filePath -notmatch '\.cs$') { exit 0 }
if (-not (Test-Path $filePath)) { exit 0 }

# Skip test files — round-trip tests intentionally exercise non-camelCase shapes.
if ($filePath -match '[/\\]tests[/\\]') { exit 0 }

$content = Get-Content $filePath -Raw
if (-not $content) { exit 0 }

# Look for serializer call sites that look new and don't have the policy nearby.
# Pattern: JsonSerializer.Serialize / Deserialize / new JsonSerializerOptions() without
# PropertyNamingPolicy = JsonNamingPolicy.CamelCase anywhere in the same file.
$hasSerializerCall = $content -match 'JsonSerializer\.(Serialize|Deserialize)\b' `
                  -or $content -match 'new\s+JsonSerializerOptions\s*\('
$hasCamelPolicy   = $content -match 'PropertyNamingPolicy\s*=\s*JsonNamingPolicy\.CamelCase'
# Allow files that delegate to AtomicJson (which handles policy itself).
$delegatesToAtomic = $content -match '\bAtomicJson\b'

if ($hasSerializerCall -and -not $hasCamelPolicy -and -not $delegatesToAtomic) {
    Write-Warning @"
check-camelcase-json: $filePath has a JsonSerializer call but no
PropertyNamingPolicy = JsonNamingPolicy.CamelCase. See .claude/rules/camelcase-json-on-disk.md
for the convention. If this file legitimately writes non-camelCase JSON (e.g., wire format for
an upstream API), ignore this warning.
"@
}

exit 0
