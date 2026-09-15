# EA app games, slice one: find them, add them, play them

**Date:** 2026-09-14
**Status:** approved 2026-09-14, with the owner's note that the mod work will bring more changes (see 6a)
**Program:** EA football, sub-project 1 (see `docs/superpowers/plans/2026-09-13-ea-football-grand-plan.md`)
**Depends on:** ban risk for games without a Steam id (merged, #344). A4 decided: the EA store key is the
`installerdata.xml` contentID. A8 decided 2026-09-14: Play opens the EA app's launch link.

## What this slice does, and what it leaves for later

After this slice, College Football 27 and Madden NFL 27 show up in the library home's discovery lane
when the EA app has them installed. One click adds each, with its own row, the BAN RISK chip, and a Play
button that hands the launch to the EA app.

It does **not** turn mods on or off for these games. There is no mod lane for them, and the grand plan
says why: the tools that mod them today replace EA's anti-cheat launcher, which is out of bounds. The mod
list says so in words rather than showing an empty box or a setup warning.

**Later slices, named so nothing here pretends to cover them:** save snapshots (these saves have no file
extension, and the save listing matches extensions only), the build-changed warning keyed on the EA
`gameVersion`, the add-game dialog's quick-add list, and the "is this install vanilla?" check
(sub-project 2).

## What is on disk, verified on this machine

| Fact | College Football 27 | Madden NFL 27 |
|---|---|---|
| Install folder | `C:\Program Files\EA Games\EA SPORTS College Football 27\` | `C:\Program Files\EA Games\Madden NFL 27\` |
| Registry | `HKLM\SOFTWARE\EA Sports\EA SPORTS College Football 27`, value `Install Dir` | `HKLM\SOFTWARE\EA Sports\Madden NFL 27`, value `Install Dir` |
| `__Installer\installerdata.xml` root | `DiPManifest version="4.0"` | same |
| `contentIDs/contentID` | `16425899` | `16425895` |
| `buildMetaData/gameVersion@version` | `1.0.140.17622` | `1.0.139.61898` |
| `gameTitles/gameTitle[@locale=en_US]` | `EA SPORTS College Football 27` | `Madden NFL 27` |
| `runtime/launcher` (non-trial) | `[HKLM\...\Install Dir]EAAntiCheat.GameServiceLauncher.exe` | same shape |
| EA's desktop shortcut target | `CollegeFB27.exe`, no arguments | `Madden27.exe`, no arguments |
| Link handlers | `origin2://` and `origin://` → `EALauncher.exe "%1"`; `link2ea://` → `Link2EA.exe` | same |

The registry parent is the publisher (`EA Sports` here). Other EA titles use `EA Games` or
`Electronic Arts`, and 32-bit installs write under `WOW6432Node`. The detector reads all three parents in
both views.

## The launch link

Play opens `origin2://game/launch/?offerIds=<contentID>`. Windows hands it to `EALauncher.exe`, so the EA
app does the launching and runs its own update check, cloud sync and anti-cheat launcher. The launcher
never starts a game file.

**Evidence, labelled honestly.** The URL shape and the use of the `installerdata.xml` contentID come from
community sources: a Lutris issue from August 2023 and EA forum threads. No EA documentation was found,
and some reports call it patchy. Nothing was launched during research.

**The one live check is yours:** press Play once on each game after this ships. If the EA app does not
start the game, the fallback already named in the plan applies. The row's action becomes **Open the EA
app**, which opens `origin2://` with no game. That means changing one import value, plus a small
repair for the two entries already added, not a redesign.

**Already works in code.** `LauncherService.Launch` hands `GameEntry.LaunchUrl` to `Process.Start` with
`UseShellExecute`, and nothing on the launch path checks the scheme (`SafeUrl.IsHttpUrl` guards browser
links only). `steam://` goes through the same door today.

## What changes

### 1. Reading `installerdata.xml` — Core, pure

```csharp
// src/ModManager.Core/Stores/EaInstallerData.cs
public sealed record EaInstall(
    IReadOnlyList<string> ContentIds, string? Title, string? GameVersion);

public static class EaInstallerData
{
    /// Parse the text of installerdata.xml. Returns null for anything that is not a DiPManifest with at
    /// least one contentID: an unreadable or foreign file is "not an EA install", never an exception.
    public static EaInstall? Parse(string xml);
}
```

- `Title` prefers `gameTitle[@locale='en_US']`, then the first `gameTitle`.
- Tests use small synthetic XML fixtures with the structure above. EA's own files are never committed.
- Hostile input: DTD processing off and no external entity resolution (`XmlReaderSettings`
  `DtdProcessing = Prohibit`, `XmlResolver = null`). It is a file on disk that any installer can write.

### 2. Finding EA installs — App adapter

```csharp
// src/ModManager.App/Services/EaLibrary.cs
public sealed class EaLibrary : IStoreLibrary   // StoreKind => "ea"
```

- Enumerates subkeys of `HKLM\SOFTWARE\{EA Sports, EA Games, Electronic Arts}` in the 64- and 32-bit
  registry views, reads `Install Dir`, and parses `<Install Dir>\__Installer\installerdata.xml`.
- Yields `InstalledGame("ea", AppId: ContentIds[0], Name: Title ?? DisplayName, InstallDir)` with
  `BuildId = GameVersion`.
- **Staged installs are skipped.** An entry counts only when its install folder exists and its
  installerdata parses. What B1 learns about half-finished downloads can tighten this later.
- **Read-only.** Registry reads and one file read per install. It never writes a key, never starts or
  queries the EA app, and returns an empty list on any failure.
- `ResolveCoverArtPath` returns null. No local EA art cache was found.

A composite `IStoreLibrary` wraps Steam and EA and is what DI hands to the discovery lane. Code that is
genuinely about Steam (Steam's running check, the Steam build baseline, the Ludusavi user id) keeps taking
`SteamService` directly and is untouched.

### 3. The manifest learns the EA id — schema, merge, miner, data

- `StoreIds.EaContentId` (camelCase `eaContentId`) in `src/ModManager.Core/Manifest/GameManifest.cs`, and
  `EffectiveManifest.MergeStores` merges it like the other four.
- **Test gap to close.** `Every_optional_field_the_schema_declares_survives_the_merge` skips `Stores`, so
  a new store field is not covered automatically. Add an assertion that walks every `StoreIds` property
  through `MergeStores`, so the next store id cannot be dropped silently either.
- The miner's `OverrideEntry` gains `EaContentId`, and `OverridesMerge` writes it into `Stores`.
- **Data PR in `626-game-manifest`, signed off by the owner 2026-09-14.** Add
  `"eaContentId": "16425899"` to `overrides/ea-sports-college-football-27.json` and
  `"eaContentId": "16425895"` to `overrides/madden-nfl-27.json`.
- **Order matters.** That repo's CI checks out the launcher repo and runs *its* `tools/ManifestMiner`. The
  launcher PR that teaches the miner `eaContentId` merges first. The data PR follows, or the field is
  dropped on regeneration.

### 4. EA games are offered only when the manifest knows them

`EaGameImport.Plan(InstalledGame, EffectiveManifest)` in Core matches the install's contentIDs against
`Stores.EaContentId`.

- **Match:** the game is offered, and its id is the manifest id. That is what makes ban risk resolve
  (#344 resolves by manifest id and holds a compiled floor for these two).
- **No match:** the game is not offered in this slice.

**Timing that follows from this.** A launcher only matches once its manifest carries the ids. On this
machine that means the data PR has merged and the feed has refreshed (about a day, or on the next
definitions check). An offline first run with only the embedded snapshot offers neither game until a
release regenerates that snapshot. That is the safe direction to be wrong in.

**Why curated-only.** An EA install added under a slug of its display name would read no ban risk at
all, and the EA catalogue is full of anti-cheat games. Offering only what the manifest describes keeps
the operating law intact. Growing EA coverage is a data PR, like every other game.

### 5. Adding one

`EaGameImport.Plan` produces a `GameInput`:

| Field | Value |
|---|---|
| `Id` | the manifest id (`ea-sports-college-football-27`, `madden-nfl-27`) |
| `GameName` | the manifest name |
| `Engine` | `"frostbite"`, a new preset that for now declares **no mod locations** (see 6) |
| `GameRoot` | `Install Dir` |
| `EaContentId` | the matched contentID, new on `GameInput` and `GameEntry` |
| `LaunchUrl` | `origin2://game/launch/?offerIds=<contentID>` |
| `SteamAppId` | **never set**, even though the manifest has one; a Steam id would route Play through `steam://` |
| `DataDir` | `%LOCALAPPDATA%\626mods\<id>`, new on `GameInput`, persisted on the entry |

**Why the data folder moves.** `Scanner.DataDirForGame` falls back to `<parent of game root>\_626mods`,
which is `C:\Program Files\EA Games\_626mods`. `icacls` shows Users have read and execute only there,
and the launcher runs unelevated, so every metadata or snapshot write would fail. The existing
`GameEntry.DataDir` override takes precedence and is already honoured everywhere. This slice sets it at
import.

**Discovery stops re-offering an added game.** `LibraryViewModel.RebuildDiscovery` today skips installs
whose Steam id is registered. It will skip on `(StoreKind, AppId)` against each registered game's Steam
id or EA content id.

**The click path.** `MainWindow.AddDiscoveredGameAsync` today always builds a `SteamImportCandidate`. It
branches on `game.StoreKind`: `"ea"` goes to `EaGameImport.Plan`, anything else keeps today's Steam path
unchanged.

### 6. The mod list says what the launcher does for these games

The `"frostbite"` preset declares no mod locations for now. With none declared, setup-drift cannot fire.
The mod list needs words, not a blank:

> **The launcher doesn't turn mods on or off for this game yet.** It tracks the game and warns about its
> anti-cheat. Mod tools for this game replace EA's anti-cheat launcher, which the launcher won't do.

This goes through `ModListEmptyState` as a fourth reason, keyed on **the resolved game having no mod
locations**, and the drop-a-zip hint does not show. A dropped archive on one of these games is refused
with the same sentence, and nothing is extracted.

**Toggles cannot reach a lane.** With no locations and no rows there is nothing to toggle.
`ModToggle.SetEnabledAsync` also refuses a game with no mod locations explicitly, so the agent tool gets
a clear refusal instead of an index error if a row ever appears.

### 6a. Built to change when the mods are looked at — decided 2026-09-14

The owner expects more changes once the mods for these two games are studied. So nothing in this slice
may make that a migration.

- **Store facts, never policy.** The stored engine is `"frostbite"`, what the game *is*. The launcher
  never stores "no mods" on a registered game. Every no-mods behaviour above keys on *the resolved game
  has no mod locations*, a state that goes away the moment a location exists.
- **A mod lane later is a preset or manifest change.** `Scanner.GameContext` already re-applies a
  corrected definition to games the user added, without rewriting the stored entry. A future lane gives
  the `"frostbite"` preset its locations, or gives the manifest entries an `engine`/`modPath`. It reaches
  both registered games with no re-add.
  - **One thing to verify then, not now:** the refresh today corrects an existing primary location. Taking
    a game from zero locations to one may need a small change to `RegistrationRefresh`, and the tests for
    that change belong with the lane.
- **The manifest entries stay open.** They carry `eaContentId` now and are free to gain `engine`,
  `modPath`, `saveDirHint` or new safety fields later, as ordinary data PRs.
- **Curated-only matching stays** whatever the mod work finds. It is what keeps ban risk attached to the
  right id.

### 7. Running check

The existing process-name check already covers these games. `GameProcessProbe` adds the names of exes
found in `GameRoot`, which here are `CollegeFB27.exe` / `Madden27.exe` and
`EAAntiCheat.GameServiceLauncher.exe`. It only reads the process list, and never opens a handle to or
signals any process. No change.

## Testing

**Core, xUnit, failing first:**

1. `EaInstallerData.Parse`:
   - reads contentIDs, the en_US title and the game version from a fixture shaped like the table above;
   - returns null for empty text, a non-DiPManifest root, no contentID, and malformed XML;
   - a fixture with a DOCTYPE and an external entity parses without resolving it.
2. `EaGameImport.Plan`:
   - a matching contentID yields the manifest id, the name and the `origin2` link;
   - `SteamAppId` stays null and `DataDir` sits under the local app data root;
   - an unmatched contentID yields nothing, even when the install's title equals a manifest name (no
     name matching, on purpose).
3. **Ban risk end to end.** The `GameEntry` the plan builds for each title resolves `High` through
   `BanRiskCatalog.Effective`, with and without a remote feed.
4. **Merge.** `StoreIds.EaContentId` survives `EffectiveManifest` both ways, and a reflection walk over
   `StoreIds` fails if any store field is dropped.
5. **Discovery dedupe.** An EA install whose content id is registered is not re-offered, and a Steam
   game with the same numeric string as an EA id is not confused with it (the store kind is part of the
   key).
6. **A game with no mod locations.**
   - `ModListEmptyState` gives the no-mods sentence and no drop hint;
   - intake refuses with nothing extracted;
   - `ModToggle` refuses it.
   - Nothing about this is keyed on the store or on the name `frostbite`: the same game with a location
     added behaves like any other game.
7. **Miner.** An override with `eaContentId` lands in `Stores.EaContentId`.

**Live, run rather than written:**

- The discovery lane shows both games on this machine, and adding each gives a row with BAN RISK and no
  setup warning.
- `list_games` shows each with its manifest id, and `get_game_shape` reports no mod locations.
- Nothing is written under `C:\Program Files\EA Games`: a directory listing before and after an add is
  identical.
- **Owner:** press Play once on each game. The EA app should start it. If it does not, record that, and
  the row action switches to Open the EA app.

## Out of scope

- Save snapshots and name-pattern save types (`ROSTER-*`, `RTG-*`, `PROFILE-*`), plus the `saveDirHint`
  data PR.
- The EA `gameVersion` build-changed warning.
- The add-game dialog's quick-add list, and the curated picker (these entries have no engine or mod path,
  so `PopularGames` excludes them).
- Uncurated EA games.
- Merging a game detected in both stores into one row. No copy on this machine is in both.
- Anything that writes to an EA game, its saves or its folders. The launcher reads here and hands Play
  to the EA app.
