# Next steps after the mod dashboard ships

**Status:** As of 2026-05-27, the mod dashboard lane is merged (PR #56). Master is at `061c95e`. The portable smoke build is at `dist/626-Mod-Launcher-portable-win-x64.zip` (67.3 MB). Smoke list with 17 items is in [`docs/smoke-tests/pending.md`](../../smoke-tests/pending.md).

Two lanes are queued — both can run in fresh-context sessions.

---

## Lane 1: Elden Ring inventory editing

**Effort:** 17-24h, 12 tasks. **Spec + plan already on master** from PR #48.

**What it ships:** Phase 2 of the FromSoft save editor — adds inventory edit (add item, remove item, set quantity / +N level / infusion). New tabbed `CharacterEditDialog` (identity / attributes / inventory tabs, all commit through one Save edit). Embedded ClayAmore item catalog (Apache-2.0) + alfizari quest-locked items list (MIT) covering ~1,500 ER items.

**Why this lane is ready:**
- Plan: [`docs/superpowers/plans/2026-05-26-saves-editor-fromsoft-inventory.md`](../plans/2026-05-26-saves-editor-fromsoft-inventory.md)
- Spec: [`docs/superpowers/specs/2026-05-26-saves-editor-fromsoft-inventory-design.md`](../specs/2026-05-26-saves-editor-fromsoft-inventory-design.md)
- Licenses clean: ClayAmore Apache-2.0 + alfizari MIT. Honor-the-builders surfaces planned (NOTICE + Settings → About + in-dialog attribution).
- BND4 walk (PR #49) is the load-bearing foundation; the inventory walker hangs off `DiscoverMagicOffset` in [`src/ModManager.Core/SaveEditor/FromSoft/EldenRingSave.cs`](../../../src/ModManager.Core/SaveEditor/FromSoft/EldenRingSave.cs).

**Concerns from the plan:**
- Task 0 (format research + offset lock) is load-bearing. Wrong inventory offsets = bricked saves. Snapshot-first + post-write verify catch this; goal is to never trigger.
- Item catalog source: ClayAmore primary + alfizari for ER 1.13+ diff. License-bundle check happens in Task 1.
- Quest-locked items: ~90 items, finalized in Task 0 by cross-referencing wiki questline trackers.

---

## Lane 2: Save-editor pipeline as a reusable skill

**Effort:** brainstorm-first, then spec, then plan, then execute. Not yet scoped.

**What it is:** Meta/process work — turn the FromSoft save editor pattern into a **reusable skill** so adding save editors for new games (DS3, Sekiro, AC6, future FromSoft titles, or other engines entirely) is mechanical instead of bespoke. Use Elden Ring's pattern (BND4 walk + slot offsets + snapshot-first writes + neutral DTOs) as the canonical template; the skill drives an agent through pinning format facts → generating model types → wiring the dialog.

**Why now:** Este flagged this earlier today as "we could probably make it a skill" alongside the ER inventory ask. Knowledge captured during ER inventory editing becomes immediately applicable to the next FromSoft game. The skill turns one-off save-editor builds into a 2-3 day per-game lane.

**Status:** No spec, no plan, no scope yet. Needs `superpowers:brainstorming` from scratch.

**Memory references:**
- [[saves-editor-fromsoft]] — existing brainstorm history
- [[fromsoft-two-mod-worlds]] — engine context (ME2 vs direct-inject)

---

## Recommended order

**Lane 1 (ER inventory) first.** Rationale:
- Spec + plan exist; lane 2 is brainstorm-only
- ER inventory's execution surfaces real-world friction with the format → that friction informs lane 2's skill design
- Lane 2 (the meta-skill) lands stronger if the second concrete editor (DS3 or Sekiro) is in the pipeline OR if ER inventory's lessons are fresh

**Alternative:** if you want the meta-thinking first, run lane 2's brainstorm now while ER inventory is still queued. Both are valid.

---

## Short prompt for the fresh session

Paste this into a fresh Claude Code session after compacting:

```
Picking up where the prior session left off. Master is at 061c95e (mod dashboard PR #56 merged). Local portable build at dist/626-Mod-Launcher-portable-win-x64.zip is smoking; smoke list in docs/smoke-tests/pending.md has 17 items across three recent lanes.

Two lanes are queued. Pick one based on what I tell you:

LANE 1: ER inventory editing.
  Spec: docs/superpowers/specs/2026-05-26-saves-editor-fromsoft-inventory-design.md
  Plan: docs/superpowers/plans/2026-05-26-saves-editor-fromsoft-inventory.md
  Effort: ~17-24h, 12 tasks. Licenses clean (ClayAmore Apache-2.0 + alfizari MIT).
  If I say "lane 1" or "ER inventory": cut feat/saves-editor-fromsoft-inventory off master and execute via superpowers:subagent-driven-development. Same pattern as PR #49/#51/#56.

LANE 2: Save-editor pipeline as a reusable skill.
  Spec: not written yet.
  Plan: not written yet.
  If I say "lane 2" or "save-editor skill": invoke superpowers:brainstorming. The goal is to turn the FromSoft save editor pattern into a skill that mechanizes adding save editors for new games. Use Elden Ring as the canonical template. Memory notes: [[saves-editor-fromsoft]], [[fromsoft-two-mod-worlds]].

Read docs/superpowers/handoffs/2026-05-27-post-dashboard-next-steps.md for the full handoff. Wait for me to pick which lane before doing anything beyond reading.
```
