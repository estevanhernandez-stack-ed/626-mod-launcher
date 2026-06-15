# Nexus by-mod-id poll — refresh stats + updates-available

> Design doc. Grounded in `docs/superpowers/research/2026-06-14-nexus-data-research.md` and the 2026-06-14 verification workflow (run `wf_1c8a32eb-09e`). Builds on the Nexus enrichment slice (PR #144).

**Goal:** Surface live Nexus stats on the *existing* library without re-finding archives, and flag when a newer version of a mod exists on Nexus — both driven by polling Nexus by mod id.

## Why this, why now

The enrichment slice (PR #144) only fills mods that are freshly md5-identified (intake / Backfill), because Nexus indexes the *archive* hash. An installed library shows nothing until re-identified, and re-identifying needs the original archives. We already persist `NexusModId` (and can parse the id from the stored `Url`), so we can poll Nexus *by id* — no archive required. The same poll yields the current version, which gives us updates-available for free.

## Verified facts this rests on

- `INexusClient.GetModAsync(domain, modId)` is already wired and returns endorsements / downloads / version / available (`NexusClient.cs:83-87`, `NexusRequests.MapMod`).
- `updated.json` and `files.json` are **not** wired. `updated.json` (`GET /v1/games/{domain}/mods/updated.json?period=1d|1w|1m`) returns `[{ mod_id, latest_file_update, latest_mod_activity }]` (unix timestamps) — one bulk call per game tells us which mods changed in the window.
- **No rate-limit handling exists.** `SendAsync` reads no `x-rl-*` headers and throws on any non-2xx (no 429 path). The Nexus-ToS `Application-Name` / `Application-Version` headers are missing.
- Limits: ~20,000/day + 500/hr once the daily quota is spent (cross-checked; the official Swagger returned 403, so treat the exact number as a ceiling and design conservatively). A personal library (dozens–hundreds of mods) is comfortably inside one full sweep.
- Mod id is recoverable two ways: `ModMeta.NexusModId` (post-Backfill libraries have it) **or** parse `ModMeta.Url` (`https://www.nexusmods.com/{domain}/mods/{id}`) via `ModSiteUrl.Parse` (strips `www.`, returns the numeric id) — `NexusRequests.cs:106`, `ModSiteUrl.cs:22-36`.
- Reuse: `SaveMetadata` / `WriteOneMeta` (atomic, camelCase) for persistence; the chip area `StackPanel Grid.Column="2"` (`MainWindow.xaml:445`) for the UPDATE chip; the "Backfill metadata from Nexus archives…" menu item (`MainWindow.xaml:78`, `OnNexusBackfill`) as the sibling-wiring template for "Refresh Nexus stats".

## Architecture

One per-mod refresh primitive, fed two ways.

**The primitive — `NexusRefresh` (Core service, takes `INexusClient`):**
`RefreshOne(ModMeta existing, domain, client)` → resolve the mod id (`NexusModId ?? ParseFromUrl(Url)`); if none, skip. `GetMod(domain, id)` → produce an updated `ModMeta`:
- refresh `EndorsementCount`, `Downloads`, `Available` (live stats),
- set `NexusLatestVersion` = the fetched current version,
- **preserve** the installed `Version` and `NexusFileId` (do *not* overwrite — they are the "what you have" side of the compare).

Update-available is computed, never trusted from disk blindly: `UpdateAvailable = NexusLatestVersion is not null && NexusLatestVersion != Version`.

**Manual sweep — "Refresh Nexus stats":** run `RefreshOne` over every identified mod in the active game, throttled, 429-aware, then `ReloadModsAsync`. Catches updates of any age and refreshes all stats. Status line reports `Refreshed N mods, M update(s) available` (or `Nexus rate limit reached — try again later`).

**Auto-check (debounced):** an App service modeled on `UpdateChecker`. On game load, when the *auto-check-for-mod-updates* setting is on, Nexus is connected, the game has a domain, and >24h since the last poll for this game:
1. one `GetRecentlyUpdatedAsync(domain, period)` call — period chosen by elapsed time (`<1d → 1d`, `<1w → 1w`, else `1m`; older-than-window updates are caught only by the manual sweep),
2. select candidates: installed mods whose resolved id ∈ the returned set **and** `latest_file_update` > the mod's baseline (`InstalledUtc`, or last-poll time),
3. run `RefreshOne` over only those candidates (cheap — changed mods are few),
4. persist + write the per-game stamp.
Any failure (offline / 429 / bad data) is swallowed silently — the auto-check can never break a working session, exactly like the manifest feed.

**Rate-limit hardening (groundwork, required by both paths):**
- `NexusRequests.Headers` adds `Application-Name: 626-mod-launcher` + `Application-Version` (ToS).
- `SendAsync` reads `x-rl-daily-remaining` / `x-rl-hourly-remaining` and surfaces them; on HTTP 429 it throws a typed `NexusRateLimitException` (not a bare `HttpRequestException`).
- The sweep/auto-check throttle (small inter-call delay + low concurrency, well under the ~30 req/s nginx burst) and stop cleanly when a 429 surfaces or remaining quota hits a floor — reporting partial progress, never thrashing.

## Data shape

New persisted field on `ModMeta`: `NexusLatestVersion` (`string?`) — last-fetched current Nexus version. Additive, nullable, camelCase on disk, round-trip-tested. Three-places rule: `ModMeta` → `MergeMeta` (carry-through) → in-memory `Mod` (`NexusLatestVersion` + computed `UpdateAvailable`) → `MergeMetadata` copy → row VM. No other persisted shape changes. Per-game poll stamp lives at `%LOCALAPPDATA%\ModManagerBuilder\last-nexus-poll-<gameId>.txt` (mirrors the update-check stamp).

## UI

- An **UPDATE** chip in the row chip area (accent color), visible when `UpdateAvailable`, tooltip `Nexus has {latest} — you have {installed}`.
- A **"Refresh Nexus stats"** game-options menu item next to Backfill.
- A **"Check for mod updates automatically"** toggle in Settings (default on), mirroring the auto-update-definitions setting.

## Laws / non-negotiables

- Pure-Core: the primitive, id-resolution, candidate-selection, period-selection, and version-compare all live in Core behind tests; only menu/chip/setting/stamp wiring is in App. `CorePurityTests` stays green.
- Zero new *bundled* anything; no embedded key (personal key only, already on-machine).
- Additive + reversible: only metadata-field writes through the atomic camelCase path; nothing deleted; old `metadata.json` round-trips unchanged.
- A remote/API failure never degrades a working install (silent fallback).

## Out of scope (follow-ups)

- File-id-precise update detection via `files.json` (more exact than version-string compare; add if the string compare reads noisy).
- One-click endorse-from-the-app (separate slice; needs endorse-precondition verification).
- A library-wide "N updates available" summary surface.
