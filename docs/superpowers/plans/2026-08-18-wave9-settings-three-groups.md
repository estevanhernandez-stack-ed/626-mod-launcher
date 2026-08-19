# Wave 9 — Settings, grouped by what you came for

**Date:** 2026-08-18 · **Item:** the round table's #6 · **Spec:** `2026-08-18-settings-information-architecture-design.md` (D1)

## What's there

One 285-line scroll, nine headings, in this order:

**Identity** → *Extracted palette* → *Window transparency* → **Nexus Mods** → **About** → **Installed
tools** → **Installed frameworks** → **Direct-inject mod configs** → **Reset** → **Restore points**

Two problems. **About sits fourth**, between the account controls and three inventories, so the thing
nobody opens Settings for is above three things they might. And **three inventories occupy the middle**
— lists of what is installed, in a dialog you came to to change a setting.

## The shape

**Appearance · Accounts · Restore points · Reset**, with **About demoted to a footer**.

One scroll, not a rail: **decided by count, not by growth**. A rail puts Danger behind a click, and
bottom-of-scroll is the danger convention — a rail would move the most consequential thing in this
dialog somewhere you have to go looking for it.

### A deviation from D1, stated plainly

D1 says *three groups plus a footer: Appearance / Accounts / Danger*. It also says **restore points are
NOT danger — they are the undo for it, so do not file them under that heading**, and it wants the
harness to assert **nothing else under Danger**. Those two together leave restore points with nowhere
to go, so they get their own heading directly above Reset. The relationship stays legible by proximity:
the thing that undoes it sits immediately above the thing that does it.

Four headings, not three. Everything else about D1's shape holds.

## The inventories leave — but two capabilities live only there

This is the part the memo understates. The inventories are **not** pure duplicates:

| Settings section | Also in ToolsPanel? | The action only Settings has |
|---|---|---|
| Installed tools | yes, with *Configure…* | — genuinely a duplicate |
| Installed frameworks | chips, with *Edit config* | **Uninstall** |
| Direct-inject mod configs | no | **Override…** (config path per mod) |

So deleting all three as written would remove two things the user can do. Nothing gets deleted until
its replacement exists and has been verified:

1. **Framework uninstall moves to the ToolsPanel framework chip** — where the chips already are.
2. **Direct-inject config override moves to the mod row** — it is per-mod configuration and it belongs
   on the mod, which is D1's own argument for moving it.
3. *Then* the three Settings sections go.

## Order of work — the harness goes red first

Same discipline as wave 7's ban-risk id, and D1 asks for it explicitly:

1. **Write the group assertions and run them. They must FAIL.** Three per group: the heading is
   present, the expected control ids sit under it, and nothing else sits under Reset. A test written
   after the move asserts a new layout against a new expectation — green, and worth nothing.
2. Add framework uninstall to ToolsPanel. Verify.
3. Add the direct-inject override to the mod row. Verify.
4. Regroup Settings, delete the three inventories, demote About.
5. Harness green.

## Two things D1 is emphatic about

**Restore points are not danger.** They are the undo *for* danger. Filing them under a Danger heading
teaches the user to fear the control that saves them.

**Do not hide the *install / refresh Nexus plugin* button when things look fine.** Its failure mode is
silent, and it is the control you need precisely when the app's own judgment is the broken thing.
Already true today (it is only disabled while working) — the harness pins it so it stays true.

**Outlined danger at the entry, filled danger only inside the confirm**, per
`.claude/rules/vsm-danger-buttons.md`.

## Done when

- Four headings and a footer, in consequence order, one scroll.
- No capability lost: framework uninstall and direct-inject override both reachable from their new homes.
- Nothing under Reset but the reset control.
- The plugin button is present and enabled with a healthy install.
- Full suite green, harness green — having been red first.

---

## What shipped

Four groups and a footer, in consequence order, one scroll:
**Appearance → Accounts → Restore points → Reset → About (footer).**
`SettingsDialog.xaml` went from **285 lines to 237**, and about twenty of what remains is the comment
explaining what left and why.

**The harness went red first, on all four assertions**, then green after the move. Written the other
way round it would have asserted a brand-new layout against a brand-new expectation.

## Nothing was deleted before its replacement existed

The memo called the inventories duplicates. Two of them were not:

- **Framework Uninstall** now sits on the framework chip in the tools row — with a **confirm the
  Settings version never had**, because a chip in the main window is a far easier thing to hit by
  accident than a button at the bottom of a dialog.
- **Direct-inject config Override** now sits on the mod row it configures. The Settings version listed
  *every* catalog direct-inject mod whether or not you had it — a catalog browser filed under settings.
  It appears only for installed mods now, and it reads the **declared** config paths rather than the
  resolved ones: `Resolve` returns only files that exist, so a row would have offered *"override this
  path"* exactly when the path was already right.

Only then were the three sections deleted, along with eight now-dead members in the code-behind.

## Two harness bugs, both worth keeping

**Geometry lies about anything below the fold.** The order assertion compared
`BoundingRectangle.Top`, and Reset — below the fold of a `MaxHeight=640` ScrollViewer — reported a
degenerate rect with `Top = 0`, so the check declared Reset *above* everything. It failed loudly, which
was luck: the same shape one group shorter would have **passed while measuring nothing**. Order comes
from the UIA tree now, which needs no scrolling and cannot be fooled by clipping.

**`Get-Tree` takes an element, not a tree.** Passing the already-flattened tree back into it threw
`GetFirstChild` overload errors. Trivial, but it is the second time this session a helper was handed
the wrong shape and reported something unrelated.

## One green not banked

`settings-inventories-moved-not-deleted` asserts the framework half (Windrose has UE4SS) and reports
**NOT ASSERTED** for the config-override half — no game on this machine has a catalog-known
direct-inject mod installed. A human case (`direct-inject-override-round-trip`) carries the rest.

## Done

- Four groups, one scroll, danger last. Nothing under Reset but `ResetLauncherButton`.
- No capability lost.
- The plugin button is present and enabled, pinned by assertion.
- 2192 tests green, harness **32/32** — having been **4 red** first.
