# Saves are three shapes, and the panel only knows one

**Date:** 2026-08-19 · **Found:** opening Palworld after Pocketpair published its mod guideline
**Status:** design agreed in principle (list worlds), one open question before building

## What happened

Palworld's saves panel said *"No save files of this game's known types here. Check the save folder
above."* The folder was correct. The saves were in it. The app was pointing at the one thing that was
not wrong — fixed separately in `fix/saves-say-which-thing-is-missing`.

The listing gap behind it is not fixed, and this is the design for it.

## The three shapes, measured on the real machine

**1. Typed files in one folder — the only shape the panel knows.**

```
ELDEN RING\<steam-id>\
    ER0000.sl2      Vanilla
    ER0000.co2      Seamless Co-op
    ER0000.err      Reforged
```

`SaveType(extension, label)` fits perfectly. Several formats of *the same* save, which is also what
makes *Clone to…* meaningful.

**2. Per-world folders — Palworld.**

```
Pal\Saved\SaveGames\<steam-id>\
    GlobalPalStorage.sav              ← the only top-level .sav
    905979404BC61E4E…\                ← a world
        Level.sav  LevelMeta.sav  LocalData.sav  WorldOption.sav
        Players\00000…0001.sav
        backup\
    F8238F784BB514DA…\                ← another world
```

74 `.sav` files, 72 of them nested. `ListSaveFiles` calls `Directory.GetFiles(saveDir)` — one level,
no recursion — so it finds `GlobalPalStorage.sav` and nothing else. Declaring `.sav` would list that
single file and imply it is your save, which is worse than listing nothing.

**3. An opaque database — Windrose.**

```
R5\Saved\SaveGames\EnhancedInputUserSettings.sav      ← input settings, not a save
R5\Saved\SaveProfiles\<steam-id>\
    RocksDB\  RocksDB_v2\  RocksDB_v2_Backups\
```

There is nothing to itemise. RocksDB is a key-value store; the game even keeps its own backups beside
it. **Windrose is not world-shaped**, which is worth stating because it was the natural guess: its
subfolders are one account directory, not saves. A blind "list the subfolders as worlds" would show a
single entry named after a Steam ID.

## The design

A game declares its **save layout**, and the panel answers accordingly. The kind is per-game, not
per-engine — Palworld and Windrose are both `ue-pak` and share nothing here.

| Layout | Panel shows | Unit of restore |
|---|---|---|
| `TypedFiles` | files, labelled by type (today) | one file, and *Clone to…* between types |
| `Worlds` | one row per world folder: name, last-played date, size | one world |
| `Opaque` | no list, and says why | the whole folder only |

`Opaque` is not a cop-out, it is the honest answer for shape 3 — and it is the same pattern as wave
8's empty states: name what is true, then name what still protects you. *"Windrose keeps its saves in
a database rather than as files. Snapshots cover it whole; restoring part of it isn't possible."*

## What Pocketpair's guideline adds

Their document tells Palworld players to **back up saves before modding**, and warns that a save once
loaded with mods may keep misbehaving after the mods are gone. That is precisely the moment this panel
appears — so a panel that cannot see the saves is failing at the exact task the publisher is telling
the player to do.

It also raises the value of `Worlds` specifically: the thing a Palworld player wants to restore is
*one world*, not a file.

## The open question

**What names a world?** The folder is a GUID — `905979404BC61E4EF56946B155337D7F` means nothing to a
person. Options, cheapest first:

1. **Folder GUID + last-modified date + size.** No parsing, works immediately, and the date is
   probably enough to tell two worlds apart.
2. **Read the world name out of `LevelMeta.sav`.** Palworld's `.sav` is GVAS wrapped in a custom
   compression; there are community parsers but it is real work and a format that can change under us.
3. **Let the user label a world**, stored beside our own data. No parsing, survives format changes,
   and the label is theirs.

**Recommendation: 1 now, 3 shortly after.** Do not parse the save format to render a list — that is a
large dependency on somebody else's reverse-engineering for a cosmetic gain, and this repo already
carries one of those for FromSoft and knows what it costs.

## Scope note

Restoring a single world is a genuine file operation into a game folder and inherits every rule:
snapshot first, atomic write, nothing deleted. It should ride the existing snapshot machinery rather
than grow a second path.
