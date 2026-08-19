# Wave 7 — one place for what's true about this game

**Date:** 2026-08-18 · **Item:** the round table's #2 · **Size:** the one that wants a real plan
**Convergence:** three of five seats arrived at this independently, from three different directions.

## What's actually there

Eight state surfaces, in two registers, with three dismiss grammars.

### Four full-width banners, in declaration order

| Binding | Says | Ends with |
|---|---|---|
| `OwnedBannerVisibility` | Some folders here are managed by Vortex | *Take them over* · **Dismiss** |
| `ReDeployedBannerVisibility` | Vortex re-deployed into a folder you took over | *Take over again* · **nothing** |
| `SetupBannerVisibility` | No mods found, and the folder this game looks in doesn't exist | *Check setup* · **Dismiss** |
| `SteamBuildWarningVisibility` | Steam updated this game | **Mark as rechecked** |

Each pushes the mod list down. Three ways to make one go away, one that cannot. A person learns
"these close with the grey button" and then meets the one that doesn't.

### Four inline warnings, in the command bar's right cluster

| Binding | Says | Weight |
|---|---|---|
| `CoopHintVisibility` | Co-op needs its launcher | danger text |
| `MpWarningVisibility` | Some enabled mods may desync co-op | bound text |
| **`BanRiskWarningVisibility`** | **This game uses anti-cheat — modding online can get your account banned** | `Border Padding="8,2"`, **no `AutomationId`** |
| `LaunchHintVisibility` | Launch options | accent text |

### And one that renders nowhere at all

`HasMissingFrameworks` and `MissingFrameworksSummary` exist on the view-model and are **bound in no
XAML file**. Wave 3 taught that summary to say *"UE4SS — loader present, runtime missing"* instead of
*"Missing: UE4SS"* — a sentence computed on every reload and displayed by nothing. The state reaches
the user only as per-row `NEEDS X` chips, which is item 5 of the order and a different job.

## The problem, stated once

**The weights run backwards against consequence.** The thing that can cost someone their account is
the smallest indicator on the screen, wedged between the launch-options hint and the theme picker. The
thing that costs nothing — another tool has files in a folder — is a full-width bar that takes space
from the mod list.

> *"A room where the fire exit is a sticker and the coat rack is a wall does not need better stickers.
> It needs the ranking fixed."*

## The shape

**One strip above the mod list. One chip per condition. Ordered by consequence. Tap to expand.**

The ordering is the part worth arguing about, so it goes in Core as a pure decision with the rationale
attached, not scattered across XAML in declaration order:

| # | Chip | Why here |
|---|---|---|
| 1 | **Ban risk** | the only one that can cost something outside the machine |
| 2 | **Launch options needed** | mods do not load at all until this is set |
| 3 | **Framework missing** | the mods that need it do not load — *and this has no surface today* |
| 4 | **Setup drift** | the launcher is looking somewhere the mods are not |
| 5 | **Steam updated** | what worked yesterday may not today |
| 6 | **Co-op launcher missing** | co-op specifically is broken |
| 7 | **MP desync risk** | affects other people, not this install |
| 8 | **Vortex re-deployed** | a takeover was undone behind the user's back |
| 9 | **Vortex managed** | informational: someone else owns this folder |

**This ordering is a judgement call and Este can reorder it.** The defensible principle: *what breaks,
and how far outside this machine does the damage reach.* Account first, then "nothing loads", then
"some things do not load", then "this may be stale", then "other players", then "another tool".

## What Core owns

A pure function: given the conditions that hold, return the chips in order. That puts the ranking under
test, keeps XAML from re-deciding it by declaration order, and gives the App one list to render.

Each chip carries: a stable id (for automation), a severity, a short label, the sentence it expands to,
and what the action is called if it has one.

## What the App owns

One templated control, rendering that list. **Four one-off banners become one template — this wave
should DELETE more XAML than it adds**, and if it does not, the shape is wrong.

## Three constraints

1. **`BanRiskBanner` gets an `AutomationId` before it moves.** It has `HelpText` and no id today, so
   the harness can assert the low-stakes banners exist and cannot cleanly assert the high-stakes one
   does. Moving it first would mean the change is verified everywhere except at the place that matters.
2. **One dismiss grammar.** Proposed: a chip is dismissible only when dismissing hides nothing that is
   still true — so *Vortex managed* and *setup drift* can be dismissed for the session as they are
   today, *Steam updated* keeps **Mark as rechecked** because that is an action and not a dismissal,
   and **ban risk is never dismissible**. Say the rule once rather than per banner.
3. **Leave room for per-game defaults.** D1's question 3 — *"always ask before enabling on ban-risk
   titles"* — lands on this strip rather than in Settings. Not built here; the shape must not preclude it.

## Tests

The ordering, exhaustively: every condition alone produces its chip; ban risk outranks everything;
adding a lower-severity condition never displaces a higher one; no condition at all produces an empty
strip rather than an empty container; and the chip ids are stable, because a harness will key on them.

Plus the harness: a game with a known condition shows its chip, and — the assertion that would have
caught the original fault — **the ban-risk chip is present and addressable when the game is high risk.**

## Done when

- One strip, one row, ordered by consequence, replacing four banners and four inline warnings.
- The ban-risk warning is addressable by id and is the first thing in the strip.
- `MissingFrameworksSummary` finally renders somewhere.
- The diff removes more XAML than it adds.
- Full suite green, `CorePurityTests` green, harness green.
- Verified on the rig against a game with real conditions — Windrose carries Vortex markers and a Steam
  build stamp; Monster Hunter Wilds is high ban-risk.
