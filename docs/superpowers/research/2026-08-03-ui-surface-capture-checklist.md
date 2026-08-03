# UI surface capture checklist (vibe-glow stage 0 baseline)

Evidence dir: `docs/ui-evidence/` (gitignored). Naming:
`NN-<surface>--<theme>.png`. Themes per full round: `default` (current
default theme), `obsidian`, `matrix` (stress). Switch themes in Settings
between rounds. After the reveal ships, rounds swap `default` for the
flagship theme.

Build for capture (Nexus surfaces vanish without the version stamp):

    dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.15.0.0

If the app doesn't appear, check `app-errors.log`. After any XAML edit,
clean `obj/`/`bin/` first.

## Automated (single mode, per theme)

| # | Surface | Reach |
| --- | --- | --- |
| 01 | main-window | Launch. Library loaded, at least one game with mods. |
| 02 | library-view | Default landing view. |
| 03 | nexus-catalog-view | Nexus tab/section (signed in). |
| 04 | updates-view | Updates section. |
| 05 | tools-panel | Tools section. |

## Guided (watch mode — F8 per dialog, Esc to end)

| # | Surface | Reach |
| --- | --- | --- |
| 06 | settings-dialog | Settings gear. |
| 07 | add-game-dialog | Add game action. |
| 08 | profiles-dialog | Profiles action. |
| 09 | saves-dialog | Saves action on a game with saves. |
| 10 | safe-clear-dialog | Safe clear action. |
| 11 | new-theme-dialog | Settings → themes → new theme. |
| 12 | ini-editor-dialog | A game exposing INI edit. |
| 13 | character-edit-dialog | Save editor on a FromSoft save. |
| 14 | update-mods-dialog | Update-check action with updates available. |
| 15 | manual-match-dialog | Unmatched mod → manual match. |
| 16 | loose-identify-dialog | Drop a loose folder onto the window. |
| 17 | framework-install-dialog | Drop a known framework archive. |
| 18 | framework-unrecognized-nudge-dialog | Drop an unknown archive resembling a framework. |
| 19 | tool-configure-dialog | Tools panel → configure on a tool. |
| 20 | vortex-takeover-dialog | A game with a Vortex-managed folder. |
| 21 | nexus-catalog-dialog | Nexus browse action. |
| 22 | nexus-mod-detail-dialog | Open a mod from the Nexus catalog. |

Some guided rows need staged conditions (updates available, a Vortex
folder, a FromSoft save). Capture what's reachable in round 1; list the
rows you skipped at the bottom of the round's notes and stage them for
round 2. A skipped row is recorded, not forgotten.

## Round notes — 2026-08-03 baseline (stage 0)

Captured: 33 PNGs — 20 `default` (626 Labs), 8 `obsidian`, 5 `matrix`.
Default round covered the full walk; obsidian/matrix ran the lean
structural set (library home, game mods view, settings, saves,
character edit; obsidian adds INI editor, new theme, readme popup).
All 2580x1023, one monitor, one scale.

**Checklist corrections (learned driving the app):**

- `update-mods-dialog` does not exist — the update path routes to Nexus.
  Row removed from scope.
- `profiles-dialog` does not exist as a dialog surface.
- `tool-configure-dialog` does not exist — tools install via `+` on the
  tools bar; there is no per-tool configure surface. Este would like one
  (needs tool-pack metadata to drive it) — QOL candidate for the stage-1
  audit.
- `safe-clear-dialog` is the Reset launcher dialog (restore-point +
  return-to-vanilla flow).

**Skipped rows (staged conditions not loaded — recorded, not forgotten):**
manual-match, loose-identify, framework-install,
framework-unrecognized-nudge, vortex-takeover.

**Observations parked for the stage-1 audit (not findings yet):**

- Mod-row toggle switches render blue under obsidian and matrix while
  every other control re-themes — hardcoded accent suspect.
- Library home under obsidian is visually near-identical to default —
  worth checking which tokens the home view actually consumes.

**Adapter friction (for the vibe-glow retro):**

- Esc ends the watch session AND closes app dialogs — closing a dialog
  with Esc silently kills capture. Drive dialog-close via app buttons.
- F8 presses during the gap between watch sessions are lost silently;
  the operator has no signal the watcher is down.
- Window titles all read "WinUI Desktop" — auto-labels are useless for
  this app; every capture needs a manual rename pass.
