# Home / title-bar / refresh UX pass — design

**Date:** 2026-08-02
**Status:** Spec (approved in-conversation — "that is the shape"). Three small, flavor-neutral launcher-UI fixes surfaced while smoke-testing OAuth. Ships to both builds; precedes today's Store cut.

## The problem

Three friction points from live use:

1. **Home game rows aren't clickable.** In the all-games list on the Library home, only the Play / Manage buttons act; clicking the row body does nothing. Users expect the row to open the game.
2. **Switching games forces a Home round-trip.** #168 replaced the title-bar game dropdown with a name label + Home button ("the library is the switcher"). To change games you must go Home, then pick. No quick-switch.
3. **Two separate refreshes.** "↻ Rescan" (toolbar, local mod re-scan) and "Refresh Nexus stats…" (buried in the game-options More menu, polls Nexus by mod id) are two actions for what users read as one "refresh."

## Scope guard

All three are **App-side UI over existing commands** — `OpenGameCommand`, `RefreshCommand` (→ `ReloadModsAsync`), `RefreshNexusStatsAsync`. No Core changes, no plugin/contract changes, no new persisted shape. Flavor-neutral: nothing new leaks into STORE; the seal is untouched. The Nexus half of the consolidated refresh stays gated by the existing capability check (inert on Store / when disconnected).

## The three fixes

### 1. Clickable home game row
Make the whole all-games row (`LibraryView.xaml` `Rows` template, ~lines 81-158) open the game via `OpenGameCommand` (the same command the Recent cover cards and the Manage button already call). Because the row becomes the open-affordance:

- **Drop the now-redundant Manage button; keep Play.** Row click = manage/open; Play = launch.
- Implement as a click/`Tapped` on the row container (mirror `OnRecentClick`/`OnManage` → `OpenGameCommand.Execute(row)`), with the Play button as an inner control whose click does not bubble to the row (Play launches, doesn't also open).
- Keep the row visibly interactive (pointer-over affordance) so it reads as clickable.

### 2. Title-bar game switcher
Replace the game-name label in `GameTitleControls` (`MainWindow.xaml` ~lines 39-44) with a **ComboBox** bound to the game list:

- Shows the **current game** as the selection; expanding lists all games (same source the library `Rows` use — the `GameLibraryRowViewModel` set).
- Selecting a different game **switches to it** (→ `OpenGameCommand` / the existing select-game path), no Home round-trip.
- **Home button stays** (→ full library for browse/discovery). Complementary to #168, not a reversal: home for discovery, dropdown for quick-switch.
- The switcher is only shown in the game view (not on the library home) — it toggles with `GameTitleControls` in `ShowLibrary` / `HideLibraryForGame` exactly as the label did.

### 3. Consolidate the two refreshes
Replace both with **one "↻ Refresh"** in the LIBRARY toolbar section (where "↻ Rescan" is today, `MainWindow.xaml` ~line 206):

- Rescans the mod list (`RefreshCommand` / `ReloadModsAsync`) **and** refreshes Nexus stats (`RefreshNexusStatsAsync`) when Nexus is connected — one user action, force-refresh both.
- **Removes** the separate "Refresh Nexus stats…" item from the More menu (`MainWindow.xaml` ~line 70) and its `OnNexusRefresh` wiring (folded into the consolidated action).
- The Nexus stats step is a no-op when `NexusActionsAvailable` is false (Store / not connected) — the consolidated button still rescans locally, so it's always useful on every flavor.
- Sequence: rescan first (fast, local), then the Nexus stats sweep (network, gated). Surface a brief status while the Nexus sweep runs.

## Error / edge handling

- Row click on a game whose folder is missing → the existing `OpenGameCommand` behavior (unchanged).
- Switcher with one game → still works (single-item dropdown) — no special-case.
- Consolidated Refresh when offline → rescans locally, Nexus step degrades silently (existing gate).

## Testing

App-side UI — covered by **build (FULL + STORE) + STORE seal + a live smoke**, not unit tests:

- FULL + STORE build succeed; `check-store-seal.ps1` green (no new symbols leak to Store).
- Core suite stays green (no Core touched).
- Smoke (append to `docs/smoke-tests/pending.md`): (a) click a home row → opens that game; Play still launches without opening; Manage button gone. (b) title-bar dropdown lists games, selecting one switches without going Home; Home still returns to the library. (c) one "↻ Refresh" rescans + refreshes Nexus stats (connected) / rescans only (disconnected); the old "Refresh Nexus stats…" menu item is gone.

## Non-goals

- No Core / plugin / contract changes. No new commands (reuse existing).
- No change to the Library home layout beyond the row-click + dropped Manage button.
- Not the Nexus catalog (separate, deferred) and not the Store-Nexus work.
