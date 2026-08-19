# Wave 8 — say what you can do

**Date:** 2026-08-18 · **Items:** the round table's #3, #4 and #5 · **Size:** three small independents

They looked like three unrelated bugs. They are one habit: **the app goes quiet exactly where it has
something useful to say.** A capability it has, hidden. A state it knows, unlabelled. An action it can
perform, offered as a link to someone else's website.

---

## Item 3 — Browse Nexus stops vanishing

`BrowseNexusButton` binds `CatalogVisibility` and **disappears entirely** when the capability is not
available. Three different situations collapse into the same nothing:

```csharp
public bool CatalogAvailable =>
    NexusActionsAvailable && NexusSource is IModCatalog && ActiveGameHasNexusDomain;
```

| Why it is false | What the user should do | What the app does today |
|---|---|---|
| Not signed in to Nexus | sign in — one dialog away | shows nothing |
| Plugin missing or too old | install it from Settings | shows nothing |
| Game has no Nexus page | nothing; it does not apply here | shows nothing |

The first two are **one step from working** and the app says nothing about either — so it presents as
though the in-app storefront was never built. The third genuinely does not apply.

**The rule:** hide a capability only when it can never work *here*. Where it is one step away, show it
and name the step.

So the button stays visible whenever the game has a Nexus domain; when it is not usable it says why in
the label and pressing it opens the remedy rather than doing nothing.

**And the two doors get told apart in the LABEL, not the hover** — the round table's rule, and the
concrete complaint that two controls both reading *find mods* land in completely different places:

| | Today | After |
|---|---|---|
| In-app storefront | `Browse Nexus` | **Find mods (in-app)** |
| Opens a browser | `Find mods` | **Find mods in browser** |

**Not merged into one `DropDownButton`.** That reverses a decision documented at `MainWindow.xaml:170`
and costs the power user a click on his most-used path.

## Item 4 — the empty mod list says something

`EmptyVisibility` is `HasGame ? Collapsed : Visible`, and `FilterEmptyText` only fires on a search
miss. So a registered game with **zero mods** renders a blank rectangle with no words — at exactly the
moment the app most needs to say *drop a zip here, or use + Add mods*.

**Wave 6 opened a second hole in the same place.** The MP/SP segments are a filter now, and
`filteredToNothing` requires `!string.IsNullOrWhiteSpace(ModFilterText)`. Filter to MP on a game whose
mods are all single-player and the list goes blank and wordless — and the control that did it is a
view control, so it reads as *the mods are gone*.

One pure rule in Core covers all four cases: no game, no mods, search matched nothing, mode matched
nothing. Every branch names what is true **and what to press**.

## Item 5 — the `NEEDS ___` chip offers the action it can already perform

This is where the round table's first-time modder closes the app, seconds after a successful toggle: a
red chip naming a thing they have never heard of, whose only offer is a `HyperlinkButton` to a GitHub
releases page full of files they cannot choose between.

**The app can install it.** Drop the right zip on the window and `AddModsAsync` classifies it,
`FrameworkInstallDialog` shows exactly what lands where, and `FrameworkInstaller.Install`
validate-then-extracts it. The chip has never mentioned that.

The chip becomes a button onto a small dialog that states the consequence and offers both doors:

- **Get it** — opens the download page (what the link does today).
- **I already have the file** — the same picker `+ Add mods` uses, feeding the same intake. Not a new
  install path; the existing one, finally reachable from the place that says it is needed.

**And it fixes wave 7's placeholder.** The framework chip in the game-state strip has an action button
labelled *Get it* that opens the same browser page — flagged in that PR as item 5's job. Both now open
this dialog.

**Copy pattern, from the round table:** the `LOADER` and `BAN RISK` chips work for a newcomer because
they state the **consequence**, not the definition. So: *"Mods that need UE4SS will not load until it
is installed"* — never *"UE4SS is a script loader for Unreal Engine games."*

---

## What Core owns

Three pure decisions, three sets of tests:

- **`ModBrowseRules`** — given connected / catalog-capable / has-domain, what the button says, whether
  it acts or remedies, and which remedy.
- **`ModListEmptyState`** — given game / total / visible / search / mode, the sentence, or null.
- **`FrameworkOffer`** — what the chip says and which doors it opens, given the framework and whether
  the app can install it.

## What the App owns

The three dialogs and the picker. No new install path — item 5 reuses `AddModsAsync` exactly.

## Tests

Core, exhaustively per rule. Plus harness cases: the browse button is present-and-labelled on a game
with a Nexus domain regardless of connection state; the empty list is never wordless; the `NEEDS` chip
is invokable. Every new control ships its `AutomationId` in the same commit.

## Done when

- No control vanishes for a reason the user could fix in one step.
- No state in the mod list is rendered as a blank rectangle.
- The `NEEDS` chip offers the install the app can already perform, and says what happens if it does not.
- Full suite green, `CorePurityTests` green, harness green.
- Verified on the rig: Windrose (27 mods, UE4SS, MP/SP tags) and a game with no mods.

---

## What shipped

All three, plus the fix wave 7 deferred.

**Item 3.** `BrowseNexusButton` shows whenever the game has a Nexus domain. Signed out or plugin
missing, it stays visible, says which of the two it is, and the press opens Settings — because a
visible control that does nothing when pressed is a worse lie than the vanishing one. A game Nexus has
no page for still hides it: the rule is *hide only when it can never work here*, not *never hide*.

The two doors read **Find mods (in-app)** and **Find mods in browser**, each with its own glyph. Not
merged — that would reverse the decision at `MainWindow.xaml:170` and cost a click on the power user's
most-used path.

**Item 4.** Two `TextBlock`s became one, and the two holes between them closed. `EmptyVisibility` is
gone entirely — a boolean cannot answer a four-way question.

**Item 5.** The chip is a `Button` onto an offer with both doors: *Get it* (the old link) and
*I already have the file* (the picker `+ Add mods` uses, feeding the same `AddModsAsync`). No new
install path — the existing one, finally reachable from the place that says it is needed. Wave 7's
placeholder on the strip's `FRAMEWORK` chip now opens the same offer, so two surfaces cannot drift.

## Two harness bugs, no app bugs

Both failures on the first run were the harness's, and both are worth keeping:

**`Get-Text` on a button reads its accessible NAME.** The assertion read the *detail sentence*, not the
label, because I had bound `Name` to the explanation. Wrong for a button whose label already names its
action — `Name` is the label now and the sentence moved to `HelpText`. (Wave 7's chips keep the
sentence as `Name`, and that stays right: `BAN RISK` alone tells a newcomer nothing.)

**`SendKeys` needs an assembly load and trusts the shell's idea of foreground.** Replaced with
`Set-EditValue`, a `ValuePattern` helper in `uia-lib.ps1` that needs neither and cannot land keystrokes
in another window.

## One green not banked

`needs-chip-is-invokable` **passes without asserting anything on this machine** — every framework a mod
needs is installed here, so it returns *"nothing is missing a framework"*. It is recorded **pending**,
not verified. A green that never ran is the cherry-picked denominator wearing a different hat.

## Done

- 2192 tests green (24 new), `CorePurityTests` green, harness **28/28**.
- Verified on the rig: the two doors read differently and both are addressable; filtering to a string
  no mod matches says so by name instead of going blank.
