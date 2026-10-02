# Build Checklist — B4 stage two: toggle a mod across all of its trees

**Blueprint:** [`docs/superpowers/specs/2026-10-02-one-mod-many-trees-design.md`](../specs/2026-10-02-one-mod-many-trees-design.md) → *Stage two: toggle*
**Branch:** `feat/b4-multi-tree-toggle` off master `8490d0e`
**Hand-off:** draft PR → `/code-review --comment` → fix and resolve every thread → SendMessage `B4 READY PR<n> (<sha>)` to the cloud session "Repository setup", which does the final check and the merge.

## Build preferences

| | |
|---|---|
| Build mode | Autonomous (Este, 2026-10-02: "full autonomous mode"), executed with superpowers subagent-driven development |
| Verification | No checkpoints. A summary at the end. Every item is still test-first, and the suite must be green before its commit. |
| Git cadence | One commit per item, conventional commits (`feat(toggle)`, `test(toggle)`, `feat(row)`, `docs(b4)`) |
| Stay out of | `src/ModManager.Mcp/` and the MCP tool tests (the cloud session's E1) |
| Commands | `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` only. Never a bare `dotnet build` / `dotnet test` at the root. App: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.23.0` |

## Sequencing

Core first, leaves first:
1. What may move (pure selection).
2. Where it is held (layout).
3. The disable move, then the enable move. Each needs the two before it.
4. One test proving every caller reaches it.

Only then the row, which only reads Core. The live check comes after the row, because it needs both.
The riskiest item (4, a failure partway through, with rollback) sits in the middle, so there is room
to rework it.

## Items

- [ ] **1. A context knows its game's extra trees, and `ModTrees` names the entries that may move**
  Spec ref: `Stage two > 1. Which trees are safe to move`, `2. A mod's files versus a framework's`
  What to build:
  - Add `IReadOnlyList<string>? ExtraModTrees { get; init; }` to `GameContext`, filled where `Scanner.GameContext(...)` builds the context, from `ManifestIdLookup.EntryFor(game)?.ExtraModTrees`. Add an overload or an optional parameter so tests can supply the trees directly, without a feed.
  - In `ModTrees`, keep a record of every entry, not only each tree. Add `MovableFor(string modName, IEnumerable<string> otherRowNames, Func<string,bool> isOwned)`. It returns `(Tree, EntryName, AbsPath)` for every top-level entry whose key equals the mod's, and returns nothing at all when another row name has the same key.
  - An entry is held back when it is, or holds, another declared tree or one of the game's own mod folders, or when its tree is tool-owned.
  - `For` is unchanged.
  Acceptance: Unit tests in `tests/ModManager.Tests/ModTreesToggleTests.cs` show that:
  - movable entries come back in manifest order
  - two claimants (`Cool_Mod` and `CoolMod`) return nothing
  - an entry holding another declared tree is held back
  - a tool-owned tree is held back
  - a framework folder (`red4ext/plugins/ArchiveXL`) with no row is never returned
  - folder and file entries with the same key (`r6/scripts/CoolMod/` and `r6/tweaks/CoolMod.yaml`) are both returned
  Verify: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModTrees"` is green, and the full suite stays green.

- [ ] **2. The holding layout for extra trees**
  Spec ref: `Stage two > 3. How the holding area records it`
  What to build: an internal static `TreeHolding` in Core:
  - `Root(ctx)` is `<dataDir>/disabled-trees`, a sibling of `ctx.DisabledRoot`, and `ModDir(ctx, mod)` is `Root/<mod>`.
  - `PathFor(ctx, mod, tree, entry)` gives a held entry's path.
  - `Held(ctx, mod)` parses the layout back into `(Tree, EntryName)`. The last segment is the entry and the segments before it are the tree; it is recursive, because a tree such as `bin/x64/plugins/cyber_engine_tweaks/mods` has depth.
  - `HoldsFiles(ctx, mod)` delegates to `HoldingFolder.HoldsFiles` with no record name.
  - Removal goes through `HoldingFolder.RemoveIfNoFiles` only. There is no recursive delete.
  Acceptance: tests show that:
  - parse and `PathFor` round-trip for a single-segment tree and a five-segment tree
  - an empty or missing root yields nothing
  - `Held` recognises an entry that is a file (`r6/input/CoolMod.xml`), not only folders
  Verify: the filtered tests are green, and the full suite stays green.

- [ ] **3. Turning off moves the extra entries under one rollback**
  Spec ref: `Stage two > The operation > Off`
  What to build: in `Scanner.DisableEntry`, after the existing guards:
  - Compute the movables from item 1. `otherRowNames` comes from `BuildModList(c)`, and `isOwned` from `ToolOwnership.Resolve(...).State == Owned` against `c.TakenOver`.
  - Refuse, with `HeldCopyCollisionException` and nothing moved, when `TreeHolding.HoldsFiles` is true or any holding destination exists.
  - In phase 1, move the main files and then each extra (`SafeMove.Move`) into one moved list.
  - On failure, move everything back, extras first. When nothing is stranded, remove the `disabled-trees/<mod>` folder the call created, with `RemoveIfNoFiles`. A stranded extra stays held and is named in the message.
  - Phases 2 and 3 (`meta.json` and mirrors) are unchanged.
  Acceptance: tests show that:
  - turning off leaves every extra entry under `disabled-trees/<Mod>/<tree>/<entry>` and gone from the game
  - the main files are held as before
  - a collision with an existing held extra refuses, with nothing moved
  - a failure midway through the extras (a file held open with `FileShare.None`, or an injected mover) leaves the game tree byte-identical, writes no `meta.json`, and leaves no `disabled-trees/<Mod>`
  - a read-only row moves nothing
  Verify: the filtered tests are green, and the full suite (including `DirectInjectHeldFilesTests` and `BepInExScanTests`) stays green.

- [ ] **4. Turning on restores the extra entries under one rollback**
  Spec ref: `Stage two > The operation > On`
  What to build: in `Scanner.EnableMod`, on the holding path:
  - Read `TreeHolding.Held`, and pre-check every extra destination (`<gameRoot>/<tree>/<entry>`) together with the main pre-check, before any write. A collision throws the existing conflict error, with nothing written.
  - After the main copies succeed, move each extra back, creating the tree folder if it is gone, and track what was restored.
  - On failure, move the restored extras back to holding, then run the existing main rollback.
  - After success, tear down `disabled-trees/<mod>` with `RemoveIfNoFiles`.
  - A mod with no `disabled-trees` folder takes exactly the old path.
  Acceptance: tests show that:
  - an off-then-on round trip leaves every tree byte-identical (compare relative path and content hash before and after), with both holding folders gone
  - a collision in one extra tree refuses, nothing is written anywhere, and everything is still held
  - a mod turned off before stage two (no `disabled-trees`) turns on unchanged
  - a held entry whose tree folder was deleted is restored, with the folder recreated
  Verify: the filtered tests are green, and the full suite stays green.

- [ ] **5. Every caller reaches it: row toggle, bulk, MCP lane**
  Spec ref: `Stage two > The operation` (no second path)
  What to build: no new production code is expected. Tests only, driven through public entry points:
  - `ModToggle.SetEnabledAsync` on a Cyberpunk-shaped custom-engine fixture, off then on
  - `Scanner`'s set-all / apply-mode entry, off then on
  If either path does not reach `DisableEntry` / `EnableMod`, fix the routing in Core, never in `src/ModManager.Mcp/`.
  Acceptance: both paths move the extras and round-trip byte-identically. `ModToggle.IsApplied` agrees with the result.
  Verify: the filtered tests are green, and the full suite stays green.

- [ ] **6. The row says what moves**
  Spec ref: `Stage two > The row`
  What to build:
  - In Core, a pure helper with tests, `ModTreesText` (or a method on `ModTrees`), returning the row's line and tooltip from three inputs: the trees that will move, the trees held back, and the trees held because the mod is off. Wording:
    - **Live, moving:** `Also has files in r6/scripts, r6/tweaks`. Tooltip: "626 turns these on and off with the mod."
    - **Live, some held back:** the tooltip adds "Files in <tree> stay where they are: 626 can't tell they belong only to this mod."
    - **Off:** `Also turned off in r6/scripts, r6/tweaks`.
  - In the App, `MainViewModel` (around line 874) feeds a live row from `ModTrees` and a turned-off row from `TreeHolding.Held`. `ModRowViewModel` binds the helper's text and drops its hard-coded tooltip.
  - Any new interactive control ships with an AutomationId (`.claude/rules/automation-ids.md`); a text-only change needs none.
  Acceptance:
  - text tests cover all three states plus the empty one
  - the Debug App build passes with warnings as errors: stop the app first, then clean `obj/` and `bin/` (XAML edits leave stale generated code)
  Verify: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.23.0` succeeds with 0 warnings, and the Core suite stays green.

- [ ] **7. Live check on Este's Cyberpunk install**
  Spec ref: `Stage two > Testing` (App row checked with a Debug build and a UIA walk)
  What to build:
  - Launch the Debug build and select Cyberpunk 2077.
  - UIA-walk a row that has extra files (pick one from `r6/scripts` ∩ `archive/pc/mod`, for example `BlackChrome`) and confirm its line and tooltip.
  - Snapshot its trees (relative path + SHA-256) to the scratchpad.
  - Toggle it off through the app (UIA), confirm the trees are emptied for that mod and the holding layout is present, and confirm the row reads `Also turned off in ...`.
  - Toggle it on, and diff the snapshot: it must be identical.
  - Restore any app state in a `finally`, including on failure.
  - Add or adjust a smoke case in `docs/smoke-tests/smoke.json` for the row text when touched, plus a manual entry in `docs/smoke-tests/pending.md`.
  Acceptance: the before and after hashes are identical, the row text is verified by UIA, and Este's install is left as it was. The transcript goes into the PR body.
  Verify: the snapshot diff is empty, the UIA output shows both row states, and the mod list matches before and after (`list_mods` diff).

- [ ] **8. Documentation & security verification, PR and hand-off**
  Spec ref: `Stage two` (all), repo conventions
  What to build:
  - Mark spec stage two as built. Update the B4 backlog entry (`docs/2026-08-05-backlog.md` ~line 1367).
  - Nothing is added to the camelCase rule list, because no JSON shape is new; say so in the PR.
  - Secrets scan of the diff, and `dotnet list package --vulnerable` on Core and Tests.
  - Run the `reversibility-auditor` and `core-purity-reviewer` agents on the diff and fix their findings.
  - Push, then open a draft PR whose body has the summary, the Windows-only results from item 7, and the shared methods touched (`Scanner.DisableEntry`, `Scanner.EnableMod`, `ModTrees`, `GameContext`).
  - Run `/code-review --comment`, fix the findings, then reply to and resolve every thread.
  - Log the decision to the 626 dashboard.
  - SendMessage `B4 READY PR<n> (<sha>)`.
  Acceptance: the draft PR is open, every review thread is answered and resolved, CI is green, and the hand-off message is sent.
  Verify: `gh pr view <n> --json reviewDecision,statusCheckRollup` and `gh api graphql` show no unresolved threads.
