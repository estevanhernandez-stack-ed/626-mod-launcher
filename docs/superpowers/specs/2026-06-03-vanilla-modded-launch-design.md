# Vanilla vs Modded launch — an honest two-mode launch button

**Date:** 2026-06-03
**Status:** Design — approved in brainstorm, pending spec review
**Branch:** `feat/vanilla-modded-launch`

## Problem

The launch button shows a per-game target label like **"Play vanilla (Steam)"**. That label was written for Elden Ring, where mods load through a required launcher (Seamless Co-op → `ersc_launcher.exe`) and a plain Steam launch genuinely bypasses them — so "vanilla (Steam)" is accurate there.

But it's misleading on most other games. Mods load by three different mechanisms, and only one of them is bypassed by a plain launch:

1. **Required-launcher mods** (Seamless Co-op): engage only through their own exe. A plain Steam launch IS vanilla. (The case the label was written for.)
2. **Direct-inject DLLs** (dinput8 / dwmapi / UE4SS proxy / ReShade): load into ANY process started from the exe folder. A plain launch is NOT vanilla — the DLLs still inject. (This is why `LaunchGuard.NeedsDirectInjectStepAside` exists.)
3. **Pak mods** (`~mods` / `LogicMods` on UE-pak games like Windrose): loaded by the game engine on any launch by file presence. A plain launch is NOT vanilla.

So on a UE-pak + UE4SS game like Windrose, clicking "Play vanilla (Steam)" launches WITH mods — the label is a lie, because the mods load by file presence, not through a special launcher. There is no vanilla launch on these games without first stepping the mod files aside.

## Goal

Make the button honest by making it real: a true **Play vanilla** that produces a mod-free run, and a **Play (modded)** that launches with mods — on every engine, with labels that match what the click does.

## Decisions (from brainstorm)

- **Real two-mode launch**, not just a relabel.
- **Stateful, not auto-restore.** Play vanilla steps the active loaders aside (reversible) and they STAY aside — the UI shows them off — until Play modded (or a manual toggle) brings them back. No game-exit detection (launch is fire-and-forget; Steam-DRM exit-detect is unreliable). The two modes map to two real, visible on-disk states.
- **Restore replays the EXACT prior-active set, not "enable all."** If 8 of 12 mods were on (4 deliberately off), Play vanilla disables those 8; Play modded re-enables exactly those 8 — never the 4 the user chose to keep off. This is the one thing a naive "toggle all on" gets wrong, and the reason Restore reads a recorded stash rather than blanket-enabling.
- **Vanilla means everything that loads steps aside** — mod rows AND active frameworks (UE4SS proxy + `ue4ss/`) AND direct-inject proxies — not just mod rows. Otherwise "vanilla" is still a lie on the exact games that prompted this (UE4SS Lua mods load even with the pak rows disabled, because UE4SS itself is still injecting).
- **One smart split-button** whose label tracks state (the existing label-tracks-target pattern). The opposite mode lives in the dropdown.

## Architecture

Pure-Core orchestration + state, thin App surface. Reuses the existing reversible primitives (mod-row disable → holding; framework disable; direct-inject step-aside) — adds NO new file-op law.

### 1. Core — the launch-state model (`VanillaLaunch`)

New class `src/ModManager.Core/VanillaLaunch.cs`. System.IO + System.Text.Json only.

**`LaunchMode` enum** — `Vanilla` | `Modded`. Derived from on-disk state, never stored as a bare flag.

**`StepAside(GameContext ctx) → StepAsideResult`**
1. Collect every ACTIVE loading mechanism for the game:
   - enabled mod rows (with their location),
   - installed/active frameworks (UE4SS etc., from `FrameworkRegistry`),
   - active direct-inject proxies (from the direct-inject service's "active proxy DLL" read).
2. Move each aside using the EXISTING reversible primitive for its kind (mod-row disable → holding folder; framework disable; direct-inject step-aside). Nothing is deleted.
3. Record the exact set moved in `<dataDir>/vanilla-stash.json` (camelCase) — per mechanism, enough to replay the restore precisely.
4. Stage-then-commit: if any single step-aside fails (file locked, game running), roll back everything already moved and return a failure — never a half-vanilla state.

**`Restore(GameContext ctx) → RestoreResult`**
Read `vanilla-stash.json`, re-enable/restore EXACTLY the recorded set (mod rows, frameworks, proxies), then delete the stash. Idempotent against partial state and against a user who already manually re-enabled things (no double-enable). A missing/corrupt stash → safe no-op.

**`CurrentMode(GameContext ctx) → LaunchMode`**
`Vanilla` when a `vanilla-stash.json` exists (we stepped aside); `Modded` otherwise. This drives the button label with no stored flag to drift. Note: if the user manually re-enables a row after StepAside, the mode read must reflect reality — see the manual-toggle edge below; the stash is cleared so `CurrentMode` returns `Modded`.

### 2. App — the smart button

The existing split-button stays. Its primary action + label become state-aware via `CurrentMode`; the dropdown gains the opposite-mode action. The existing per-target entries (Seamless, ME2, Steam) and the existing launch guards are unchanged — vanilla/modded is a SECOND axis layered on top.

- **Modded** (mods active): button reads **"▶ Play (modded)"** (or composes with the effective target, e.g. "▶ Play (Seamless Co-op)"). Primary click launches as today, honoring the existing guards. Dropdown adds **"Play vanilla"** → `StepAside` → refresh rows (all show off) → launch clean.
- **Vanilla** (stash exists): button reads **"▶ Play vanilla"** and MEANS it (proxies stepped aside). Primary click launches clean. Dropdown adds **"Play modded"** → `Restore` (re-enable exactly the stashed set) → refresh rows → launch.

After Restore/StepAside, the App calls the existing reload so rows repaint, then fires the launch on the real target (existing Seamless/ME2/direct-inject guards still apply to that target).

## On-disk shape (camelCase)

`<dataDir>/vanilla-stash.json`:

```json
{
  "version": 1,
  "steppedAsideUtc": "2026-06-03T...Z",
  "modRows": [ { "name": "FasterShips10", "location": "mods" } ],
  "frameworks": [ "ue4ss" ],
  "directInjectProxies": [ "dwmapi.dll" ]
}
```

Written via `AtomicJson`. Ships a round-trip test asserting the camelCase keys on disk (per the camelCase-JSON-on-disk rule). Added to the rule doc's governed-surfaces list.

## Edge cases

- **Manual toggle after StepAside** — user re-enables a row by hand: that mechanism leaves holding, something active exists again → the stash is cleared and `CurrentMode` returns `Modded`; the button re-labels. No stuck "vanilla" state. (Clearing the stash on a detected manual re-enable happens at the next reload/mode read.)
- **Stash exists but on-disk already matches modded** (user manually re-enabled everything) → Restore is a no-op + stash delete; never double-enables.
- **A mechanism can't step aside** (file locked, game running) → StepAside refuses as a unit, rolls back anything already moved, surfaces the reason — never a half-vanilla launch. Same stage-then-commit shape as Vortex takeover / the Lua installer.
- **Stale/corrupt stash** → treated as "no stash" (Modded); Restore degrades safely, never throws into the UI.

## Testing

**Core (xUnit):**
- StepAside records EXACTLY the prior-active set; a deliberately-off mod is NOT in the stash and NOT re-enabled by Restore (the "8 of 12" guarantee).
- StepAside → `CurrentMode == Vanilla`; Restore → `Modded` with the original active set restored byte-for-byte.
- StepAside includes frameworks + direct-inject proxies, not just mod rows (the "vanilla isn't a lie on Windrose / Elden Ring" guarantee).
- Partial-StepAside failure rolls back (no half-vanilla state).
- Restore is idempotent; a user who already re-enabled everything isn't double-enabled.
- Stale/corrupt stash degrades to Modded; Restore on a missing stash is a safe no-op.
- camelCase round-trip on `vanilla-stash.json`.

**App:** label tracks `CurrentMode`; dropdown shows the opposite-mode action; StepAside/Restore each refresh rows then launch; the existing launch guards still fire on the actual target.

## Non-goals

- No game-exit detection, no auto-restore (explicitly rejected as fragile).
- No new file-op law — reuses the existing disable / framework-disable / direct-inject step-aside reversible primitives.
- Does not change the existing per-target launch entries or their guards (Seamless, ME2, direct-inject step-aside, Steam-running) — vanilla/modded layers on top.
- Does not touch the Vortex-takeover work (separate branch / PR #113).

## Surfaces touched

| Path | Change |
|---|---|
| `src/ModManager.Core/VanillaLaunch.cs` | NEW — StepAside / Restore / CurrentMode + stash shape |
| `src/ModManager.Core/Scanner.cs` (or the disable service) | Reuse existing mod-row disable/enable primitive; expose a Core entry if needed |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | Mode-aware label + the two dropdown actions; refresh-then-launch |
| `src/ModManager.App/MainWindow.xaml(.cs)` | Dropdown gains the opposite-mode item |
| `.claude/rules/camelcase-json-on-disk.md` | Register `vanilla-stash.json` (local-only doc; `.claude/` is gitignored) |

## Open implementation question (resolve in planning)

The Core StepAside needs to drive the mod-row disable, framework disable, and direct-inject step-aside. The mod-row disable currently lives behind the App's VM / Scanner; the plan must confirm a Core-callable entry exists for each of the three mechanisms (or add a thin Core wrapper) so `VanillaLaunch` stays pure-Core and testable. This is wiring, not a design fork — flagged so the plan pins the exact call shape for each mechanism.
