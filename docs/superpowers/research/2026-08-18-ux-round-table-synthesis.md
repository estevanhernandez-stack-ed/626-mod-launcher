# The round table — synthesis

**Date:** 2026-08-18 · **Input:** [the four seats](2026-08-18-ux-round-table.md) plus Este's desktop
agent · **Subject:** D1 (Settings IA) and D4 (surface coherence)

Five voices. Every load-bearing claim was verified against the source before it was recorded.
**Three of the five found errors in the proposals themselves**, which is the point of having asked.

---

## Seat 5 — the architect (Este's desktop agent)

Pushed back on framing rather than findings.

**Item 4's rule needs a second clause.** *"Same sentence → one control"* solves duplication and creates
**buried capability**:

> Same sentence → one control. **The default action is the one that keeps the user in the app; the
> alternative is one step deep and named for what it does.**

**Say the capability in the label, not the hover.** `Find mods (in-app)` versus `Find mods on Nexus ↗`.
In the Store SKU the in-app catalog is a *permanent* condition, and permanent conditions should not
nag — *"the memo's own principle applied to its own fix."*

**Item 5 is a data question, not a taste question**, and this is the sharpest reframe anyone offered:

> *"Guard-don't-hide was the right rule for identify actions because those fail on STATE — transient
> conditions where an explanation helps. Engine facts aren't transient. Different failure class,
> different rule, and the memo can say so instead of presenting three options as equals."*

**Game-shaped, not state-shaped.** With one carve-out it reached independently of the power user:
MP/SP is *list-shaped* and can flip mid-session, so it wants disable-with-reason instead.

**Banners → a status strip.** One row, one chip per condition, tap to expand. The bindings already
exist, so *the refactor is layout, not logic, and it reduces code* — four one-off banners become one
templated control.

**On D1:** the inventories leave Settings, because `ToolsPanel` already holds tool and framework chips
— *"D4's exact pattern, two doors, same room, inside D1's own subject."* Direct-inject configs are
per-mod configuration and belong on the mod row. Layout decided **by count, not growth**: three groups
plus a footer is one scroll, because *tabs and rails hide the danger group* and bottom-of-scroll is the
danger convention. Per-game defaults live on the status strip. **Outlined danger at the entry, filled
at the confirm**, per `.claude/rules/vsm-danger-buttons.md`. Harness first, three assertions per group
— heading, expected ids, and *nothing else under Danger*, which is the one that catches drift.

---

## What five voices converge on

**1. A status strip — and three seats reached it independently.** Feng shui wants one ranked strip
above the mod list ordered by consequence, ban risk first. The architect wants one row of chips
replacing four banners. D1's unanswered per-game question lands on the same surface. Biggest layout
win, *reduces* code, bindings already exist.

**2. The bug is `Browse Nexus` vanishing, not the two doors.** Power user, switcher and new modder all
hit it separately — the new modder hardest: *"from where I sat, this launcher has no way to find mods
inside itself, and I'd never learn otherwise."*

**3. The inventories leave Settings.** Feng shui, switcher and architect all answered the question D1
left open, and the architect closed it with a fact rather than a preference.

**4. Restore points are not danger.** They are the undo *for* danger; filing them under that heading
teaches people to avoid the recovery path.

---

## The one real dispute, and what evidence does to it

Hide versus disable for sometimes-relevant controls. The architect's game-shaped/state-shaped frame
narrows it almost to nothing:

| Control | Shape | Evidence | Verdict |
|---|---|---|---|
| **MP/SP** | **list-shaped** — the user can tag a mod by hand mid-session | power user and architect carved it out independently | do not hide |
| **Saves** | claimed game-shaped | `SaveDir` is a registration field; when empty the click handler runs `SaveLocator.DetectAsync` (Ludusavi → heuristics → Steam id). A **fact after first use, a probe before it** | do not hide — gating needs eager detection per game |
| **Reorder** | genuinely game-shaped | already guards: *"Load order doesn't apply to these mods — they load independently."* | already compliant |
| **Browse Nexus** | capability-shaped | the only control that hides, and the hiding is the bug | stop hiding |

**A philosophy argument resolved to one control already compliant and one that should stop hiding.**

**Settings layout:** three of four say one scroll, and the architect's reason decides it — a rail puts
Danger behind a click. With the inventories gone it is three groups; a rail for three is ceremony.

---

## What outranks the entire agenda

**The MP/SP segments perform a bulk file operation.** Verified: `SetMode` → `Scanner.ApplyMode` walks
every mod and enables or disables it, then returns early as a cosmetic highlight on Mod Engine 2,
direct-inject and loose-root games. Identical styling, opposite behaviour, in a control shape that
means *change what I see* in every other application. **Both memos call it a filter.**

The switcher: *"the only item that costs you the user rather than the feature."*

---

## Recommended order

1. **MP/SP** — safety, smallest fix here, and the only one that loses a user rather than a feature.
2. **Status strip** — three seats converged; reduces code; lands D1's question 3 in the same build.
3. **`Browse Nexus` stops vanishing** — one control, one string, three seats.
4. **The empty mod list says something** — a registered game with no mods renders a blank rectangle at
   exactly the moment the app needs to say *drop a zip here*.
5. **The `NEEDS ___` chip offers the action it can already perform** — the give-up moment, and the app
   can install the framework if only the chip said so.
6. **Settings: three groups + footer, one scroll, outlined danger at entry.** Harness asserts groups
   first, red before green.
7. **Vocabulary** — one word for loadout/profile, and stop using `LIBRARY` for two things one click
   apart.
8. **Keyboard accelerators** — three exist today. Cheap wins: `Ctrl+,` `Ctrl+O` `Ctrl+P` `Ctrl+1/2/3`.

Items 1, 3, 4 and 5 are small and independent. Item 2 is the one worth planning properly.

---

## What the table found in the proposals

1. The one-door merge **reverses a documented decision** (`MainWindow.xaml:170`); a SplitButton gets
   the benefit without the regression.
2. `Reorder` **already implements** the rule the memo asks for — the table's "always shown" was true
   and misleading.
3. **Restore points are not danger.**
4. Hiding the plugin-refresh button **contradicts the rule the same table is being asked to adopt**,
   and targets the one control needed when the app's own judgment is the broken thing.
5. Both memos **call MP/SP a filter**.
6. D1 claimed Settings has **31 automation ids**; it has **12**.
