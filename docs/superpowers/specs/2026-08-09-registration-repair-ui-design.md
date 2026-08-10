# Registration repair — the surfaces

**Date:** 2026-08-09
**Backlog:** A2 (*No in-app way to repair a game registration*) — the UI half
**Scope:** App layer, plus three small Core additions. Spec 1 (`2026-08-09-registration-repair-core-design.md`) built the primitives.
**Status:** approved design, ready for a plan

---

## Why

Spec 1 landed three Core primitives — the `userSet` marker, `DataDirMove`, and
`RegistrationChange.Plan` — and **nothing consumes any of them.** The launcher can now compute
exactly what an edit would do; there is still no way to ask it.

Meanwhile `GameShape` (shipped earlier, exposed as the `get_game_shape` MCP tool) can already state
the truth about a registration, and the App layer does not consume that either. So the launcher holds
the diagnosis and the repair machinery, and a user holds neither.

**Live case.** Elden Ring declares a Mod Engine 2 `mod` folder that does not exist, while all eleven
of its mods load by direct-inject under `Game\`. `get_game_shape` reports it as `Drifted` and says
outright that the install is healthy. A user cannot see that, and cannot act on it.

---

## What this ships

One banner, one dialog, one confirm — plus three Core additions, each because the design forbids the
UI from working an answer out for itself.

### The banner

A Vortex-style strip above the mod list, shown only when drift is provably costing something:

```text
ModCount == 0  AND  at least one declared location does not exist
```

> No mods found here, and the folder this game is set to look in doesn't exist.
> **[ Check setup ]** **[ Dismiss ]**

Same grammar as the existing Vortex banner: a statement of fact, never a question; one verb button;
an optional Dismiss; escalation by `BorderBrush` only. Dismiss collapses it for the session, matching
`OnDismissVortexBanner` — a later rescan may re-show it, which is acceptable and already precedent.

After a successful repair the banner re-evaluates for free: `ReloadModsAsync` recomputes the shape, so
a fixed registration stops tripping the predicate without anything explicitly clearing the banner.

**Wording, fixed across all three entry points** so they read as one feature rather than three:

| surface | text |
|---|---|
| More menu item | `Check setup…` |
| Banner button | `Check setup` |
| Dialog eyebrow / title | `GAME // SETUP` / `Setup` |
| Expander inside the dialog | `Edit setup…` |

"Check" rather than "Edit" at the entry points is deliberate: the common outcome is *nothing is
wrong*, and an entry point that promises editing implies something needs to be edited.

**Why this predicate and not "drift".** Drift is common and usually harmless. Elden Ring is drifted
and perfectly healthy; so is any loader-based install. Surfacing every drift would flag working games
and train people to dismiss the banner — the launcher crying wolf about something it already handles
correctly. The predicate above fires on the shape that actually hurt: a Cyberpunk registration whose
194 `.archive` mods showed as zero.

The predicate lives on `GameShape` in Core, not in the view-model, so the banner and the dialog's
verdict come from one place and cannot disagree.

### The dialog — `GAME // SETUP`

House style throughout: 3px accent rail bleeding to the dialog edges, mono-caps eyebrow, title,
`ScrollViewer` body at width 440, `DialogTheming.Apply` in the constructor.

**Top half — the diagnosis, read-only, rendered straight from `GameShape`:**

```text
  Mods found        11, all working
  Loaded by         Elden Mod Loader
  Living in         Game\mods\
  Set to look in    mod  (this folder doesn't exist)

  Nothing is wrong. This game's mods load by a route the setup
  doesn't describe, which is normal here.
```

The verdict line is `GameShape.Notes`, which already says drift is a description and not a defect. The
launcher tells the user the truth it already holds, instead of inventing a repair for a working
install.

**Bottom half — an `Expander` labelled "Edit setup…", collapsed by default**, holding eight fields:
game name, game folder (with Browse), engine, mod folder, file extensions, grouping rule, Steam app
id, required launcher.

Beneath the fields, a consequences panel bound live to `RegistrationChangePlan` as the user types.
`Save` is disabled while `Blockers` is non-empty, and the blocker text is shown.

**Why one dialog instead of two.** WinUI 3 permits one `ContentDialog` per `XamlRoot`. A separate
diagnose dialog handing off to a separate edit dialog handing off to a move confirm is two chained
flag-then-`Hide` hand-offs, which is the fragile part of this build. Collapsing the first two into one
scrolling surface leaves exactly one nesting boundary — and it lets the user read *"nothing is wrong
here"* and the current values together, which is the point of diagnosing before editing.

### The confirm

The move-or-pin prompt is the only hand-off: a flag property set on the dialog, `Hide()`, and
`MainWindow` reads it after `ShowAsync` returns — the canonical `SettingsDialog` pattern.

---

## The Core additions

### 1. `GameShape.NeedsAttention`

`ModCount == 0 && DeclaredLocations.Any(d => !d.Exists)`. Trivial, and it belongs in Core so a test
can pin that a healthy drifted game never trips it.

### 2. `RegistrationChangePlan.OtherChanges`

`FieldsChanged` is deliberately the four pinnable fields only. Spec 1's final review warned that a UI
rendering it as "here is what will change" would lie — and with name, Steam id, and required launcher
now editable, it would: rename a game and the consequences panel sits blank while something real
happens.

So a second list, for changes that are real but carry no pin and no move:

```text
FieldsChanged   [modLocations]          -> pinned; outranks future corrections
OtherChanges    [gameName, steamAppId]  -> saved; nothing further
FieldsToPin     [modLocations]          -> written to userSet on save
```

Three lists answering three questions. The dialog renders all three and decides none of them.

### 3. A progress callback on `DataDirMove.Execute`

An optional `IProgress<(int Copied, int Total)>`, reported per file on the copy path only — a rename
is instantaneous and reports nothing. Null keeps every existing call site and all current tests
unchanged, the same trick `userSet`'s `false` default uses.

**Deliberately NOT added: cancellation.** `Execute`'s whole safety argument is that the source
survives until the target is verified. A cancel between the swap and the source delete would leave two
complete copies and no owner. The repo's own rule — atomic file ops must not be interruptible
mid-flight — is at its clearest here. The status bar ticks `Moving launcher data: 412 of 1,204 files.`
with no Stop button.

---

## The save path

**The move happens first; the registry write second.**

That ordering is what makes failure recoverable. If `Execute` fails, nothing has been written
anywhere — registration untouched, data untouched — and the dialog stays open with the user's typed
edits intact (`ToolConfigureDialog`'s `args.Cancel = true` precedent).

**If the registry write fails after a successful move**, the data would sit at the new path while the
registration still points at the old — orphaning the exact files this feature exists to protect. So
that case **reverses the move**: re-plan in the opposite direction with
`DataDirMove.Plan(newPath, oldPath)` and execute that. It is a fresh plan, not a stored inverse, so the
reverse gets the same refusals and the same free-space check as the forward trip — and because the
forward move emptied the old location, the "never merge two data dirs" refusal will not fire on it.

If the reverse ALSO fails, stop and surface both absolute paths in the status line. Two failures in a
row means the disk is in a state the launcher should not keep writing to, and a user who is told
exactly where their files are can recover by hand. Silence is the only unacceptable outcome.

**Pin needs no file operation at all.** Choosing "leave it where it is" writes `proposed.DataDir` =
the currently-resolved path, which `Scanner.DataDirForGame` already honours ahead of its derivation.
Nothing moves; the folder keeps working from where it sits. That is why pin is the safe fallback.

Then, in order: `UserSet` = `plan.FieldsToPin` → `Registry.UpsertGame` → `RegistryStore.Save` (atomic
temp + rename) → `RegistryChanged` → `ReloadModsAsync`.

---

## Where the logic lives

Flow logic goes in a thin App-side `RegistrationRepairService`, not in `MainViewModel`.

`MainViewModel` has 14 concrete service dependencies and is unconstructible in tests. Three times in
recent work, a decision parked there accumulated defects until it was extracted to Core
(`IdentifyRunReport`, `LongOperationSlot`, `StatusHold`). The dialogs stay dumb renderers; anything
that decides something lives in Core behind a test.

---

## Testing

The App layer is headless-untestable. The split is explicit rather than aspirational.

**Core, real tests:**

- `NeedsAttention` — a healthy drifted game (11 mods, missing declared location) never trips it; a
  zero-mods game with a missing declared location does; a zero-mods game whose declared location
  exists does not.
- `OtherChanges` — a rename reports there and pins nothing; a mod-path change reports in
  `FieldsChanged` and not there; both together populate both lists.
- The progress callback — reports per file on the copy path, reports nothing on a rename, and a null
  callback changes nothing.

**Smoke, appended to `docs/smoke-tests/pending.md`:**

- Elden Ring: More → Edit registration reads "nothing is wrong", names Elden Mod Loader, and shows
  the missing `mod` folder without implying a repair.
- A game with zero mods and a missing declared folder shows the banner; Dismiss collapses it for the
  session.
- Save is disabled while a blocker is present, and the blocker text is visible.
- A real cross-volume data-dir move shows per-file progress and completes; the mods still list
  afterwards.
- Cancel after typing loses nothing and changes nothing on disk.

The riskiest path — a move that fails partway — is already covered by Core tests on `Execute`,
including the file-held-open case. What smoke adds is whether the UI tells the truth about it.

---

## Laws this design is bound by

- **Reversibility.** The move rolls back on a failed write; pin moves nothing at all; cancel changes
  nothing.
- **Pure core.** The predicate, the change lists, and the mover stay in Core. The App renders.
- **camelCase JSON on disk** — `userSet` and `dataDir` both already conform.
- **No nested `ContentDialog`** — exactly one hand-off, via the canonical flag-then-`Hide` pattern.
- **Voice** — builder-to-builder, second person, sentence case, periods. No emoji.

---

## Out of scope

No undo-this-repair, no history of past edits, no multi-game repair sweep, no cancellation of a move
in flight. One game, one edit, reversible because the operations underneath it are.
