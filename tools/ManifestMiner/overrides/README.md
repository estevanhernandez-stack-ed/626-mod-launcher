# Curated overrides

Hand-curated corrections that win over mined data. One `*.json` file per game (Steam id is the key).
The miner applies these as the final merge step (`--with-overrides`), after the Ludusavi backbone +
MO2 enrichment. Curated data wins over everything the miner produced.

## Format (camelCase, all fields except `steamAppId` optional)

```json
{
  "steamAppId": "72850",
  "id": "skyrim",
  "name": "The Elder Scrolls V: Skyrim",
  "engine": "bethesda",
  "modPath": "Data",
  "nexusDomain": "skyrim",
  "featured": 20,
  "fileExtensions": ["esp", "esl", "esm", "bsa"]
}
```

`engine` must be a real engine key (`bethesda`, `ue-pak`, `bepinex`, `smapi`, `minecraft`, `source`,
`melonloader`, `fromsoft`, `custom`). An override whose `steamAppId` isn't in the backbone ADDS a new
entry; one that matches OVERRIDES the mined fields. Unspecified fields are left as the miner set them.

`extraModTrees` (optional) lists the OTHER folders, relative to the game root, where this game's mods
also put files, beside `modPath`. For example, Cyberpunk 2077:
`"extraModTrees": ["r6/scripts", "r6/tweaks", "r6/input", "red4ext/plugins", "bin/x64/plugins/cyber_engine_tweaks/mods"]`.
The launcher only SHOWS which of these trees hold an entry at their top level named exactly like a
mod. It still turns mods on and off in `modPath` alone (B4,
`docs/superpowers/specs/2026-10-02-one-mod-many-trees-design.md`). Each path must be a folder below the
game root: relative, no drive letter, no `..`, and not `.` itself. A launcher reading the feed drops a
bad tree and keeps the rest of the entry, but this build REFUSES a curated file that has one, so a typo
can't vanish from the signed feed unnoticed.

To add a game: drop a `<game>.json` here, run `dotnet run --project tools/ManifestMiner -- --with-mo2
--with-overrides`, and check the coverage summary + the diff. Verify the Steam id (a wrong id just
won't match — it's reported as not-applied, never corrupts).

## Loaders (`loaders/*.json`)

Mod loaders with a launcher exe of their own go in the `loaders/` subfolder, one loader per file. They
become the published manifest's top-level `loaders` list. That list is what the launcher detects in a
game folder ("Launch via X"), and the ban-risk gate offers the ban-safe ones as the safe way to mod.
The game override loader reads only this folder's top level, so a loader file is never mistaken for a
game.

```json
{
  "id": "seamless-coop",
  "displayName": "Seamless Co-op",
  "engine": "fromsoft",
  "steamAppId": "1245620",
  "launcherExeNames": ["launch_elden_ring_seamlesscoop.exe", "ersc_launcher.exe"],
  "getUrl": "https://www.nexusmods.com/eldenring/mods/510",
  "author": "LukeYui",
  "banSafe": true
}
```

- **`id`** is lowercase kebab-case (`seamless-coop`), and must match the built-in loader's id exactly
  to correct it.
- **Write the whole loader, every time.** A feed loader replaces the launcher's built-in loader with
  the same `id`, so a file that only says `"banSafe": false` is rejected, not merged. To correct one
  field, copy the loader and change that field.
- **Pinning to games.** Leave both pins out for an engine-wide loader (Mod Engine 2 serves every
  FromSoft game). Otherwise use either, or both:
  - **`gameIds`**: manifest ids, such as `["madden-nfl-27"]`. Prefer this. It reaches the game on
    every store, including EA app installs, which have no Steam id. The launcher resolves each
    registration to its manifest id through its Steam id and EA content id too, so a second store copy
    (registered as `madden-nfl-27-2`) or an older install registered under a name slug still matches.
    Each id must be a game in the feed, or the run stops.
  - **`steamAppId`**: one numeric Steam id. It reaches the Steam copy only, never an EA install of the
    same game.

  An empty `steamAppId` or an empty `gameIds` list is rejected, because each would pin the loader to
  nothing.
- **`launcherExeNames`** are bare `*.exe` filenames found in the game's play folder: no directories,
  no drive, no `..`. The launcher runs whatever file has that name, so the gate is strict.
- **`getUrl`** must be an absolute `https` link. The binary is never bundled; this is where the user
  gets it.
- **`editsSaves`** is not acted on yet: launching a loader does not snapshot saves. Don't rely on it.
- **`banSafe`** is a claim that the loader's modding path avoids the game's anti-cheat. Only set it with
  evidence. `"banSafe": false` withdraws a claim the launcher shipped with.

A duplicate `id` across two files stops the run, and so does a field the launcher doesn't know
(usually a typo such as `gameId` for `gameIds`). A loader the launcher's gate would refuse is named in
the run's output and left out of the draft.

The launcher already ships Mod Engine 2 and Seamless Co-op in its embedded manifest, so they need no
file here unless something about them changes.
