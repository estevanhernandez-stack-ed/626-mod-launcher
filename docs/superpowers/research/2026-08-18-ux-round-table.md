# The UX round table — four seats

**Date:** 2026-08-18 · **Input:** the two design proposals (D1 Settings IA, D4 surface coherence)
**Status:** four agent reports in, Este's desktop agent pending, synthesis not yet written

Four personas were asked to review the same two proposals and the screens behind them, each told
explicitly **not to agree to be agreeable**. All four found things the proposals missed, and three of
the four found errors in the proposals themselves.

Every load-bearing claim below was verified against the source before being recorded here.

---

## Seat 1 — the feng shui aficionado

*Reads a screen as a room: commanding position, flow, negative space, dead weight.*

### The severity inversion (verified)

Game state renders in three registers, and the weights run **backwards against consequence**:

| What it says | How it renders |
|---|---|
| **anti-cheat — modding online can get your account banned** | inline `Border Padding="8,2"`, right cluster of the command bar, beside the theme picker |
| Some folders here are managed by Vortex | **full-width bar**, pushes the mod list down |
| No mods found / Steam updated this game | **full-width bars** |

> *"A room where the fire exit is a sticker and the coat rack is a wall does not need better stickers.
> It needs the ranking fixed."*

**Verified:** `MainWindow.xaml:316` — the ban-risk warning is a small inline Border carrying
`AutomationProperties.HelpText` and **no `AutomationId`**. The harness can assert the low-stakes
banners exist and cannot cleanly assert the high-stakes one does.

### Four banners, three dismiss grammars

Vortex-owned has *Dismiss*. Vortex-redeployed has **no dismiss at all**. Setup has *Dismiss*. Steam has
*Mark as rechecked*. A person learns "these close with the grey one" and then meets the one that
cannot be closed.

### The toolbar has two policies twelve pixels apart

Left cluster: 13 controls, 1 conditional. Right cluster: 7 controls, 4 conditional. The bar does not
lack a rule — it has two.

### The column widths already made a decision nobody made (verified)

`MainWindow.xaml:152` — column 0 is `*`, column 1 is `Auto`. When the window narrows, `Saves`,
`Profiles` and `Reorder` clip off the edge while the theme picker is guaranteed its space forever.

### Settings

- `Pick image…` and `Reset launcher…` render **identically** — same style, no separation but a 1px
  border. One picks a JPEG; one resets the launcher.
- The heading levels make the three inventories **children of About** — "Installed frameworks", which
  carries per-row Uninstall buttons, is styled as a sub-item of a credits paragraph.
- **Two commit models in one column:** `OnApply` commits only the avatar and derived theme; six of
  seven sections write on interaction. So *Close* cancels nothing, and Enter means something the user
  cannot see once they have scrolled.

### The library home already contains the rule the round table is convening to invent

`LibraryView.xaml:90` — `UpdatesEntryVisibility`, with the comment: *"Present ONLY when something is
actually pending — an empty or never-checked library keeps the home clean rather than advertising a
zero."* That is an answer to "what earns permanent space", written two files from the toolbar that
needs it.

Also: six of eleven games render **twice** on the home (recent strip + all-games list), and the
discovery lane sits *below* an unbounded list, so on a large library it is off the bottom of the
scroll — one of only two ways to add a game.

### If only one change

Collapse game state into **one ranked strip above the mod list, ordered by consequence, ban risk
first** — replacing both the four stacked banners and the four inline warnings in the command bar.

---

## Seat 2 — the power PC user

*200+ mods, three drives, keyboard-first, multiplies every click by his library.*

### The keyboard answer, which reprices everything else (verified)

The whole app has **three accelerators**: `Ctrl+F` filter, `Ctrl+R` refresh, `Esc` back. Plus `Space`
on a focused row. **Zero in Settings.** No keyboard path to Add mods, Browse Nexus, Find mods, Enable
all, Disable all, Profiles, Saves, Reorder, the loadout segments, Settings, the game switcher, or any
per-row action.

**None of the three proposals adds one.** That changes the price of everything: demoting a control to
a menu is cheap when you can hit a key and permanent when you cannot.

### The proposals reverse a decision that was already argued

`MainWindow.xaml:170` carries a comment: *"Browse Nexus is a first-class action, not a menu entry… It
was buried in the Find mods flyout, which is about opening a BROWSER — wrong home, and two clicks
deep."* The one-door proposal reverses that without acknowledging it.

**His counter-proposal:** a `SplitButton`. Primary = in-app storefront (still one click). Chevron =
*Open on the Nexus website*. When the catalog is unavailable the primary falls back to the website
**and says why instead of vanishing** — which is the actual bug.

Also: CurseForge is not a Nexus door, and folding it inside a Nexus button hides a second destination
inside an option.

### Guard-don't-hide is already the house rule, already applied (verified)

`MainViewModel.cs:1453` — entering load-order mode on a direct-inject or loose-root game sets
*"Load order doesn't apply to these mods — they load independently."* and returns. **Reorder already
guards.** So "pick one rule for four controls" collapses to: **Browse Nexus is the only
non-compliant one.**

### What he refuses

- **Hiding anything.** A toolbar whose width depends on game state destroys positional memory. And
  MP/SP relevance derives from per-mod tags **the user can set by hand**, so segments would
  materialise as a side effect of tagging a mod — a control moving under the cursor.
- **Hiding the filter box would break `Ctrl+F`**, which is a third of the keyboard surface.
- **Gating `Saves`** requires eager Ludusavi save-tree detection per game at load time; today that is
  lazy inside the click handler. An unpriced cost.
- **Restore points under a heading called "Danger."** Restore is the *undo for* danger. Filing it
  there teaches people to avoid the recovery path.
- **Outright:** hiding "install / refresh Nexus plugin" until something looks wrong. That is
  hide-when-inapplicable smuggled into a reorganisation, applied to the one control that exists for
  when the app's own judgment is the broken thing — and the plugin's failure mode is *silent*, as
  proven on this machine today.

---

## Seat 3 — the first-time modder

*Never modded anything. Wants one mod working. Worried about breaking a game they paid for.*

### The empty mod list says nothing at all (verified)

`MainViewModel.cs:423` — `EmptyVisibility => HasGame ? Collapsed : Visible`. So the "No game
registered yet" message does not apply once a game exists, and `FilterEmptyText` only fires on a
search miss. **A registered game with zero mods renders an empty rectangle with no words** — at
exactly the moment the app most needs to say "drop a .zip here".

### The give-up moment

> *"At the red `NEEDS UE4SS` chip, seconds after the toggle went on. Not at an error — nothing failed.
> I got the game added, the mod in, I flipped the switch, and the app said on. Then a red word I have
> never seen told me it isn't really on, in a sentence made of three more words I've never seen, and
> its only offer was a GitHub page full of files I can't choose between."*

The chip says **NEEDS**, not *get it and drop it here and I will install it* — which the app is fully
capable of doing.

### The pattern that works, stated better than the proposals state it

`LOADER`'s tooltip says *"toggling it turns the whole modded setup off or on"* — it explains the
**consequence**, not the definition, and it works even though the reader does not know what a proxy
DLL is. `BAN RISK` does the same. **Consequence beats definition**, and that is the pattern every
other chip is missing.

### Words with no way in

`UE4SS`, `pak`, `Lua mods`, `Blueprint LogicMods`, `proxy DLL`, `ASI plugin`, `dinput8.dll`,
**`loadout`**, `load order`, `direct-inject`, **`framework`** (the thing blocking them, never
defined), `engine profile`, `Nexus domain`, `Mod Engine 2`, `Vortex`, `config cockpit`, `Curated`.

- **`Profiles` tooltip is "Saved loadouts"** — an unknown word defined by another unknown word, and
  nothing anywhere defines *loadout*.
- **`LIBRARY` means two things one click apart** — my games on the home, a toolbar group in the game
  view. The app's own top-level noun changing under the reader.
- The chip glossary does not cover `NEEDS ___`, `UPDATE`, `Curated`, or `BAN RISK` — the chips that
  actually stop people.

### A dialog that defaults to the thing it just warned against (verified)

`FrameworkUnrecognizedNudgeDialog.xaml` — body says *"This usually doesn't work for frameworks"*, and
`PrimaryButtonText="Continue as mod"` with `DefaultButton="Primary"`. Press Enter, do the warned-against
thing.

### Where a scared person goes

Settings, looking for undo. Finds **Reset launcher…** and **Restore points** — the two most
reassuring-sounding controls in the app, neither of which puts their *game* back. And the real undo
(toggling off moves files aside rather than deleting) is never advertised anywhere.

---

## Seat 4 — the reluctant switcher

*Came from Vortex/MO2, did not want to, compares constantly, barred from nostalgia.*

### The most serious finding in the whole table (verified)

**The MP/SP segments are not a filter.** `MainViewModel.SetMode` → `Scanner.ApplyMode`:

```csharp
foreach (var m in ListWithClass(c)) {
    if (m.Enabled && !want) DisableEntry(m, c);
    else if (!m.Enabled && want) EnableMod(m.Name, c);
}
```

It **enables and disables every mod in the game**. And `SetMode` returns early — cosmetic highlight
only — for Mod Engine 2, direct-inject and loose-root games.

So three identically-styled segmented buttons are a **full bulk file operation** on one game and a
**no-op** on the next, in a control shape that means *change what I see* in every other application.
The XAML comment calls it a filter. Both design proposals call it a filter.

*(In the app's favour, and the seat did not note it: `SetMode` does pass the ban-risk gate once before
the bulk apply.)*

### `Enable all` / `Disable all` lose state that is not recoverable

The file operations are reversible; **the knowledge of which 140 of 200 mods were on is not**. `Enable
all` does not undo `Disable all`. The fix already exists and is not connected: a profile saves exactly
that set. Snapshot before the bulk op.

### Four words for one concept, and one word for four concepts

`LOADOUT` (toolbar section), `Profiles` (button, tooltip "Saved loadouts"), `ProfilesDialog`
(eyebrow `LOADOUTS // PROFILES`, five labels across two words), and `GameProfile` in Core (an unrelated
engine descriptor).

> *"If two words name the same object, one of them is wrong."* — extending the proposal's own rule
> from controls to nouns.

A Vortex profile also carries its own load order, INIs and saves; 626's carries the first third. That
is a legitimate scope choice being charged as a translation cost for no benefit.

### The gap that decides whether 200 mods move

Vortex has **Purge**. MO2 has *close MO2*. 626 has per-mod reversibility — which is not the same
shape. **There is no "put this game back the way it was on Tuesday."**

And the machinery already exists: `RestorePointManifest` is sealed, complete-flagged, refuses an
incomplete manifest, captures launch targets, frameworks, loader mods, moved files and saves — *"more
care than Vortex's uninstall shows"*. It is wired to exactly one moment: **the moment you are
leaving.** Point it at the moments you are arriving — before a takeover, before `Disable all`, before
`Apply order`, before a framework install — and rename it so the word matches.

### No conflict detection at all

Nothing tells you mod B overwrote three files mod A placed. `ReplacedStore` keeps the overwritten
bytes, so the safety net is **file-level**; Vortex's and MO2's is **informational**. Both matter and
626 has one. The mod-provenance spec is the right foundation and is still awaiting review — *"until it
lands, reversible by default is true per-file and not yet true per-mod."*

### No downloads store

Read once for hash-matching during identify, then forgotten. What an archive store buys: reinstall
without re-downloading, reinstall a *specific old version* after a bad update, and a rebuild path when
the game folder is nuked.

### What earned trust immediately

Reading the game folder instead of demanding an import — *"that is not a translation of Vortex's
import, it is a different answer to the same question, and it is the better one"*. Archiving Vortex's
marker rather than deleting it, and anticipating the redeploy. Refusing to delete another tool's
files. The ban-risk chip on the library row, **before** you invest in a game.

### Two things that would make him commit

1. **A snapshot he can take on purpose**, before doing something stupid. *"I do not need a VFS if I can
   get Tuesday back."*
2. **Ship provenance, and let it report overlaps.**

### The one thing that would send him back

The MP/SP segments.

---

## Where the seats genuinely disagree

This is the part worth deciding rather than averaging.

| Question | Feng shui | Power user | New modder | Switcher |
|---|---|---|---|---|
| Hide inapplicable controls? | **yes** — dead weight hides the living | **refuse** — reflow destroys positional memory, breaks `Ctrl+F` | doesn't say | **no** — disable with the reason stated in the control |
| Is the toolbar the main problem? | no, state ranking is | it is mostly already solved | no, the vocabulary is | no, MP/SP is |
| Settings: tabs, scroll or rail? | **one scroll** — a rail routes faster to a misplaced corner | **scroll**, or a rail only if `Ctrl+1..4` ships with it | doesn't say | **rail** — it will grow |
| Where do the inventories go? | out of Settings eventually, regroup first | regroup, fine either way | doesn't say | **out** — two libraries, one hidden behind a gear |

Three of four converge on one thing the proposals got wrong: **stop hiding `Browse Nexus`** is the fix,
not merging the doors.

---

## What the table found that neither proposal contained

1. **MP/SP is a bulk file operation wearing a view control.** (switcher)
2. **The ban-risk warning is the smallest state indicator on the screen.** (feng shui)
3. **Three keyboard accelerators exist, and no proposal adds one.** (power user)
4. **A registered game with no mods renders a blank rectangle.** (new modder)
5. **`Enable all` / `Disable all` destroy state that nothing restores.** (switcher)
6. **The restore-point machinery is pointed at the exit instead of the entrance.** (switcher)
7. **Settings has two commit models in one column.** (feng shui)
8. **The unrecognized-framework dialog defaults to the action it warns against.** (new modder)

## What the table found wrong in the proposals

1. The one-door merge **reverses a documented decision**, and a SplitButton gets the benefit without
   the regression. (power user)
2. `Reorder` **already implements guard-don't-hide**; the table's "always shown" was true and
   misleading. (power user)
3. **Restore points are not danger** — they are the undo for it. (power user)
4. Hiding the plugin-refresh button **contradicts the rule the same round table is being asked to
   adopt**, and targets the one control needed when the app's own judgment is broken. (power user)
5. Both proposals **call MP/SP a filter**. (switcher)
