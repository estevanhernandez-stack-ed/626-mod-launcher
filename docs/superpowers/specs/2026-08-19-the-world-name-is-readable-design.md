# The world name is readable, and the non-goal was wrong

**Date:** 2026-08-19 · **Amends:** `2026-08-19-world-level-saves-design.md`
**Status:** design, approved. Supersedes that spec's section 3 and one of its non-goals.

## What changed

That spec says, twice and in bold, that nothing here reads or writes the `PlM1` container, and it
drops Duplicate on the grounds that a copy is indistinguishable from its original. Both statements
rested on a scan I ran badly — I looked for a pattern, found none, and reported the files as opaque
without ever looking at the bytes.

They are not opaque. `LevelMeta.sav` is **2,015 bytes** and its tail is a plaintext property list:

```
WorldName        StrProperty   "ItjustEst Islands"
HostPlayerName   StrProperty   "este"
HostPlayerLevel  IntProperty
InGameDay        IntProperty
None
```

Every string is `<length byte><bytes><NUL>`. The world's own name sits at a fixed, findable offset,
in the clear, exactly once in the file.

## What was proven on the real game

Three runs against Este's install, each on a **copy** in a fresh GUID folder, with an independent
hash manifest of the real tree before and after. The real tree verified `IDENTICAL` — 69 files,
0 changed, 0 missing — at the end of every one.

1. **A same-length rename loads.** 17 bytes overwritten at offset 1667, nothing else. Same file
   length, same declared uncompressed (2231) and compressed (2003) sizes, same `PlM1` magic. The world
   appeared in Palworld's list under the new name and entered normally.
2. **The game adopts the name rather than tolerating it.** After a play session Palworld had written
   its own `backup/` history for the copy and re-saved `LevelMeta.sav` — still carrying our name. It
   read what we wrote and wrote it back out.
3. **Space padding is invisible.** A 6-byte name in the 17-byte budget renders as `Padded` in the
   world list, which is left-aligned, so the trailing bytes have nowhere to show.

## What the smoke found the next morning

Two things the three original runs could not see, because both need the game to save on its own
schedule rather than on ours.

### A name that does not fill its budget may stop being readable

The world renamed to `Padded` — six bytes in a seventeen-byte budget — was re-saved by Palworld
overnight and came back with a **shorter payload**: 2003 bytes down to 2000, uncompressed still 2231.
The codec turned the eleven-space run into a back-reference, so the name region stopped being literal
bytes. The markers and both length fields are still there; the name between them is not.

The world still shows `Padded` in-game, correctly, forever. We simply cannot read or rewrite it again.
And the world that fills its budget exactly — `ItjustEst Islands` — has survived months of the same
re-saves and still parses, because an exact fit introduces no run to collapse.

Three consequences, all shipped:

- **A rename always records the launcher's label too**, rather than clearing it. The label is the
  durable display record; the bytes in the save are the part Palworld reads. Losing the second must
  never cost the user the first. Duplicate records one for the same reason.
- **There is a third state, and it gets its own sentence.** "This world never had a name" and "we can
  no longer change the name it has" are different facts, and `Read` returning null for both is not a
  reason to say the same thing. `PalworldWorldName.HasOwnSave` tells them apart.
- **The rename copy says which names last.** *"A name that fills the space exactly is the one that
  stays changeable."*

### Steam Cloud puts deleted worlds back

A test copy deleted with the game closed, verified gone, was **back the next morning** — the folder
carries `steam_autocloud.vdf`, and the cloud re-synced it on the next launch. This is a second,
independent reason a Delete cannot simply remove a folder, on top of the game's own flush-on-exit.
Nothing here ships a delete; when one is designed, it starts from this.

## The two laws this creates

### The name has a byte budget, and it is the current name's length

A longer name changes the decompressed size, which makes the header's 2231 a lie, which means owning
the compressor. `PlM1` is not zlib — deflate was ruled out at every plausible offset — and the visible
raw literals point at Oodle. **We will not take that dependency.** The failure mode is a corrupt save
on a game that patches monthly, and the payoff is longer strings.

So a rename fits into the bytes already there, padded with spaces. Two things follow:

- **The budget is bytes, not characters.** UTF-8: an accented letter costs two, an emoji four. A
  counter that says "characters" would be lying to anyone who does not type ASCII.
- **The UI states the budget before the user types**, per the app's own rule about saying what it can
  do. Discovering a limit by being refused is the failure this repo keeps designing away from.

### Nothing may touch a save while Palworld is running

Found by accident and worth more than the test it interrupted. A copy deleted while the game was open
**came back on exit** — Palworld held that world in memory and flushed it. Nothing of Este's was
damaged, but a Duplicate or Rename that silently undoes itself is a bug users would report as "it
didn't work" with no way for us to see why.

Every operation that writes to a world refuses while the game is running, and says which game.

## What gets built

**Read the real name.** The panel shows `ItjustEst Islands`, not `World 1`, with nothing typed. This
is the change that makes the whole surface legible for free.

**Rename writes it.** The button now changes the name Palworld itself shows — which the game's own
settings screen will not do, the field there is read-only. Snapshot `LevelMeta.sav` first (2 KB, and
`ListSnapshots` globs `*.zip` so a `.sav` sibling stays invisible), then atomic temp-and-rename.

**Duplicate a world**, un-dropped. Copy the folder to a fresh GUID, skip the game's own `backup/`
history, and patch the copy's name so the two are distinguishable — the exact objection that killed it
last time, now answerable. Nothing is destroyed, so it needs no confirm; it needs the game closed.

## What happens to `WorldLabels`

It stays, demoted, and the demotion is the point. The round table's vocabulary rule says two names for
one object means one is wrong — so there is **one** Rename, and it prefers the real thing.

Labels remain the answer for the two cases the save cannot serve:

- **A joined world has no `LevelMeta.sav` at all.** Este's second world is `LocalData.sav` and nothing
  else; the world lives on the host's machine. There is no name in it to read or write.
- **A name that overruns the budget**, where the user still deserves to call it what they want in our
  panel even though Palworld will not hear it.

Display order is **the label, then the game's own name, then the ordinal** — and the label winning is
the point, not an oversight. A label only gets set now when a rename could not be written: the name
overran its budget, or the world has no name to write. So a label sitting beside a game name is
always the user's newer choice losing to the older one they already tried to replace.

A rename that DOES reach the save clears any label rather than racing it, so the two can never
disagree about a world the user has successfully renamed.

*(Corrected during implementation — this section originally had the precedence the other way up.)*

## Tests

Core, pure, against fixture bytes rather than a real save:

- the name is located in a synthetic `LevelMeta.sav` and read back
- a same-length write changes only the name bytes; every header field is byte-identical
- a shorter name is space-padded to the exact budget
- an over-budget name is refused before anything is opened
- a multi-byte character is measured in bytes, so a 9-character name can overrun a 10-byte budget
- a file with no `WorldName` marker reads as null and never throws
- a joined world (no `LevelMeta.sav`) reads as null and falls back to its label
- duplicate produces a new GUID folder, omits `backup/`, and leaves the source byte-identical

**The end-to-end stays on a copy**, per the method that has now caught three wrong conclusions. Run
2026-08-20 against a copy of the real tree with `games.json` repointed: all three name states rendered
correctly, the budget counter refused a 40-byte name into 17 and enabled an exact fit, a duplicate left
its source byte-identical at 65 files and dropped 29 MB of the game's backup history, a rename wrote
its label and its `before-rename` snapshot where `ListSnapshots` cannot see them, and with Palworld
running both operations refused and wrote nothing at all. The real tree verified IDENTICAL, 89 files.

## Non-goals, restated

- **Growing the name past its budget.** Needs the compressor. No.
- **Reading or writing anything else in the container** — inventories, levels, characters. The name is
  a special case: one string, one occurrence, fixed shape, cosmetic failure. `Level.sav` is none of
  those things.
- **Merging worlds.**
