# Handoff: decouple the loader toggle (reshape PR #93)

**Status:** in progress on branch `feat/inline-loader-cascade` (PR #93). Rig restored (`dinput8.dll` back ON). App builds clean.

## Decision (user-confirmed via live test — do NOT re-litigate)
The loader toggle must be **DECOUPLED**, not a cascade. Toggling the loader row moves **only its own `dinput8.dll`** (via the normal per-mod path); the hosted `mods\` mods are left untouched. Live test proved: with the loader off, the hosted `mods\` mods sit **inert-but-harmless** (they don't load, but cause no crash, game launches fine). So cascading them to holding was solving a non-problem. The user asked for this directly and repeatedly.

Also confirmed: ERSS2/ReShade/regulation (root-level) load via Seamless's `ersc.dll` when the loader is off (that's PR #94, already shipped). The `mods\` mods specifically need Elden Mod Loader to *function*, but their mere presence with the loader off does not break launch.

## KEEP (the good part of #93 — loader stays visible + independently toggleable)
- `Mod.IsLoader` transient flag (Mod.cs)
- `DirectInjectListing.Enabled` un-drop (loader row no longer removed when mods\ has contents) + `Row` sets `IsLoader = d.Name == DirectInject.LoaderName`
- `ModRowViewModel.IsLoader` + `LoaderChipVisibility`
- LOADER chip in MainWindow.xaml
- `canToggle: rep.IsLoader || ...` in MainViewModel row build
- Listing/IsLoader/transient tests in DirectInjectLoaderCascadeTests.cs

## REMOVE (orphaned by decouple)
- `MainWindow.xaml.cs` OnModToggled: the `if (row.IsLoader) { ... ToggleLoaderCascadeAsync ... }` branch — DELETE it so the loader row falls through to the normal per-mod path. (On the integ branch it's already commented out; on THIS branch it's still the live cascade branch ~line 122-130.)
- `MainViewModel.ToggleLoaderCascadeAsync` (~line 582)
- `DirectInjectService.SetLoaderEnabled`
- `DirectInject.SetLoaderEnabled` + `CascadeOff` + `CascadeOn` + the `FolderFiles`/`FolderDirs`/`BaseNames` helpers added for them
- The cascade tests in DirectInjectLoaderCascadeTests.cs: off-moves, on-restores, round-trip, no-ops, slug-collision guard, stale-holding guard, loader-locked rollback, hosted-locked rollback, ON-rollback-throw, collision-skip. KEEP only: loader-row-present, hosted-not-tagged, disabled-loader-tagged, IsLoader-transient.

## Why decouple "just works" (trace)
Loader row → OnModToggled (no IsLoader branch) → falls through → `ToggleAsync(row)` → DirectInjectBacked → `_direct.SetEnabled(game, "DLL mod loader", false)` → `DirectInject.Disable(loader unit)` where the loader unit's `Entries = [dinput8.dll]` (from `Detect`, NOT the mods\ contents) → moves only dinput8.dll to holding. Re-enable restores it. Hosted mods\ untouched. That IS the decouple — no new code, just remove the cascade special-case.

## PROGRESS (2026-05-30, mid-compaction)
DONE on `feat/inline-loader-cascade`:
- OnModToggled IsLoader cascade branch REMOVED (loader now falls through to per-mod path). ✅
- MainViewModel.ToggleLoaderCascadeAsync REMOVED. ✅
- DirectInjectService.SetLoaderEnabled REMOVED. ✅

REMAINING (resume here — verify build first, tools were garbling):
- DirectInject.cs: remove `SetLoaderEnabled` + `CascadeOff` + `CascadeOn` + `FolderFiles`/`FolderDirs`/`BaseNames` helpers (block starts ~line 192 doc-comment, ends before "---------- install" section ~line 315). Currently ORPHANED (App no longer calls it; only the cascade tests do).
- DirectInjectLoaderCascadeTests.cs: delete the cascade tests (they call DirectInject.SetLoaderEnabled); KEEP loader-row-present / hosted-not-tagged / disabled-loader-tagged / IsLoader-transient (note: transient test calls SetLoaderEnabled(off) to write holding meta — rewrite it to use DirectInject.Disable on the loader unit instead).
- Intermediate state likely STILL COMPILES (Core method just unreferenced by App; tests still call it) — confirm with `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`.
- Then: update spec + plan + pending.md smoke entry (cascade → independent toggle), commit, update PR #93 title/body, log decision.

## After the edits
- Full Core suite + CorePurity green; App x64 0 errors (was 0 warnings on integ branch after decouple).
- Update the spec (2026-05-30-loaders-section-cascade-disable-design.md) + plan: cascade → independent toggle. Update the smoke entry in pending.md.
- Re-smoke: loader toggle off → only dinput8.dll moves, hosted rows still show (inert); toggle on → restored.
- Update PR #93 title/body: "inline distinguished loader row + independent toggle" (drop "cascade-disable").
- Log decision to 626 dashboard (project DP1YCsh7iAN1yAiR8sAd).

## Open PRs (8): #87 smoke-refresh, #88 amber chip, #89 Steam, #90 crash-guard, #91 saves, #92 running-guard, #93 loader row (being reshaped), #94 Seamless-proxy. Merge order: #88 before #93.
