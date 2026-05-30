# Loader cascade-disable + inline distinguished row — Implementation Plan

> **For agentic workers:** built test-first (TDD). Steps use checkbox (`- [ ]`) tracking.

**Goal:** The DLL mod loader (Elden Mod Loader = `dinput8.dll`) stays visible as a distinguished inline row, and its toggle reversibly cascades the whole modded stack (loader + every `mods\*.dll` hosted mod) off/on in one atomic action.

**Spec:** [docs/superpowers/specs/2026-05-30-loaders-section-cascade-disable-design.md](../specs/2026-05-30-loaders-section-cascade-disable-design.md). Revises remediation Task 6. Derived from a multi-agent understand+adversarial-design workflow (run wf_04e6d45b-cbc).

**Branch:** `feat/inline-loader-cascade` (already cut; carries the spec commit).

---

## Architecture (decided)

One new pure-Core orchestrator `DirectInject.SetLoaderEnabled(playFolder, holdingRoot, enabled)` composes the already-tested per-unit `Disable`/`Enable` primitives under an OUTER try/catch that owns the cross-unit atomicity those primitives individually lack. Never reinvents `MoveAny`, never `File.Delete`. Plus a transient `Mod.IsLoader` flag, removal of the loader-row-drop line in `DirectInjectListing.Enabled`, and App wiring.

**Resolved forks (from adversarial synthesis):**
- **Disable order is ASYMMETRIC:** OFF disables loader FIRST; ON restores loader LAST. OFF-first because the dominant real failure is the game running with `dinput8.dll` locked — it trips on unit 0 before any hosted mod moves, so rollback is a no-op with the play folder already fully-on. ON-last closes the silent-broken window (proxy live before its mods are back).
- **No batch manifest (v1):** ON recomputes the restore set from `ListDisabled(holdingRoot)`. Per-unit `__626mod.json` reuse adds zero new on-disk shape. (Batch manifest logged as follow-up if field crashes surface.)
- **Two refuse-up-front guards:** slug-collision (two units slugging to the same holding dir would clobber each other's meta → un-restorable) and stale-holding-dir (a prior partial op's dir). Both throw before any file moves — untouched is the most-recoverable state.
- **`IsLoader` is transient:** `Mod` is never serialized anywhere (grep-confirmed — only `ModMeta` + `DisabledMeta` reach disk), so a plain `bool` engages no camelCase rule. Code comment locks the intent.

---

## Cascade-OFF algorithm

1. Detect loader: `Detect(files, dirs).FirstOrDefault(m => m.Name == LoaderName)`. Null → return (no-op).
2. Detect hosted: `DetectLoaderMods(modsDir files, dirs)` if `mods\` exists, else empty.
3. Batch = `[loader] + hosted` (LOADER FIRST).
4. **Slug-collision guard:** group batch by `Slugify(Name)`; if any group >1, throw before moving anything.
5. **Stale-holding guard:** if any `holdingRoot/<slug>/` already exists, throw before moving anything.
6. `disabled = []`; `foreach unit: Disable(playFolder, holdingRoot, unit); disabled.Add(unit.Name)`.
7. Wrap 6 in try/catch → on failure run OFF-rollback over `disabled`, then `throw InvalidOperationException($"Couldn't disable the {LoaderName} ({e.Message}) — is the game running?", e)`.

## Cascade-ON algorithm

1. `disabledUnits = ListDisabled(holdingRoot)` — snapshot now (rollback source; Enable clears holding as it goes).
2. Partition: `loaderUnit` (Name==LoaderName) + `hostedUnits` (rest). Empty → return (no-op).
3. `enabled = []`; Enable hosted FIRST, tracking successes.
4. Enable loader LAST.
5. Wrap 3-4 in try/catch → on failure run ON-rollback over `enabled` (re-Disable using the snapshot), then `throw InvalidOperationException($"Couldn't enable the {LoaderName} ({e.Message}) — is the game running?", e)`.

## Rollback (both directions)

- OFF-rollback: iterate `disabled` reverse; `try { Enable(...) } catch { /* best-effort */ }` per unit. **Each Enable MUST be individually guarded** — Enable's inner `MoveAny` re-throws `ERROR_SHARING_VIOLATION` (SafeMove.cs:22 only skips the fallback; DirectInject.cs:186 calls it unguarded), so an unguarded rollback would strand remaining units. Always re-throw after the loop.
- ON-rollback: iterate `enabled` reverse; `try { Disable(..., snapshotByName[name]) } catch {}` per unit. Re-throw after.
- The failing unit is never in the tracked list (Disable's own inner try already unwound its partial move + deleted its half-written holding dir before throwing).
- Residual honesty: a still-locked unit stays on the side it started (visible to ListDisabled or Detect) — never deleted, never half-written; next toggle re-converges. "Holding empty after rollback" asserted only on the clean lock-free path.

---

## Tasks

### Task 1 — Core: `Mod.IsLoader` transient flag
- [ ] Add `public bool IsLoader { get; set; }` to `src/ModManager.Core/Mod.cs` after `Builtin`, with comment: `// transient — Mod is never serialized; add [JsonIgnore] if a write path is ever added.`
- [ ] Build Core (`dotnet build` not needed standalone; covered by test runs).

### Task 2 — Core: surface + tag the loader row (TDD)
- [ ] RED: in a new `tests/ModManager.Tests/DirectInjectLoaderCascadeTests.cs`, write `DirectInjectListing_loader_row_present_when_hosting_mods` (FromSoftFixture.Build → `DirectInjectListing.List(game)` contains Name==LoaderName && IsLoader==true AND a hosted row "Adjust The Fov"), `..._hosted_mod_rows_not_tagged_loader`, `..._disabled_loader_row_tagged_loader`. Run → fail/compile-error.
- [ ] GREEN: `DirectInjectListing.cs` — delete the drop line (`if (loaderMods.Count > 0) top = top.Where(...)`); update the stale comment; add `IsLoader = d.Name == DirectInject.LoaderName` to `Row`'s Mod initializer.
- [ ] Run the three → pass. Check no existing test asserted the old loader-dropped behavior (fix/flip if so).

### Task 3 — Core: `SetLoaderEnabled` cascade (TDD, the load-bearing task)
- [ ] RED: add cascade tests to the same file — `_off_moves_loader_and_all_hosted_to_holding`, `_on_restores_loader_last_and_clears_holding`, `_round_trip_byte_for_byte`, `_off_no_loader_installed_is_noop`, `_on_no_holding_is_noop`. Light `IDisposable` Play/Holding fixture (mirror DirectInjectToggleTests). Run → compile-error (method absent).
- [ ] GREEN: add `DirectInject.SetLoaderEnabled(string playFolder, string holdingRoot, bool enabled)` per the algorithms above. Filename-array helper mirrors `DirectInjectListing.Names` (try/catch → Path.GetFileName list).
- [ ] Run → pass.

### Task 4 — Core: guards + rollback (TDD, the reversibility-critical task)
- [ ] RED: `_off_refuses_on_slug_collision` (two hosted mods slugging identically → throw, nothing moved), `_off_refuses_on_stale_holding_dir` (pre-create a holding slug dir → throw, nothing moved), `_off_rollback_when_hosted_mod_locked` (lock a LATER hosted DLL with `using var hold = new FileStream(path, Open, ReadWrite, FileShare.None)`; loader moves first then hosted fails → throw InvalidOperationException w/ IOException somewhere in the inner chain, play folder fully-ON, holding empty), `_off_loader_locked_is_cheap_noop_rollback` (lock dinput8.dll itself → throw, play folder UNCHANGED, holding empty), `_on_rollback_re_disables_when_locked` (disable all, lock a play dest, Enable fails mid-restore → throw, state back to fully-OFF). Run → fail.
- [ ] GREEN: implement the guards (steps 4-5 OFF) + the OFF/ON rollback loops. Run → pass.

### Task 5 — Core: transient guard test
- [ ] `Mod_IsLoader_is_transient_not_on_disk` — after a cascade-off, read holding `__626mod.json` and assert it contains no `isLoader`/`IsLoader` key (DisabledMeta has no such field; Mod never serialized). Run → pass.
- [ ] Full Core suite + `CorePurityTests` green.

### Task 6 — App: distinguished inline row + cascade toggle wiring
- [ ] `ModRowViewModel.cs`: add `public bool IsLoader => Mod.IsLoader;` + `public Visibility LoaderChipVisibility => Mod.IsLoader ? Visibility.Visible : Visibility.Collapsed;` (mirror IsBuiltin/BuiltinVisibility).
- [ ] `DirectInjectService.cs`: add `public void SetLoaderEnabled(GameEntry game, bool enabled)` → resolve PlayFolder + Holding, call `DirectInject.SetLoaderEnabled`.
- [ ] `MainViewModel.cs`: add `public async Task ToggleLoaderCascadeAsync(ModRowViewModel row, bool on)` modeled on `ToggleFamilyAsync` (NOT ToggleAsync — no optimistic `row.Enabled` mutation; rely on ReloadModsAsync). And at the row build (~line 380) force `canToggle: rep.IsLoader || !rep.ReadOnly || rep.Loader is "ue4ss" or "bepinex"`.
- [ ] `MainWindow.xaml.cs` OnModToggled: add BEFORE the HasVariantOptions branch — `if (row.IsLoader) { if (sw.IsOn == row.Mod.Enabled) return; await ViewModel.ToggleLoaderCascadeAsync(row, sw.IsOn); return; }`.
- [ ] `MainWindow.xaml`: clone the UE4SS-BUILT-IN chip Border (col2) → "LOADER" chip bound to `LoaderChipVisibility`, ThemeAccent brush, Consolas 11.
- [ ] Build App x64 (kill any running instance first) → 0 errors, no new warnings.

### Task 7 — Verify + smoke entry + PR
- [ ] Full Core suite + CorePurity + App build all green.
- [ ] Append a smoke entry to `docs/smoke-tests/pending.md`: loader shows inline with LOADER chip; toggling it off moves the whole stack to holding (rows reflect it) and on restores; individual hosted-mod toggles still work.
- [ ] Run the `reversibility-auditor` agent over the diff.
- [ ] Commit (logical commits per task), push, open PR.

---

## Out of scope (logged follow-ups)
- Expander/parent-child tree (hosted mods nest under the loader).
- "Turn off everything?" confirmation dialog.
- Other engines' loaders (BepInEx/UE4SS).
- Uniquifying the holding-dir slug (the real fix for slug-collision; changes on-disk key, breaks field state — guarded against here, not fixed).
- Batch manifest for crash-mid-cascade recoverability.
