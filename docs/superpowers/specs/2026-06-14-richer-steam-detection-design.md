# Richer Steam detection — design

**Date:** 2026-06-14
**Status:** Approved design, pending implementation plan
**Research grounding:** [docs/superpowers/research/2026-06-14-steam-detection-research.md](../research/2026-06-14-steam-detection-research.md)

## Context & goal

The launcher already reads the user's local Steam library to power Add Game, but it uses only a sliver of what's on disk. `SteamService.InstalledGames()` walks every library (`steamapps/libraryfolders.vdf`), reads each `appmanifest_*.acf`, and returns `SteamGame(AppId, Name, InstallDir)` — three fields out of ~eight. `SteamParse.ParseAppManifest` ([src/ModManager.Core/SteamParse.cs:45](../../../src/ModManager.Core/SteamParse.cs)) pulls only `appid`/`name`/`installdir`.

Two concrete gaps the user hit:

1. **The popular-game picker makes the user Browse to a folder we already parsed.** `OnPopularSelected` ([src/ModManager.App/AddGameDialog.xaml.cs:239](../../../src/ModManager.App/AddGameDialog.xaml.cs)) fills name + engine + modPath + SteamAppId but leaves `FolderBox` blank — even though `InstalledGames()` already has that game's `InstallDir`, keyed by the exact `SteamAppId` the pick just set.
2. **No cover art.** Steam caches each game's artwork locally under `appcache/librarycache/<appid>/` (header, portrait, hero, logo) — no login, no network. The Add Game list and game tiles are text-only where every real launcher shows art. This is the "Xbox app shows my Steam games richly" effect the user noticed.

The goal: read and use more of the Steam data already on disk, behind a clean seam that future stores can plug into — without breaking pure-Core, reversibility, camelCase-on-disk, or the local-first / no-API-key posture.

## Decisions (locked with the user)

1. **First slice (v0.6.2) bundles cover art** — folder auto-fill + parser widening + local cover art ship together, for the full "real launcher" feel in one drop.
2. **Define the store seam now** — `IStoreLibrary`, Steam-complete; GOG/Epic/Xbox become on-demand adapters with no schema migration.
3. **Build-id awareness is committed** (Phase 2) — "Steam updated this game under your mods" warning. We land the `buildId` parse now so the field is ready.
4. **Behavioral data is live-only, never persisted** — `lastPlayed` may be read at the moment a picker is built (for sort), never written to disk. No playtime, no achievements.

## Non-goals (cut, with rationale)

- **Steam Web API / owned-but-not-installed library** — needs the user's API key + network. Off the local-first lane, and a mod manager growing a full library view is scope creep against the launcher the user already runs.
- **`localconfig.vdf` playtime + per-app launch options** — high privacy, rewritten live by Steam mid-session, deeply nested, serves a launcher not a mod manager.
- **Per-user achievement JSON** — high privacy (unlock timestamps), undocumented schema, zero modding value.
- **`appinfo.vdf` display-name enrichment** — harder binary-VDF parse for a marginal name win; the `.acf` `name` already suffices.
- **Continuous filesystem watcher on the Steam cache** — Steam churns the artwork cache; a watcher thrashes disk. Read on demand at game-add / refresh instead.
- **`SteamUserData` cloud-save resolver fill** — the `ExpandSaveRoot` `SteamUserData→null` branch is a fallback-of-a-fallback (only after Ludusavi misses, and the comment "Ludusavi covers these games" holds). Also a footgun: `CurrentUserId64()` returns the +offset SteamID64, but the `userdata/<id>` folder uses the bare 32-bit account id. Cut unless a concrete game proves Ludusavi misses it.

## Architecture — the store seam

A single seam so "richer Steam detection" generalizes to other stores later without a rewrite.

- **`IStoreLibrary` (App-side, `Services/`)** — the IO-bearing contract: *enumerate installed games* and *resolve a game's metadata + art paths*. Lives in the App layer because every implementation does filesystem/registry IO, which Core forbids. `SteamLibrary : IStoreLibrary` is built from today's `SteamService`. Future `GogLibrary` / `EpicLibrary` / `XboxLibrary` satisfy the same contract on demand.
- **Core stays pure** — `SteamParse` (extended), the `InstalledGame` record, and the art-path-candidate logic (a pure function over an injected `exists` predicate) all live in Core with no IO. `CorePurityTests` stays green; `ImageSource`/`BitmapImage` never enter Core.
- **`SteamGame`/`SteamService` refactor** to feed the seam rather than being a one-off. The existing `steamapps/common` + `installdir` join ([SteamService.cs:91](../../../src/ModManager.App/Services/SteamService.cs)) stays the single source of truth for the resolved path — the folder auto-fill reuses it, it does not re-derive the path.

This mirrors the established `SteamService` (App IO) / `SteamParse` (Core pure) split — the seam just names it.

## Data model

Widen the Core `AppManifest` record and the `InstalledGame` it feeds with the fields already present in every `.acf`, all optional (a sparse manifest still parses):

| field | source | used by | persisted? |
|---|---|---|---|
| `buildId` | `appmanifest.buildid` | Phase 2 build-id warning | yes (Phase 2, one field) |
| `stateFlags` | `appmanifest.StateFlags` | "fully-installed" gate (verify-or-drop) | no |
| `sizeOnDisk` | `appmanifest.SizeOnDisk` | future size display | no |
| `lastUpdated` | `appmanifest.LastUpdated` (epoch s) | future | no |
| `lastPlayed` | `appmanifest.LastPlayed` (epoch s) | future picker sort (live-only) | **never** |
| art paths | `librarycache/<appid>/` | Phase 1 cover art | no (resolved on read) |

Parsing a field in Core is not the same as persisting it — only `buildId` is ever written to disk (Phase 2).

## Phase 1 — v0.6.2 (the shippable slice)

**Folder auto-fill on popular pick.** A pure Core helper matches the picked game's `SteamAppId` against the installed-games list and returns the match (not a re-derived path); `OnPopularSelected` sets `FolderBox.Text` from the match's `InstallDir`. `steamGames` currently reaches the dialog ctor but is consumed locally and not stashed — stash it as a field first. The textbox stays editable; when the game is installed this makes the popular pick a near one-click add. Read-only, zero file ops.

**Cover art.** Core resolves candidate art paths under `librarycache/<appid>/` via an injected `exists` predicate — handling **both** the newer hashed-subfolder layout (confirmed on the dev machine) and the legacy flat layout (still in the wild); do not hardcode one filename scheme. The App turns the resolved path into an `ImageSource` (App-side, per purity), reusing the proven `ModRowViewModel.Thumbnail` pattern ([ModRowViewModel.cs:285](../../../src/ModManager.App/ViewModels/ModRowViewModel.cs) + [MainWindow.xaml:350](../../../src/ModManager.App/MainWindow.xaml)). Surfaces in the Add Game picker (today text-only `DisplayMemberPath` at [AddGameDialog.xaml:66](../../../src/ModManager.App/AddGameDialog.xaml) + :73) and the game tiles. Missing art degrades gracefully to the current text.

**Parser widening** (the Core enabler under both) — extend `ParseAppManifest`/`AppManifest` to the field set above. Pure regex extension of the existing best-effort parser.

## Phase 2 — build-id awareness

Persist one new field, `lastKnownSteamBuildId`, on `GameEntry` ([GameEntry.cs:42](../../../src/ModManager.Core/GameEntry.cs) already holds `SteamAppId`). camelCase JSON + a round-trip test (per the on-disk-JSON rule). On launch/refresh, read the live `buildId`, compare via a pure Core comparator, and if it moved show a "Steam updated this game under your installed mods" banner — reusing the Vortex-banner pattern ([MainWindow.xaml:284](../../../src/ModManager.App/MainWindow.xaml)). Fully additive + reversible: an old registry reads the field as null = no baseline = no false warning. `buildId` is a public version stamp, not behavioral data. This beats Vortex's *manual* version wizard by reading the truth automatically — the differentiated mod-manager angle.

## Phase 3 / later

Enriched installed-games picker: cover art + **sort-by-last-played read live at picker-build time, never written to disk** + show-all (routing engine-undetected games to a pre-filled "Set up" row instead of today's comma-joined `SteamManualNote`). Deferred because it's the largest surface and makes behavioral data the most prominent thing in the picker — worth a conscious call once Phase 1/2 land.

## Future store adapters (feasibility, for the seam)

- **Epic** — HIGH feasibility: plain JSON manifests under `ProgramData/Epic/...`, no new dependency. (Corroborated, not observed on the dev machine.)
- **GOG Galaxy** — MEDIUM: a local SQLite DB (new dependency). (Corroborated.)
- **Xbox / MS Store** — LOW, detect-and-route only: `WindowsApps` is ACL-sandboxed, which defeats reversible file-ops, so Xbox stays read-only by nature. (High confidence it's hard, per MS GDK docs.)

The seam is built now; these implementations land on demand. One complete Steam adapter beats four half-built stubs.

## Privacy posture

`lastPlayed` is read only when a feature needs it (Phase 3 sort) and **never persisted**. No playtime, no achievements, no account enumeration, no Web API, no network. Everything read is the user's own local disk, and the only thing written is `lastKnownSteamBuildId` (a public version stamp).

## Reversibility / pure-Core / camelCase compliance

- **Reversibility:** Phase 1 is entirely read-only (no file ops). Phase 2 writes only one additive registry field; old registries stay valid.
- **Pure-Core:** parsers, records, comparator, and art-candidate logic in Core; all IO and `ImageSource` in App `Services`/view-models. `CorePurityTests` covers it.
- **camelCase on disk:** the one persisted field (`lastKnownSteamBuildId`) ships with the string-contains round-trip test the rule requires.

## Testing strategy

- **Core — parser:** optional fields (sparse manifest still parses); `lastUpdated`/`lastPlayed` unit pinned as epoch **seconds**; the existing fixture already carries an unparsed `StateFlags` "4" ([SteamParseTests.cs:41](../../../tests/ModManager.Tests/SteamParseTests.cs)).
- **Core — art-path resolver:** injected `exists` predicate, asserting both the hashed-subfolder and legacy-flat layouts resolve, and missing art returns none.
- **Core — folder match:** appid → installed-game match returns the right entry / null; the folder comes from the match's `InstallDir`, not a re-derived path.
- **Core — build-id comparator (Phase 2):** moved / unchanged / null-baseline verdicts; camelCase round-trip for `lastKnownSteamBuildId`.
- **StateFlags caveat (below):** a test that reads a real not-fully-installed `.acf` and pins the bitmask, or the gate is dropped.

## Open caveat & risks

- **`StateFlags` bitmask is community-documented, not decoded here.** The catalog observed the field present with value `4` but did not verify "`& 4` = fully installed" against an actually-mid-update install. The "hide update-pending games" gate is therefore **verify-with-a-test-or-drop**, not assumed. It does **not** block the Phase 1 anchor (folder-fill + art + parse stand without it). This deliberately overrides the research doc's recommendation to ship the install-state gate alongside the parser widening — the gate waits on verification; the parser widening does not.
- **Artwork filename/dimension roles** (e.g. "600x900 portrait") are widely-documented interpretation, not measured; a wrong tile choice degrades to "pick a different file," so low stakes — but the resolver should prefer-by-candidate-list, not assert a single canonical name.
- **Multi-library / staging:** `InstalledGames` already de-dups the same appid across libraries (the `seen` HashSet). When the parse starts reading `StateFlags`, the move/staging-in-progress state is exactly what the install-state gate should catch — cover it in the gate's test.

## References

- Research + competitive scan: [docs/superpowers/research/2026-06-14-steam-detection-research.md](../research/2026-06-14-steam-detection-research.md)
- Current Steam read path: `src/ModManager.App/Services/SteamService.cs`, `src/ModManager.Core/SteamParse.cs`
- Add Game flow: `src/ModManager.App/AddGameDialog.xaml(.cs)`
- Tile/banner patterns to reuse: `ModRowViewModel.Thumbnail`, the Vortex banner in `MainWindow.xaml`
