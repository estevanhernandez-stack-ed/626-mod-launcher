# Two doors to the same room

**Date:** 2026-08-18 · **Entry:** D4 · **Status:** agenda for a UI round table, not a build

Este: *"if we have 2 different surfaces to search Nexus, but one of them only takes you to the Nexus
website and the other one loads into the app, we need to consider if we just want to make that a
single section that gives the user the option to go to the website."*

And the goal underneath it, in his words: users should not be *"avoiding certain areas"* but
*"welcomed into them so they know exactly what they do."*

---

## The audit

Every label the main window offers, grouped by what a person would think it does.

### Finding mods — three doors, and the difference is invisible

| Surface | What it does | What the label says |
|---|---|---|
| **Browse Nexus** (toolbar) | opens the in-app storefront | "Browse Nexus" |
| **Find mods ▾** (toolbar) → *Find mods on Nexus Mods* | opens your browser | "Find mods" |
| **Find mods ▾** → *Check CurseForge* | opens your browser | — |
| Settings → **Nexus Mods** | connect, disconnect, plugin | — |

**Two buttons sit side by side, both meaning "find mods", and the only difference is whether you stay
in the app.** Nothing on either label says so. A user who presses the wrong one is thrown into a
browser they did not ask for — and, worse, a user who has *only ever* pressed that one has no idea the
in-app storefront exists.

It also fails invisibly: when the Nexus plugin is not loaded — which is every dev build until today,
and every Store build by design — *Browse Nexus* is simply absent, and the app looks like it only ever
had the browser handoff. That is exactly how it presented this afternoon.

**Proposal.** One door. **Find mods** opens the in-app storefront when it can, and carries *"Open on
the Nexus website"* inside it as a choice the user makes deliberately. Where the plugin cannot load,
the same button opens the website and says why in one line rather than vanishing. One entry point,
no dead ends, and the capability difference stated rather than expressed as an absence.

### Identifying mods — five doors, and this one is deliberate

| Surface | |
|---|---|
| More → **Identify my mods…** | the unified run |
| More → Advanced → **Match against my downloads folder…** | |
| More → Advanced → **Refresh details from Nexus** | |
| More → Advanced → **Check CurseForge** | |
| Row → **Match to a mod…** | |

**Leave this alone.** `feat/identify-consolidation` was literally "one action instead of six", and the
Advanced items survived on purpose under *guard, don't hide* — each explains itself when it cannot run
rather than disappearing. The one thing worth revisiting is that **Advanced** is a poor name for
"narrower versions of the thing above", but that is a labelling fix, not a restructure.

### Refreshing — two doors, one candidate to fold

| Surface | |
|---|---|
| Toolbar **↻ Refresh** | rescans mods |
| More → **Re-detect launchers & frameworks** | rescans loaders |

A user who wants "look again" has to know which kind of looking they need. `feat/home-titlebar-refresh-ux`
already folded *Rescan* into *Refresh*; this is the same fold left half done. **Candidate:** one
Refresh that does both, with re-detect surviving only if there is a real cost reason to separate them.

### Not duplication, and worth saying so

**Check setup** appears in the More menu and in a banner. That is one action with a contextual
shortcut, which is right. Same for **Take them over** in the Vortex banner.

---

---

## Layout and usefulness — the wider half

Este, mid-audit: *"It's not just overlap I want the round table to look at. It's overall usefulness
and layout."* So the sharper question is not "are these two the same" but **"does this earn permanent
space, and is it where a person would look for it?"**

### Twelve of the thirteen toolbar controls are shown unconditionally

Measured on `MainWindow.xaml`: every control in the mod toolbar is always visible except
**Browse Nexus**, which alone binds a visibility.

| Control | Applies when | Shown |
|---|---|---|
| Enable all / Disable all | there are mods | always |
| **Browse Nexus** | the game has a Nexus domain AND the plugin loaded | **conditionally** |
| Find mods | always | always |
| + Add mods | always | always |
| Filter mods | the list is long enough to need it | always |
| Loadout **All / MP / SP** | mods carry MP/SP tags | always |
| Refresh | always | always |
| Reorder | load order matters for this engine | always |
| Profiles | you have more than one loadout | always |
| Saves | the game has a save profile | always |

**So the toolbar is the same width for a game with 27 mods and a game with none.** MP and SP segments
sit there for a single-player-only game; Reorder sits there where load order is meaningless; Saves
sits there for a game with no save tree. None of it is broken, and all of it costs the same thing:
the user reads eleven controls to find the two that apply.

That is the layout question, and it is bigger than the duplication one. **Browse Nexus is the only
control that already knows when it does not apply** — which makes it the pattern to argue about rather
than an oddity. Its own failure mode is instructive too: it vanishes silently, so the app looks like
it never had the feature (see above).

### Two ways a control can earn its place

- **Always relevant** — Enable all, Add mods, Refresh, Filter. These belong where they are.
- **Relevant sometimes** — Reorder, Saves, MP/SP, Browse Nexus. These want a rule, and the round
  table should pick ONE: hide when inapplicable (honest, but things move), disable with a reason
  (stable, but greyed clutter), or demote to a menu (stable and quiet, but discoverability drops).

This project already has a rule for the third case — *guard, don't hide* — chosen deliberately for the
identify actions so each explains itself rather than disappearing. Whether the same rule fits a
toolbar, where space is the scarce thing rather than clarity, is exactly what a round table is for.

### What is missing from the layout, not just what is over-represented

Worth putting on the same agenda: the mod view has no place for **what state this game is in**. Today
that arrives as banners stacked above the list — Steam updated the game, Vortex manages this folder,
this game uses anti-cheat, a framework is missing — each a full-width bar competing with the mods for
the top of the screen. On a game with three conditions, the list starts halfway down.

## The pattern under all three

Every one of these is the same shape: **two entries that a user would describe with the same sentence,
distinguished by a capability difference the labels never mention.** Not clutter — clutter is
harmless. This actively teaches people that some buttons are unpredictable, and the response to an
unpredictable button is to stop pressing it.

Which is Este's point: the goal is not fewer controls, it is controls whose names say what happens.

---

## For the round table

1. **Does *Find mods* become one door?** Recommended yes, with the website as a choice inside it.
2. **What is *Advanced* actually called?** It holds narrower versions of the action above it, and
   "Advanced" implies risk that is not there.
3. **Does Refresh absorb re-detect?** Needs a cost answer: if re-detecting loaders is expensive on a
   large install, keeping it separate is a real reason rather than an accident.
4. **What is the rule going forward?** Proposed: *if two controls would be described by a user with
   the same sentence, they are one control with an option inside it.* Cheap to state, and it would
   have caught all three of these before they shipped.
5. **What earns permanent space?** Pick one rule for sometimes-relevant controls - hide, disable with
   a reason, or demote - and apply it to Reorder, Saves, MP/SP and Browse Nexus together rather than
   deciding each one on its own.
6. **Where does game STATE live?** Banners currently take the top of the mod view, one full-width bar
   per condition. Three conditions push the mod list halfway down the screen.

Nothing here is a build instruction. Item 4 is the one worth settling first, because it decides the
other three without needing a meeting about each.
