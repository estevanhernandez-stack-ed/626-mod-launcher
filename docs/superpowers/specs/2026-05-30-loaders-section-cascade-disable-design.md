# Distinguished inline loader row for the DLL mod loader — Design

> **SUPERSEDED 2026-05-30 — the cascade was dropped; the loader toggle is now DECOUPLED.**
> Live testing (Este) proved the hosted `mods\` mods sit **inert-but-harmless** when the loader is
> off — they don't load, but cause no crash and the game launches fine. So cascading them to holding
> alongside the loader solved a non-problem. SHIPPED behavior (PR #93): the loader is a visible,
> distinguished, **independently** toggleable row (LOADER chip) — toggling it off moves only its own
> `dinput8.dll`; the hosted `mods\` mods stay in place. Everything below about `SetLoaderEnabled` /
> cascade / cross-unit rollback / the slug + stale-holding guards was **built, validated, then removed**
> in favor of the simpler decoupled toggle (which routes through the existing per-mod Disable/Enable).
> KEPT from the cascade work: the `IsLoader` flag, the un-drop of the loader row, the LOADER chip.
> See handoff `docs/superpowers/handoffs/2026-05-30-decouple-loader-toggle.md`.

**Status:** Design for review. Revises remediation **Task 6** (`docs/superpowers/plans/2026-05-28-smoke-remediations.md`) per live-smoke feedback on 2026-05-30 — the original Task 6 chose "inline locked row"; this supersedes it with "inline distinguished row + cascade-disable."

**Placement (maintainer choice):** the loader stays a row **inline in the MODS list** — not a separate section — but visually marked as a loader (icon + "LOADER" chip) so it doesn't read as a peer mod. Toggle behavior is **cascade-disable** (reversible).

**Goal:** The DLL mod loader (Elden Mod Loader = `dinput8.dll`) stays visible and directly manageable when it's hosting active mods, instead of vanishing from the list. Its toggle turns the whole modded setup off and on in one reversible action.

---

## The problem

For a direct-inject FromSoft game, when the loader's `mods\` folder has contents, [`DirectInjectListing.Enabled`](../../../src/ModManager.Core/DirectInjectListing.cs) drops the bare loader row — `if (loaderMods.Count > 0) top = top.Where(m => m.Name != DirectInject.LoaderName).ToList();` — and surfaces the individual mods (AdjustTheFov, etc.) as their own rows instead. The loader is "represented by its contents."

Two consequences the maintainer hit during live smoke:

1. **You can't see the loader.** It's installed and load-bearing, but invisible — infrastructure you can't confirm or manage.
2. **You can't toggle it off** without first disabling every mod it hosts (or hitting "toggle all off"). There's no direct path to "turn the loader off."

The original hide had a real motive: avoid a redundant row, and avoid the foot-gun where toggling the loader off leaves its mods on disk but silently non-loading (no proxy → nothing loads them). The motive is sound; the full-hide overshot. The fix keeps the safety (no silent-broken state) while restoring visibility + direct control.

The loader is conceptually **not a mod** — it's the thing mods run on. The launcher already separates frameworks and tools from mods; a loader belongs in that same "infrastructure" register, not as a peer row in the mod list.

---

## Decisions

| Decision | Choice | Why |
|---|---|---|
| Visibility | Loader is **always shown** when present (never dropped) | Infrastructure you can't see, you can't trust or manage. |
| Placement | **Inline distinguished row** in the MODS list (icon + "LOADER" chip) | Maintainer choice — matches the "keep it as a line on the mod list" instinct; smallest change; the chip marks it as not-a-peer-mod without a whole new section. |
| Toggle behavior | **Cascade-disable, reversible** — toggling the loader off moves it + its hosted mods to holding together; toggling on restores all | Matches the mental model "turn off the loader = turn off the modded setup"; one action; honors reversibility. |
| Confirmation prompt | **Out of scope (future).** | Maintainer flagged "down the road" — a "turn off everything?" confirm dialog is a later add, not this change. |
| Other engines' loaders | **Out of scope.** FromSoft DLL loader only for now. | BepInEx/UE4SS have their own loader models; not in this feedback. |

---

## Architecture

```text
 MODS
 ────────────────────────────────────────────
  Elden Mod Loader   [LOADER]   on   ← inline, distinguished; cascade toggle
  Adjust The Fov                on
  Remove Vignette               on
  Seamless Co-op     [CO-OP]    on

                 ┌───────────────────────────────────────────────┐
   App           │  DirectInjectListing.List(game)  [Core]        │
 (MainViewModel) │  - enabled loader (always, IsLoader=true)      │
                 │  - its hosted mods (as today)                  │
                 │  - disabled holding entries (as today)         │
                 └───────────────────────────────────────────────┘
                                   │  toggle loader off/on
                                   ▼
                 ┌───────────────────────────────────────────────┐
                 │  DirectInject.SetLoaderEnabled(...)  [Core, NEW]│
                 │  cascade: loader DLL + every hosted mod →       │
                 │  holding (off) / restore all (on). Atomic +     │
                 │  rollback. Reuses DirectInject.Disable/Enable.  │
                 └───────────────────────────────────────────────┘
```

**Core changes:**

- **Surface the loader as a distinct, always-present row.** Stop dropping `DirectInject.LoaderName` from `DirectInjectListing.Enabled`. Instead tag it so the App can render it distinguished — add a transient `IsLoader` bool on `Mod` (clearest; mirrors how `Builtin`/`ReadOnly` already work; never serialized). The row stays in the normal listing so it sits inline in MODS; only its rendering differs.
- **Cascade toggle, reversible — the load-bearing part.** New Core op (working name `DirectInject.SetLoaderEnabled(playFolder, holding, enabled)`):
  - **Off:** enumerate the loader's entry (`dinput8.dll`) + every hosted mod's entries (the `mods\` DLLs + same-named config folders). Move them all to the per-mod holding folders the existing `DirectInject.Disable` already uses. **Snapshot/plan the full move list first; if any move fails, roll back every move already done** so the game folder never lands half-disabled. No `File.Delete`.
  - **On:** restore the loader + all its previously-cascaded mods from holding back to the play folder, same rollback discipline.
  - Build on the existing reversible primitives (`DirectInject.Disable`/`Enable` already do per-mod move-to-holding with rollback) — compose them under one atomic outer operation, don't reinvent the move logic.
- **Hosted-mod rows stay** exactly as today (each `mods\` DLL is its own toggleable row). Disabling an individual hosted mod is unchanged. Cascade only triggers from the loader row's toggle.

**App changes:**

- **Distinguished inline row.** The `IsLoader` row renders in the MODS list like any other row but visually marked — a loader icon + a "LOADER" chip — so it reads as infrastructure, not a peer mod. Its toggle is wired to the cascade op (below) rather than the per-mod toggle.
- `MainViewModel` maps `Mod.IsLoader` → a `ModRowViewModel.IsLoader` flag the row template binds (icon + chip visibility); ordering keeps the loader at the top of the list (it's the thing the others run on).
- The Task 4 amber/red framework chips are unaffected.

---

## Reversibility (the rule this change lives under)

Cascade-disable is a multi-file move, so it follows the project's file-op laws exactly:

1. **Plan before write.** Enumerate the complete set (loader DLL + all hosted mod entries) before moving anything.
2. **Move, never delete.** Everything goes to the existing holding folders; a toggle never deletes.
3. **Atomic with rollback.** If any move in the cascade fails, every completed move is reversed — the play folder is either fully-on or fully-off, never mid-state.
4. **Round-trip exact.** Off → on restores the loader + every hosted mod to where they were.

The `reversibility-auditor` agent reviews this before merge.

---

## Test plan (Core, test-first)

New `tests/ModManager.Tests/DirectInjectLoaderCascadeTests.cs` (fixture mirrors `FromSoftFixture` / `DirectInjectToggleTests`):

1. **Loader is listed when hosting mods** — `mods\` has a DLL → `DirectInjectListing.List` includes the loader row (flagged `IsLoader`) *and* the hosted-mod rows. (Pins the un-drop; this is the regression the maintainer reported.)
2. **Loader still listed when `mods\` empty** — unchanged from today.
3. **Cascade off moves loader + all hosted mods to holding** — after `SetLoaderEnabled(false)`: `dinput8.dll` gone from play folder, every `mods\` DLL gone, all present in holding; game's own files untouched.
4. **Cascade on restores everything** — after off→on: loader + every hosted mod back in the play folder, holding cleared, byte-for-byte.
5. **Rollback on mid-cascade failure** — simulate a move failure partway (locked file / mock) → assert the play folder is back to fully-on, nothing stranded in holding.
6. **Individual hosted-mod toggle is unchanged** — disabling one `mods\` DLL still works and doesn't touch the loader.
7. **`IsLoader` flag is transient** — never written to any on-disk JSON (guard the camelCase/serialization contract).

App: no unit tests (thin shell); `docs/smoke-tests/pending.md` entry — loader appears in its own section, toggling it off moves the whole setup to holding and the rows reflect it, toggling on restores; individual mod toggles still work.

`CorePurityTests` stays green.

---

## Out of scope / future

- **Expander / parent-child tree (maintainer-flagged, strong next step).** Turn the inline loader row into an expandable parent whose hosted mods nest under it as children — a dropdown from the loader reveals the sub-mods running *through* it, making the load relationship visible structurally rather than as flat sibling rows. This builds directly on this change's `IsLoader` flag + the loader-knows-its-hosted-mods grouping; the cascade toggle becomes "toggle the parent → toggle the subtree." Natural follow-up lane once the inline distinguished row ships.
- **Own "Loaders" section** — a dedicated section (sibling to the Tools row) was the alternative placement; maintainer chose inline-distinguished for now, open to a section "in the future." The expander idea above may make a separate section unnecessary.
- **Confirmation dialog** ("turn off everything?") before a cascade-off — maintainer-flagged for later.
- **Other engines' loaders** (BepInEx, UE4SS) — different models; not in this change.
- **Reordering** loaders vs mods — N/A (one loader).

---

## Acceptance criteria

- [ ] With a loader hosting ≥1 mod, the loader is visible in a "Loaders" section and directly toggleable — no "disable everything to reach it."
- [ ] Toggling the loader off moves it + all hosted mods to holding (reversible); toggling on restores all; never a half-disabled state.
- [ ] Individual hosted-mod rows still toggle independently, unchanged.
- [ ] The framework chip behavior (Task 4 amber/red) is unchanged.
- [ ] `IsLoader` is transient — never serialized.
- [ ] Core suite + `CorePurityTests` green; App x64 builds clean; reversibility-auditor passes.
