# Settings — an organising principle

**Date:** 2026-08-18 · **Entry:** D1 · **Status:** proposal, needs Este's call before any build

## What's in there today

Everything, in one scrolling column, in the order it was built. Seven headings across 285 lines of
XAML:

| # | Heading | What it actually holds |
|---|---|---|
| 1 | **Identity** | avatar picker, extracted palette, "use as icon", "derive a theme", theme name |
| 2 | *(unlabelled)* | window transparency |
| 3 | **Nexus Mods** | connect / disconnect, auto-check mod updates, keep plugins updated, install-or-refresh the plugin |
| 4 | **About** | version, attribution |
| 5 | *(under About)* | **installed tools**, **installed frameworks**, **direct-inject mod configs** |
| 6 | **Reset** | Safe Clear |
| 7 | **Restore points** | list, restore, delete |

## The actual problem, and it isn't tidiness

**Three of those are not settings.** Safe Clear resets your launcher. Restore points restore a game.
"Install / refresh Nexus plugin" installs software. Those are *actions with consequences*, filed
between a checkbox about theme colours and a version string. The dialog has no way to say "this one
is different", so it says nothing, and the most destructive thing in the app sits in the same visual
register as a preference.

**And three more are not settings either — they're inventories.** Installed tools, installed
frameworks and direct-inject mod configs are *lists of what you have*, parked under **About** because
About was the last heading. They answer "what is installed" — a library question, not a preference.

So the column mixes four different kinds of thing with one presentation.

## The proposal

Four groups, ordered by how often a person touches them, with a hard line between the first three
and the fourth.

### 1. Appearance
Avatar, derived palette, theme name, window transparency.
*Touched often, reversible instantly, consequences visible immediately.*

Fixes **D3** in passing: the avatar box seeds with the app's own icon instead of an empty square.

### 2. Accounts
Nexus connect / disconnect and the state that belongs to it — auto-check updates, keep plugins
updated. "Install / refresh Nexus plugin" **moves out** and becomes a repair affordance shown next to
the connection state only when something is wrong with it, rather than a button everyone must ignore.

### 3. What's installed
Tools, frameworks, direct-inject configs — the three inventories, together, under a heading that says
what they are. Out from under About, where they were never About.

### 4. Danger
Safe Clear and restore points, below a visible separation, with the danger styling the design
language already defines for confirm dialogs. Not hidden — hiding a destructive action is its own
problem — but visually unmistakable as a different kind of thing.

**About** stops being a heading and becomes a footer line: version, attribution link, the NOTICE
pointer. It is the least-touched thing in the dialog and currently sits in the middle.

## The questions I can't answer for you

1. **Is Settings the right home for the inventories at all?** They are arguably library surfaces —
   "what tools do I have" is closer to the Tools panel than to a preferences dialog. Moving them out
   is a bigger change than regrouping them, and it may be the right one.
2. **Should the four groups be tabs, sections in one scroll, or a nav rail?** One scroll keeps the
   current model and is cheapest. A rail scales better if Settings keeps growing, and it will — B6,
   E1's consent surface and any per-game defaults all want somewhere to live.
3. **Does per-game state belong here?** Today Settings is entirely app-level. The moment a game
   default exists ("always ask before enabling on ban-risk titles"), the dialog needs a shape for it.

## Sequence, and why it isn't first

The entry says it and the day proved it: *"a redesign that nothing can verify is how the four bugs in
0.18 got out."* Settings has 12 automation ids and the harness drives it open and closed. (An earlier draft said 31 —
that was the app-wide count after the LibraryView conversion, misread as this dialog's. Corrected by
a reviewer who ran the grep instead of trusting the sentence.) Before
moving anything, the harness should assert the **groups** — that each heading exists and that the
danger group carries the controls it claims. Then the move is verifiable rather than hopeful.

**Nothing here is a build instruction yet.** It is the design conversation the entry asked for, and it
wants a decision on question 2 in particular before any XAML moves.
