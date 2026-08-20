# Confirming in the saves panel

**Date:** 2026-08-19 · **Prompted by:** the restore test on real Palworld saves
**Status:** design, not built

## What prompted it

Restoring a snapshot deletes every file and subdirectory in the save folder and extracts over the
top. It fires on one click with no confirmation. That was the observation; speccing it out found
something worse two rows away.

## The inventory

Every destructive action reachable from `SavesDialog`, and what actually protects it:

| Action | Confirms? | Snapshots first? | Recoverable after? |
|---|---|---|---|
| **Restore** a snapshot | no | yes — `before-restore` | **yes** |
| **Restore one type** | no | yes — `before-restore` | **yes** |
| **Reset** a save mod | no | yes | **yes** |
| **Clone → replace** an existing type | no | yes — `before-clone` | **yes** |
| **Delete** a snapshot | no | nothing to snapshot | **NO** |

### The finding

**The one irreversible action in the panel is the only one that destroys the thing making the others
safe, and it is a single unguarded click on a trash icon sitting next to Restore.**

`SaveManager.Delete` is `File.Delete` — no recycle bin, no holding folder, no undo. Meanwhile every
*reversible* action in the same dialog is protected by an automatic snapshot. So the protection is
inverted: the four operations you can walk back are cushioned, and the one you cannot is bare.

The misclick this invites is specific and bad. Restore and Delete are adjacent on the same row. Aiming
for Restore and hitting Delete means *"I meant to go back to my last good save, and instead
permanently destroyed it."*

It is also **inconsistent with the app's own precedent**: deleting a *restore point* in Settings does
confirm — *"The archived setup will be permanently removed."* Same class of object, same permanence,
opposite treatment.

## The constraint that shapes the solution

`SavesDialog` **is** a `ContentDialog`. A `ContentDialog` cannot be shown from inside another one, and
this codebase already knows it — Settings hands its confirms back to `MainWindow` to run *after* it
closes, with a comment saying exactly why:

> *"The OAuth flow opens a browser and (on a first-ever connect) shows the consent dialog — neither
> can be nested under this ContentDialog, so we hand off."*

So the obvious implementation is the wrong one, and the two available shapes each have a cost:

**Hand-off** (what Settings does): set a request field, close, let `MainWindow` confirm, act, reopen.
Correct, and heavy — the panel disappears and comes back, which for *Delete a snapshot* means losing
your place in a list you were reading. Settings can afford it because those actions end the session
with the dialog anyway.

**Flyout anchored to the button:** a `Flyout` is a popup, not a dialog, so it composes inside a
`ContentDialog`. It appears at the control, keeps the panel and the list on screen, and dismisses on
click-away. **Chosen.** It also matches the physical reality of the mistake: the confirmation appears
at the exact pixel the user aimed at.

## What gets confirmed, and what does not

Over-confirming is its own failure. A dialog that asks twice about everything trains people to click
through without reading, and then the one that mattered gets clicked through too.

**Confirm — `Delete` a snapshot.** Irreversible. Non-negotiable.

**Confirm — `Restore` (whole folder).** Reversible, but it replaces *everything*, and the undo is only
useful to someone who knows `before-restore` exists. The confirm is where they find that out.

**Do not confirm — `Restore one type`, `Reset` a save mod, `Clone → replace`.** All snapshot first,
all are scoped to one file or one mod, and all already say so in the status line afterwards. Adding a
step here buys nothing and spends the user's willingness to read the two that matter.

## The copy

Each confirm names the thing and the consequence, in the pattern the state chips already use — say
what happens, not what the button is called.

**Delete:**
> **Delete this snapshot?**
> `Before mods` · 26.3 MB · taken 19 Aug 17:12
> This one is gone for good — snapshots are the only copy the launcher keeps.
> `[ Delete it ]` `[ Keep it ]`

Naming the snapshot matters: the list is timestamps, and the row you are hovering is easy to lose.

**Restore:**
> **Replace your saves with this snapshot?**
> Everything in the save folder is replaced — 2 worlds, 25.4 MB.
> Your current saves are snapshotted as `before-restore` first, so this is undoable.
> `[ Replace ]` `[ Cancel ]`

The second line is the part worth having. The restore test proved that safety net works; the user has
no way of knowing it exists unless something says so at the moment they are deciding.

Both use **outlined danger on the entry, filled danger inside the confirm**, per
`.claude/rules/vsm-danger-buttons.md` — and that rule's warning applies: a `Style` that sets
`Background` only wins at rest, so the pointer-over and pressed keys must be element-scoped onto the
button using the live theme brushes.

## Tests

**Core** — nothing new. `SaveManager.Delete` and `Restore` are already covered; this is App-layer
consent, not logic.

**The counts in the copy are the testable part.** *"2 worlds, 25.4 MB"* comes from
`SaveManager.ListWorlds` plus a size sum, and a confirm that misreports what it is about to replace is
worse than one that says nothing. That summary belongs in Core as a pure function with tests, not
built inline in the click handler.

**Harness** — one case per confirm, asserting the flyout opens and the destructive call has *not*
happened yet. The existing `Assert-NoModal` will not see a `Flyout` (it looks for `Window`/`Dialog`),
which is correct here and worth a comment so the next person does not "fix" it.

**A human case** for the delete confirm actually deleting, since it is the one action no automated run
should perform against a real snapshot folder.

## Non-goals

- Not adding an undo for snapshot deletion. A trash folder for zips is a second holding area to keep
  swept; the confirm is the proportionate answer.
- Not touching `Restore one type`, `Reset`, or `Clone`.
- Not changing what any of these operations *do*. This is entirely about consent.

## Also worth fixing while in here

`OnDelete` does not guard `_saveDir` being empty and does not need to — but unlike its siblings it
also **does not refresh the worlds list**. It does not change the save folder, so that is correct;
noted only so the next reader does not add it by pattern-matching.
