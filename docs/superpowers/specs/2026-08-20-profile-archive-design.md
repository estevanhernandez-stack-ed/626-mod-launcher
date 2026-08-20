# The profile archive — making a PC refresh cheap again

**Date:** 2026-08-20 · **Follows:** `2026-08-20-save-transport-and-the-data-it-needs.md`
**Status:** design, for review.

## Why

People stopped reinstalling Windows. Not because machines stopped needing it — because the cost of
putting everything back got quietly enormous, so a degrading install is cheaper than a clean one, and
the problems just live there. Games are a big part of that cost, and a modded install is the worst
part of the games.

The launcher already knows more about a modded setup than anything else on the machine: which games,
which mods, which versions, which are enabled, which loadout, and where every save is. That knowledge
is the whole feature. **The archive is just how you carry it.**

## What it is

One file. Take it to a fresh install of Windows, point the launcher at it, and it puts the setup back
— *without* needing the new machine to look anything like the old one.

## Measured on a real install, 12 games

```
mod files        3.49 GB     349 files, 194 of them Cyberpunk's
saves            1.10 GB     elden-ring 497 MB · cyberpunk 406 MB
launcher data      482 MB    of which ~446 MB is snapshot history
registry + settings  ~120 KB
                 ────────
                 ~4.6 GB     ~4.2 GB without snapshot history
```

Small enough for a USB stick, which is what makes this worth building at all.

**The snapshot history is most of the launcher data and should default to OFF.** Windrose's 328 MB and
Elden Ring's 118 MB are backups of backups. An archive that silently hauls every restore point you
ever made is carrying a spare tyre for a spare tyre. Offer it: *"bring your snapshot history too
(+446 MB)"*.

## The three things that make this hard

### 1. Paths, not bytes

A fresh install has a different Steam library, possibly a different drive, certainly different
folders. So the archive cannot be *"restore these bytes to these paths"* — it has to be:

> This profile had Palworld (Steam `1623730`) with these mods, this loadout and this save. Find where
> Palworld lives **now** and put it back.

Path-independent by construction. The same shape as the save bundle's game-id check, one level up.
Every recorded path is stored **relative to a named anchor** — game root, save folder, launcher data
dir — never absolute, and resolved fresh on restore.

**Games that are not installed yet are listed, not failed.** Restoring onto a machine where Cyberpunk
has not been downloaded should say *"9 of your 12 games are here; the other 3 are waiting for you to
install them"* and hold their mods until they are. A restore that refuses because the games are not
there yet is useless on exactly the machine it exists for.

### 2. Mods live intermixed with game content

This one bit during measurement and is worth writing down. `modLocations` points at folders that hold
**both** mods and the game's own files. Measuring those folders gave 159 GB — Palworld's base-game
paks and Death Stranding's entire data directory. The real answer is 3.49 GB.

**So the archive copies the files the scanner identified as mods, never a folder.** A folder-level copy
would haul the base game, and worse, a folder-level *restore* would overwrite it.

### 3. The credential problem is already solved, by accident of good design

`nexus.json` holds the user's Nexus key **DPAPI-encrypted to `CurrentUser`**. It physically cannot be
decrypted on another machine or by another Windows account. So it is excluded deliberately, and the
restore says one honest sentence: *"you'll sign in to Nexus again."*

`nexus-oauth-cache.json` is client configuration — endpoints, scopes, client id — with no token, so it
travels harmlessly.

Everything else goes through the same `CredentialScan` the save bundles use, because a game can drop a
token beside its saves and one of them provably does.

## What it contains

```jsonc
{
  "archiveVersion": 1,
  "createdUtc": "…",
  "launcherVersion": "0.19.0",
  "games": [
    {
      "id": "palworld", "name": "Palworld", "steamAppId": "1623730",
      "engine": "ue-pak",
      "settings": { "autoBackupOnLaunch": false, "saveAutoKeep": 25, … },
      "mods":  [ { "name": "…", "version": "…", "nexusModId": 1, "enabled": true,
                   "location": "mods", "files": ["…"] } ],
      "loadout": "…", "profiles": [ … ],
      "saveIncluded": true
    }
  ],
  "excluded": [ { "path": "nexus.json", "reason": "machine-bound" } ],
  "app": { "themeId": "…", "backdrop": "…", "themes": [ … ] }
}
```

Payload beside it: `mods/<gameId>/…`, `saves/<gameId>/…`, `data/<gameId>/…`.

**The mod list is recorded even for mods whose files are archived.** If a file is corrupt on arrival —
or the user chose the list-only option — the missing-mods report from the save bundle work already
knows how to say what is absent and link where to get it.

## Restore is a report before it is an action

Same discipline as the save bundle import, and more so, because this touches every game at once.

1. **Read the archive and say what is in it** — games found, games not installed yet, mods, saves,
   what was excluded and why. Nothing has been touched.
2. **The user chooses** what to restore. Per game, and per part.
3. **Snapshot anything about to be replaced**, per the file-op laws.
4. **Refuse while any of the affected games are running.** Learned the hard way on Palworld: a folder
   changed under a running game is silently undone on exit.
5. **Report what actually happened**, including what could not be done and why.

## Files, not just a list

**Decision, and Este's to overrule.** The archive carries mod files by default.

- It works offline, which is the whole point of a machine that has just been wiped.
- It keeps exact versions, and a save built on mod v1.2 does not necessarily work on v1.9.
- **A mod pulled from Nexus is gone.** It happens, and when it does the user's own copy is the only one
  left in the world.

3.49 GB is not the reason to say no.

**The law it must not break.** `NOTICE` says we never bundle third-party binaries, and that stands —
this is *the user's own local archive of their own downloads*, on their own disk, for their own
restore. It is never shared, never uploaded, and never offered as a shareable artifact. That is the
same portable-versus-shareable line the save bundles already draw, and it is the line that keeps this
clear of redistribution. A "list only" option exists for anyone who wants it.

## Non-goals

- **Sharing a profile.** This is one person's setup moving to one person's new machine. A shareable
  profile is a different feature with a different licence question, and it is not this one.
- **Installing games.** The archive restores mods, saves and settings. Steam installs games.
- **Cloud storage.** One file, the user's disk, the user's USB stick. Where it goes after that is
  their business.
- **Backing up the games themselves.** 4.6 GB is a USB stick. Cyberpunk is 100 GB and Steam already
  has it.

## Order

1. **Write the archive.** Reads only, nothing destructive — shippable and useful alone, as a backup.
2. **Read and report.** The whole inspection UI with no restore button. Also useful alone: *"what is
   in this thing?"*
3. **Restore, per game and per part**, with the snapshot and running-game guards.
4. **The not-installed-yet path** — hold mods for a game until Steam brings it back.

Step 1 and 2 together are already a genuine feature: a complete, inspectable backup of a modded
setup. Step 3 is what makes it a refresh tool.

## Open questions

- **Does a profile archive supersede per-game save bundles, or sit above them?** They share the
  credential scan, the game-id check and the missing-mods report. The archive is plausibly *a bundle
  per game plus the registry*, which would be less code and one format to test. Worth settling before
  building rather than discovering later.
- **Loadouts and profiles reference mods by name.** If a mod is missing on restore, does its loadout
  entry survive as a ghost, or get dropped? Ghost entries are recoverable; dropped ones are not.
- **What happens to a game the user no longer has registered?** Restoring a profile onto a machine
  that already has games set up is a merge, not an overwrite, and merges need rules.
