# Run Brief: patch-day

## Ask
`/vibe-cartographer:take docs/superpowers/specs/2026-10-16-patch-day-design.md`, after "make sure my vibe-cart plugin is updated and use it". Unattended confirmed: "yes" (2026-10-16, 22:18 local).

## Source
- **Kind:** design spec (plan file)
- **Location:** `docs/superpowers/specs/2026-10-16-patch-day-design.md` on branch `docs/patch-day-spec` (PR #404, not yet merged to master). Read it with `git -C C:/Users/estev/Projects/626-mod-launcher show docs/patch-day-spec:docs/superpowers/specs/2026-10-16-patch-day-design.md`.

## Intent
Patch Day works end to end in the launcher: after a Steam patch that leaves a version-locked loader older than the game, the player is told why and offered a one-click way to keep playing, with every file change a button they press and an automatic restore point taken first.

## Done when
- `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` passes on `cart/patch-day` with the new Patch Day tests in it.
- `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` succeeds on `cart/patch-day`.
- The Play gate (item 6) is built and checked on its own branch, waiting for review.
- The feed CI schedule change (item 11) is written up, not built.

## Out of scope
- Running the smoke harness against real games: it toggles real mods (memory: smoke runs touch real mods). Morning task.
- Any push, PR, or change in the `626-game-manifest` repo.
- Nexus-hosted loader watch (Elden Mod Loader) and tray notifications: spec says not in this cut.

## Inferred
- Invoking /take on the spec counts as Este's approval of the written spec (from: the message itself, after the spec-review handoff).
- Feed release data ships as a new top-level `loaderReleases` list keyed by loader name, not `latestRelease` on `loaders[]` (from: the embedded feed holds only mod-engine-2 and seamless-coop loaders; REFramework/UE4SS/Elden Mod Loader are compiled catalog rows). Dashboard decision C6WDauZAJxuMKCGNjKRD.
- The automatic restore point is a new capture-only single-game path, not Safe Clear (from: `RestorePointOrchestrator.SafeClearAsync` walks every game and always applies an end state). Dashboard decision yg7m3kIqUOZYV4EP04EB.
- While PATCH DAY shows, both UPDATED and OLD LOADER hide (from: spec §2 folds the banner in; OLD LOADER would repeat the same sentence).
- Item 6 is `hold` because it sits on the Play path beside the ban-risk gate (from: CLAUDE.md "Don't auto-force mods onto a ban-risk game"; stop-class default for access gating).

## Mode
- **Mode:** Unattended
- **Triggered by:** builder confirmed a recommendation (late window 22:18, 11 items)
