# Rule: validate-then-extract for intake

## The pattern

Any code that takes a user-supplied archive and writes its contents to the game folder follows this order:

1. **Open** the archive (read-only, no writes yet)
2. **Enumerate** entries — collect filenames + sizes + relative paths
3. **Validate** against the forbidden-paths gate (game executable, anti-cheat files, save folders the game owns, paths that escape the destination via `..`)
4. **Classify** — what kind of payload is this (framework? direct-inject mod? Lua mod? save mod?)
5. **Plan** the install — destination dir, file list, overwrite policy, snapshot list
6. **THEN extract** — only after all of the above passes

If any step 1–5 fails, the game folder is untouched. Step 6 is the only step that writes.

## Why

Two failure modes the law-and-order avoids:

- **Partial-extract corruption.** Half-installed framework leaves the game in a broken state with no clean rollback. The user power-cycled mid-extract or the disk filled or the archive was malformed at the tail. If validation happened first, the user gets a clear "this archive looks broken / unsafe — nothing was changed" message instead.
- **Forbidden-path damage.** Archive contains `../../Windows/System32/something.dll` (path-traversal attack) or `eldenring.exe` (game executable replacement, which AC flags). Extracting first means the damage is already done by the time you notice. Validating first means the user gets a clear refusal.

## How — reference implementation

`src/ModManager.Core/Frameworks/FrameworkInstaller.Install` is the canonical reference. Read it before writing a new intake site.

The shape (paraphrased):

```csharp
public InstallResult Install(string archivePath, string gameRoot, KnownFramework framework)
{
    // Step 1 + 2: open + enumerate (read-only)
    using var archive = ArchiveReader.Open(archivePath);
    var entries = archive.Entries.ToList();

    // Step 3: validate
    var forbidden = entries
        .Where(e => IsForbiddenPath(e.RelativePath, gameRoot))
        .ToList();
    if (forbidden.Any())
        return InstallResult.Refused(forbidden);

    // Step 4: classify
    if (!framework.Matches(entries))
        return InstallResult.WrongFramework();

    // Step 5: plan
    var plan = BuildInstallPlan(entries, framework, gameRoot);
    if (!plan.Validate(out var planError))
        return InstallResult.Invalid(planError);

    // Step 6: extract (now and only now do we write)
    return ExtractAtomically(archive, plan);
}
```

## The forbidden-paths gate

A path is forbidden if any of these are true:

- It contains `..` segments (path-traversal)
- It is absolute (relative paths only — destination is decided by the planner)
- It targets the game executable or any file the game owns at runtime (anti-cheat surfaces)
- It targets a save folder the game manages (the launcher snapshots before save-tree writes; intake doesn't get to do that on its own)
- It targets a folder another tool claims (Vortex / MO2 manifests — see `VortexManifest`, `ToolOwnership`)
- It escapes the destination directory when resolved relative to the planned install root

The gate lives in (or near) the installer for each intake surface. Don't reimplement it inconsistently — if you need it, extract the helper.

## Atomic extraction (step 6)

Even step 6 is reversible — extract to a temp / staging directory under the destination, then move-in. If the move fails partway, the staging dir is the rollback surface.

For framework intake specifically: `FrameworkRegistry` records the install manifest (file list, snapshot pointers) so an Uninstall can reverse it deterministically.

## The test

Every intake site ships with at least these test cases:

- **Happy path** — clean archive installs successfully
- **Forbidden path** — archive with `..` segment is refused, game folder unchanged
- **Wrong framework** — archive that doesn't match the catalog signature returns `WrongFramework` without writing
- **Partial-extract failure** — simulate a write failure mid-extract (mock filesystem) and assert rollback to clean state
- **Round-trip** — install then uninstall, assert game folder matches pre-install state byte-for-byte

The reversibility-auditor agent (`.claude/agents/reversibility-auditor.md`) flags intake sites that skip any of these.

## Surfaces this rule governs

- `FrameworkInstaller.Install` (the reference)
- `SaveModInstaller`
- `Intake` + `IntakePlan`
- `DirectInject` install paths
- Anything new with a method like `Install(archive, target, ...)` or `Apply(payload, ...)` that touches the game folder
