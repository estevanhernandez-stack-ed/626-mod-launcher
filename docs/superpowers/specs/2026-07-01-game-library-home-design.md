# Game Library home — design

**Date:** 2026-07-01
**Status:** Spec (brainstormed in-conversation, grounded against the current game-switcher + a cross-launcher last-played feasibility research pass). Awaiting review → writing-plans. Launcher feature → ships in a release (both flavors).

## The problem

Today the launcher's game switcher is a **ComboBox dropdown** in the title bar (`MainWindow.xaml:41-59`, backed by `ObservableCollection<GameOption>` in `MainViewModel.cs:101`) — cover thumbnail + name, nothing else. It answers "which game am I managing," and nothing more.

The Xbox app's sidebar is the reference the user liked: recency ordering, install-source labels, last-run. But that's a *launcher's* job. 626 is a *mod manager*, so the same surface can answer questions Xbox structurally has no data for: **how modded is each game right now, is it safe to mod, can 626 manage it, is a loader waiting** — surfaced at a glance instead of two clicks deep. That mod-state intelligence is the thing a launcher can't copy back, and it's the point of this feature.

## The shape (approved decisions)

1. **Library home — a landing surface.** A first-class page you land on when you open 626: every registered game as a rich row, sorted most-recent-first. Not a menu — a home. Clicking a game's body switches to its existing mod-management view; a persistent way back to the home.
2. **Hybrid layout** — a "Most Recent" strip of big cover cards across the top (the visual hook) + a dense mod-state **list** below (the full library, scannable).
3. **Recency = real last-played where readable, with 626's own launches as the reliable floor.** A best-available-wins ladder (see *Recency subsystem*).
4. **Launch hub** — each game carries a **Play** control (Play modded / Play vanilla via existing `LaunchTargets` + `VanillaLaunch`) alongside **Manage**. Launching through 626 stamps our own last-played — the universal recency floor.

## Architecture — Core/App split

The mod-state data already exists; this feature is mostly **presentation + a recency subsystem**. New units, respecting the pure-Core / thin-shell law:

| Unit | Layer | Responsibility |
|---|---|---|
| `GameLibraryRow` (record) | Core | Immutable per-game view-data: id, name, source, coverPath, recency (last-played + optional playtime), modCount, enabledCount, activeProfile, engineTier, banRisk, detectedLoaders, nexusDomain. Pure data. |
| `GameLibraryBuilder` | Core | Given the registered games + injected data sources, produces the ordered `IReadOnlyList<GameLibraryRow>` (recency sort, tier resolution, mod-state rollup). Testable — no IO. |
| `ILastPlayedSource` | Core (interface) | `LastPlayed? ForGame(GameRecencyKey key)` — one recency provider. `LastPlayed` = `{ DateTime? LastPlayedUtc; TimeSpan? Playtime; string Source; }`. |
| `RecencyLadder` | Core | Merges N `ILastPlayedSource` results best-available-wins; returns the winning last-played + playtime (independently). Testable with fake sources. |
| `SteamLastPlayedSource` | App (Services) | Reads `appmanifest_<appid>.acf` `LastPlayed` (Unix s). |
| `GogLastPlayedSource` | App (Services) | Reads `galaxy-2.0.db` (`LastPlayedDates.lastPlayedDate` + `GameTimes.minutesInGame`). *Phase 2.* |
| `UserAssistLastPlayedSource` | App (Services) | Reads `HKCU\...\Explorer\UserAssist` — maps a game's known `.exe` / Xbox AUMID → last-run FILETIME. Universal fallback (covers Epic + Xbox). *Phase 2.* |
| `OwnLaunchLastPlayedSource` | App (Services) | Reads 626's own launch log (`LastLaunchedUtc` + session log). Ground truth. |
| `LibraryViewModel` | App | Backs the Library home view; builds rows via `GameLibraryBuilder` with the App-side sources injected; owns sort/filter/search + the discovery lane. |
| `LibraryView` (XAML) | App | The hybrid layout — recent strip + list + discovery lane. |

The IO/Windows readers (ACF file, GOG SQLite, UserAssist registry) live App-side behind `ILastPlayedSource`, exactly like `IStoreLibrary` today. `GameLibraryBuilder` + `RecencyLadder` are pure Core and carry the tests. `CorePurityTests` stays green.

## Recency subsystem — the ladder

Per game, resolve last-played best-available-wins (and playtime independently — some sources give one, not both):

1. **`OwnLaunchLastPlayedSource`** — 626's own launch log. Ground truth for last-played *and* playtime of anything launched through 626.
2. **Source-native** — `SteamLastPlayedSource` (`appmanifest.LastPlayed`, last-played only — Steam playtime is cloud-side); `GogLastPlayedSource` (`galaxy-2.0.db` — last-played **and** playtime in minutes).
3. **`UserAssistLastPlayedSource`** — universal fallback: our stored `GameRoot`/launch `.exe` (or Xbox AUMID) → UserAssist last-run FILETIME (last-run only, no playtime). This is what covers **Epic and Xbox**, whose own launchers do not expose last-played locally.
4. Nothing hits → **`LastPlayed = null`** → the row shows "last played — unknown." **Never a fabricated value.**

Playtime shows only where genuinely sourced (GOG + own-launch); never inferred from a last-run signal.

**Grounding (evidence-backed research):** Steam ACF `LastPlayed` = Unix seconds, present-if-newer (pre-2020 ACFs lack it — degrade). GOG `galaxy-2.0.db` is plain SQLite (`LastPlayedDates.lastPlayedDate`, `GameTimes.minutesInGame`), joined via `releaseKey` through `InstalledProducts`/`InstalledExternalProducts`; **read a copy or open read-only/immutable** to dodge the live-Galaxy lock; **verify the epoch unit (s vs ms) against a real DB before shipping**. UserAssist: HKCU (no elevation), ROT13 value names, run-count @ offset 4, last-run FILETIME @ offset 60, covers `.exe` paths *and* UWP AUMIDs. Epic exposes nothing usable locally (manifests = install dirs only) — UserAssist is its only path. `ActivitiesCache.db` is deprecating out of Windows (24H2+) — **not used.**

## Library row content

Each row: cover · name · **source badge** (steam/gog/epic/xbox/manual) · **recency** ("2h ago", + playtime where real) · **mod-state** (N mods · M on · active profile) · **engine tier** (engine-curated / nexus-only / *engine not detected → Request this game*) · **ban-risk flag** (from `BanRiskCatalog`) · **detected-loader chip** (from `LoaderScan`) · **Play** (modded/vanilla) + **Manage**. The recent strip = big cover cards of the top-N by recency.

Mod-state (mod count / enabled) is already computed per game on load; the tier comes from `EffectiveManifest`/`KnownEngines` (curated) vs nexusDomain-only (nexus-only) vs neither (request); ban-risk from `BanRiskCatalog.ByAppId`; loaders from `LoaderScan.Detect`. No new detection — this is surfacing.

## Launch hub + launch stamping

The Play control reuses `LaunchTargets` (modded options + vanilla + alt launchers, `IsDefault` marked) and the existing launch path (`LauncherService.Launch`). On launch, stamp `GameEntry.LastLaunchedUtc` (atomic registry write) and append a launch-log entry (for playtime of 626-launched sessions). This is the ground-truth recency floor that makes the strip correct regardless of external-launcher readability.

## Discovery lane (the add funnel)

The bottom of the Library home is a collapsed **"Add from your library"** section: installed-but-unadded games from the store scan we already do (`IStoreLibrary` / `SteamService`), one click to add via the existing `AddGameDialog` flow. The library *is* the add funnel — not a separate dialog you hunt for. Ordered by the same recency signal (a game you played yesterday but haven't added floats up).

## Cover art

Steam (`SteamArt.PickCover` from `librarycache`) and GOG (local art) resolve real covers. Epic/Xbox/manual fall back to a **themed placeholder** (game initial on the accent, using the active theme tokens). A later option lets the user drop their own image per game. Cover resolution stays resolved-at-load (as today), extended with the GOG path + the placeholder.

## Data model changes

`GameEntry` (`src/ModManager.Core/GameEntry.cs`) gains:
- `StoreSource` (string?) — explicit `"steam" | "gog" | "epic" | "xbox" | "manual"` (today only `SteamAppId` is inferred). Set at add-time.
- `LastLaunchedUtc` (DateTime?) — stamped on 626 launch.

Launch log — a small append-only per-game or global JSON (`launched` events with game id + start/end) for own-launch playtime. **camelCase JSON on disk** (the rule), atomic writes (`AtomicJson`), round-trip test. Both new `GameEntry` fields serialize camelCase; round-trip test asserts it.

## Data flow

Open 626 → `LibraryViewModel.LoadAsync`: read registered games → for each, `GameLibraryBuilder` rolls up mod-state (existing per-game read) + resolves recency via `RecencyLadder` over the injected sources → order by recency → bind recent strip (top-N) + list. Discovery lane = store scan minus registered. Click game body → existing `SetActiveGame` + navigate to the mod view. Click Play → `LauncherService.Launch` (stamps recency). Click Manage / Add → existing flows.

## Error handling / graceful degradation

- Recency ladder: any source that throws/returns null is skipped; the next tier tries; all-miss → "unknown" (never fake).
- GOG DB locked/absent → that source returns null (copy-and-read or read-only/immutable first). Missing Galaxy → skip.
- UserAssist disabled/cleared → returns null → skip.
- Cover missing → themed placeholder.
- Launch-log write failure → non-fatal (recency degrades to source-native/UserAssist).

## Testing

- **Core (TDD):** `RecencyLadder` merge (best-available-wins, independent last-played vs playtime, all-miss → null) with fake `ILastPlayedSource`; `GameLibraryBuilder` (recency ordering, tier resolution, mod-state rollup, discovery exclusion) with injected fakes. `GameEntry` camelCase round-trip incl. the two new fields.
- **App (build + smoke):** the readers (Steam ACF, GOG SQLite, UserAssist) and the WinUI view are covered by build + a smoke checklist entry (real machine, real launchers). Add `docs/smoke-tests/pending.md` entries per phase.
- `CorePurityTests` stays green (readers are App-side).

## Phasing

- **Phase 1 — the Library home itself.** Hybrid layout, mod-state rows, launch hub, discovery lane, cover art (Steam + placeholder). Recency from **`OwnLaunchLastPlayedSource` + `SteamLastPlayedSource`** (the reliable core). `GameEntry.StoreSource` + `LastLaunchedUtc` + launch stamping. Shippable and correct on day one.
- **Phase 2 — broaden recency.** `GogLastPlayedSource` (+ GOG cover art) and `UserAssistLastPlayedSource` (lights up Epic + Xbox). Pure additive behind `ILastPlayedSource` — no Phase 1 rework. Gated on the GOG epoch-unit verification.

## Non-goals

- **No per-game theme** (themes stay app-wide).
- **No total-playtime we can't source** — playtime shows only for GOG + 626-launched; never inferred.
- **No `ActivitiesCache.db`** (deprecating out of Windows).
- **No cloud/account auth** — all recency is local-only, no elevation.
- **Phase 1 does not block on cross-launcher recency** — Steam + own-launch ship first.

## Success criteria

- Opening 626 lands on the Library home: recent cover strip + a mod-state list, sorted most-recent-first.
- Each row shows source, recency, mod-state (N mods · M on), engine tier, ban-risk, detected loaders; Play (modded/vanilla) + Manage both work.
- Recency is correct for Steam + 626-launched games in Phase 1; GOG + (via UserAssist) Epic/Xbox in Phase 2; unknown shown honestly, never faked.
- The discovery lane surfaces installed-unadded games and adds them in one click.
- Both flavors (no `#if FULL`); `CorePurityTests` green; camelCase JSON round-trips; launch/recency writes are non-destructive.

## Repo-law checklist

- **Core purity** — recency readers are App-side adapters behind `ILastPlayedSource`; ladder + builder are pure Core.
- **camelCase JSON on disk** — new `GameEntry` fields + the launch log; round-trip tests with string-contains assertions.
- **Reversibility** — recency/launch-log writes are additive metadata (atomic), never touch game files; Play uses the existing launch path.
- **Both flavors** — no `#if FULL` anywhere in this feature.
