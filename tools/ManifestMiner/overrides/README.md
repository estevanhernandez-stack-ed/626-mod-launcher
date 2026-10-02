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
- **`steamAppId`** pins the loader to one game, and must be the numeric Steam id. Leave it out
  entirely for an engine-wide loader (Mod Engine 2 serves every FromSoft game); an empty string is
  rejected, because it would pin the loader to nothing.
- **`launcherExeNames`** are bare `*.exe` filenames found in the game's play folder: no directories,
  no drive, no `..`. The launcher runs whatever file has that name, so the gate is strict.
- **`getUrl`** must be an absolute `https` link. The binary is never bundled; this is where the user
  gets it.
- **`editsSaves`** is not acted on yet: launching a loader does not snapshot saves. Don't rely on it.
- **`banSafe`** is a claim that the loader's modding path avoids the game's anti-cheat. Only set it with
  evidence. `"banSafe": false` withdraws a claim the launcher shipped with.

A duplicate `id` across two files stops the run. A loader the launcher's gate would refuse is named in
the run's output and left out of the draft.

The launcher already ships Mod Engine 2 and Seamless Co-op in its embedded manifest, so they need no
file here unless something about them changes.
