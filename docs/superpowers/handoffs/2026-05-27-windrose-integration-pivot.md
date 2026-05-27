# Windrose save editor — integration pivot

**Status:** pivoted 2026-05-27. Native plan parked; integration brainstorm to come.

## What happened

Task 0 (format research) of the native Windrose save editor plan was executed and committed (`285323f`). The research turned up two structural blockers that make the native plan as written non-viable:

### 1. Save format is BSON, not a packed byte struct

The plan assumed character data was a packed byte struct readable with hex offsets (`OffsetLevel = 0x40`, etc.). Reality:

> Per-character data is a BSON document, not a packed byte struct. Stats live at `PlayerMetadata.PlayerProgression.StatTree.Nodes.<key>.NodeLevel`. Talents at `PlayerMetadata.PlayerProgression.TalentTree.Nodes.<key>.NodeLevel`. Gold is an inventory currency item under `Inventory.Modules.<idx>.Slots`. XP doesn't exist as an editable scalar — `ProgressionPoints` is derived from the sum of stat NodeLevels.

Stats are six (Strength, Agility, Precision, Mastery, Vitality, Endurance), cap 60 — not the placeholder (STR / DEX / CON / INT) the plan listed. Talents are four trees (Fencer / Toughguy / Marksman / Crusher), 42-asset canonical table.

Pushing through the plan as written would have built Tasks 3-4 around the wrong model. The research doc is the new source of truth: [`docs/superpowers/research/2026-05-26-windrose-save-format.md`](../research/2026-05-26-windrose-save-format.md).

### 2. Honor-the-builders blocks the native re-implementation

- **Chris971991/WindroseCharacterEditor** — source is **private** (binary releases only). We can't reference it.
- **WSE Project (RimmyCode)** — README says "personal use, see Nexus terms." Not OSS. Both the Python source AND the 1,268-item ID Database the plan's Task 7 would have shipped fall under this license.

So we have one viable reference (WSE Project) under a restrictive license that explicitly forbids redistribution. Task 7 (ship the item DB) would have been a direct honor-the-builders violation.

## New direction — integration, not re-implementation

Instead of building a native Windrose editor, **launch WSE Project as the canonical editor and wrap our snapshot-first safety law around it.** Same pattern we already use for Vortex / MO2 coordination ([`coordination-owned-folder-invariant`](../../../README.md) — the launcher orchestrates; the owner-tool edits).

**Why this is the right call:**

- **Zero license risk.** We're not redistributing anything — just launching what the user already installed.
- **Honor-the-builders done correctly.** Drives traffic + donations to RimmyCode instead of competing.
- **Survives game patches for free.** When Windrose ships a save-format change, RimmyCode handles it. Our launcher keeps working.
- **Composes with existing patterns** — the framework-dep chip + get-link from PR #51 lands a "Get WSE Project" link the same way it lands "Get UE4SS."
- **Adds a product surface that scales** — "mod-tool coordinator" becomes a differentiator that slots future tools in via the same model.

**FromSoft editor stays native.** Its references (BenGrn, ClayAmore, alfizari) have clean OSS licenses and we already shipped it. Two engines, two models — pragmatic, not inconsistent.

## What's committed already

- Branch: `docs/windrose-pivot` (renamed from `feat/saves-editor-windrose`)
- Commit `285323f` — research: pin Windrose RocksDB key schema + character payload offsets
  - 311-line research doc at [`docs/superpowers/research/2026-05-26-windrose-save-format.md`](../research/2026-05-26-windrose-save-format.md)
  - Includes verified Steam App ID `3041230` (plan's placeholder `2399830` was wrong)
  - BSON field paths for stats, talents, inventory
  - Column-family layout, key disambiguators, write-strategy notes
  - License audit of every source
- This handoff doc.

## What needs to happen next (tomorrow's work)

### Brainstorm the integration plan (~5 tasks)

1. **Detect WSE Project install** — common paths, registry probe, user-pick override. Cache in settings.
2. **"Edit in WSE Project" button** in the Saves dialog when Windrose is the active game (engine `ue-pak` + Steam App ID `3041230`).
3. **Snapshot-first wrapper** — our existing pipeline runs BEFORE launching their tool. If snapshot fails, edit aborts before WSE Project even opens.
4. **Refresh-on-exit** — after WSE Project closes, re-read the characters list so the dialog reflects what they did.
5. **"Get WSE Project" link** when not installed — same chip-and-hyperlink pattern as PR #51's `NEEDS UE4SS`. Click opens the Nexus page.
6. **Honor surface** — attribution + donation link in Settings → About. Maybe a one-time "WSE Project is doing the heavy lifting here — consider supporting them" callout on first launch.

That replaces the old plan's 9-task scope (Task 0 already done; Tasks 1-8 superseded).

### Decide on the old plan + spec

The current files based on the wrong-shape assumption:
- [`docs/superpowers/plans/2026-05-26-saves-editor-windrose.md`](../plans/2026-05-26-saves-editor-windrose.md)
- [`docs/superpowers/specs/2026-05-26-saves-editor-windrose-design.md`](../specs/2026-05-26-saves-editor-windrose-design.md)

Options: (a) leave them and reference this handoff at the top of each as "SUPERSEDED — see X"; (b) replace with new `-integration-` versions; (c) delete and start fresh. Recommend (a) — the old work captures real thinking and might inform a future native attempt if licensing changes.

### Roadmap context — other lanes still queued

- **ER inventory editing** (~17-24h) — clean ClayAmore Apache-2.0 + alfizari MIT licenses, well-specced. Could run in parallel with the Windrose-integration brainstorm.
- **Cross-FromSoft character transfer** — longer-horizon, needs its own brainstorm.
- **Vortex Collections support** — longer-horizon, see [`saves-editor-fromsoft`](../../..) memory.
- **DS3 / Sekiro / AC6 save adapter** — longer-horizon, has AES-128-CBC encryption layer.

## If you want to re-open the native option later

Three things need to change:
1. RimmyCode grants explicit redistribution permission for WSE Project's source OR the item DB (or one without the other — they're separable).
2. **OR** we re-derive the item DB ourselves from game assets via FModel (the README itself credits FModel as the original source — this path is clean).
3. **OR** somebody publishes a third format reference under an OSS license that we can study without restriction.

If any of those happens, the research doc at `docs/superpowers/research/2026-05-26-windrose-save-format.md` is the head-start. The native plan can be rebuilt around BSON traversal instead of byte offsets — probably 1-2 days of work given the format is now well-documented.
