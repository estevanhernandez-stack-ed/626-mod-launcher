# Nexus data — what more we can use, and what's worth building

> **Date:** 2026-06-14
> **Status:** Research / grounding doc. No code changes. The analog to the Steam-detection question, asked of Nexus: *what does the Nexus API expose that we parse partially or ignore, and what's worth building?*
> **Confidence tags:** `verified-in-repo` (I read the file), `verified-from-docs` (Nexus help article / official node-nexus-api / FluentNexus), `likely` (widely reported, not in primary docs), `uncertain` (claimed but unconfirmed). Anything not tagged is my synthesis, not a fact.

## The one-line read

The headline feature people *want* — "your installed mods have updates" — is the **most rate-limit-sensitive** thing on the board, because the naive version is one API call per installed mod. The headline feature we should **ship first** is the opposite: surfacing data already sitting in responses we fetch and throw away. Endorsement count, version, adult flag — all ride in the exact `mods/{id}.json` and `md5_search` bodies we already parse. Reading them is zero net-new network cost and the cleanest honor-the-builders win we have. Update awareness is real and worth doing — but it goes *second*, on the bulk `updated.json` primitive, not a per-mod sweep.

The unglamorous prerequisite under almost everything: **we don't persist the Nexus mod id today.** `NexusMd5Match` carries it; the Scanner drops it. Fix that first or every id-based feature has to re-hash files to find the mod again.

## Ground truth — what the repo actually does today

All `verified-in-repo` (2026-06-14, on `docs/update-for-feed-golive`):

- **`MapMod`** (`src/ModManager.Core/NexusRequests.cs:82`) maps exactly: `name`, `summary`, `author`/`uploaded_by`, `uploaded_users_profile_url`, `picture_url`, `mod_id` (→ constructed Url), `category_id` (→ name). It **hardcodes** `Source = null`, `Donate = null`, `Downloads = null`. The same response body carries `endorsement_count`, `mod_downloads`, `mod_unique_downloads`, `version`, `created_timestamp`, `updated_timestamp`, `status`, `available`, `contains_adult_content`, and a `user` object — every one of them dropped on the floor.
- **`MapMd5Response`** (`NexusRequests.cs:143`) reads only the `mod` sub-object and `mod_id`. It **never touches `file_details`** — the installed file's `file_id`, `version`, `category_name`, `size`, `uploaded_timestamp` are all discarded. This is the single cheapest miss: the data is already in the response we make at intake.
- **`ModMeta`** (`src/ModManager.Core/Mod.cs:54`) is the ceiling. It has `CurseforgeId` but **no Nexus id field**, and no version / endorsement / status / file-id fields. Unmapped data dies at `MapMod` regardless of intent.
- **The mod id is lost on persistence.** At `Scanner.cs:1264-1267` the md5 hit merges `match.Meta` into `metadata.json` but discards `match.ModId`. The *only* code path that holds a Nexus mod id is the Vortex route (`Scanner.cs:1349-1351`), and it gets the id by re-parsing Vortex's manifest, not from our stored metadata. So today: hash a file → learn its mod id → throw the id away → can't look it up again without re-hashing.
- **`MergeMeta`** (`Scanner.cs:1141`) is per-field curated-wins (`curated.X ?? cf.X`), with `IsManual` short-circuiting the whole merge. **Every new `ModMeta` field must be added here too** or it silently vanishes on the next rescan. This is a real gotcha, not a nicety.
- **`SendAsync`** (`src/ModManager.Core/NexusClient.cs:42`) reads **zero** `x-rl-*` headers and throws `HttpRequestException` on any non-2xx. `GetByMd5Async` special-cases 404, `ValidateAsync` special-cases 401 — **nothing handles 429.** A rate-limit breach is an unhandled exception today.
- **`INexusClient`** has exactly three methods, all GET: `GetModAsync`, `GetByMd5Async`, `ValidateAsync`. There is **no write path** — endorse would be the first POST the client has ever made.
- **`ValidateAsync`** maps only `name` + `is_premium`. The body also carries `user_id`, `profile_url`, `email`, `is_supporter`, `key` — correctly *not* persisted (privacy), but `profile_url` and `is_supporter` are available if ever wanted.
- **Categories** are fetched once per session per domain and cached in memory only (`NexusClient.cs:63`). No on-disk cache like `RemoteManifestCache` has. Fine at today's volume; worth revisiting if call volume climbs.

## The Nexus API surface — the catalog (verified-from-docs)

Sources: Nexus official `node-nexus-api` types, FluentNexus client, Nexus help article 105 (rate limits). Cross-checked, not pulled from memory.

| Endpoint | We use it? | What's in it we're missing |
|---|---|---|
| `GET /v1/games/{domain}/mods/{id}.json` | Yes (`GetModAsync`) | `endorsement_count`, `mod_downloads`, `version`, `updated_timestamp`, `status`, `available`, `contains_adult_content` — **all on the wire already** |
| `GET .../md5_search/{md5}.json` | Yes (`GetByMd5Async`) | `file_details`: `file_id`, `version`, `category_name`, `size`, `uploaded_timestamp` — **discarded today** |
| `GET .../mods/updated.json?period=1d\|1w\|1m` | No | The bulk update primitive: one call/game → every `mod_id` touched in the window. This is how you check 200 mods in 1 call. |
| `GET .../mods/{id}/files.json` | No | Full file list + versions + `category_name` (MAIN/OLD/OPTIONAL) + `file_updates[]` rename chain. The precise half of update detection. |
| `GET .../mods/{id}/files/{file_id}.json` | No | Single-file refresh: changelog, virus-scan link, exact size for the file we matched. |
| `GET .../mods/{id}/changelogs.json` | No | `{version: [lines]}` — "what changed" before pulling an update. |
| `POST .../mods/{id}/endorse.json` / `/abstain.json` | No | One-click endorse. First write op. Requires installed version in body. |
| `GET /v1/user/endorsements.json` | No | What the user already endorsed (toggle state). |
| `GET/POST/DELETE /v1/user/tracked_mods.json` | No | The user's tracking-centre list. One cheap GET regardless of library size. |
| `GET .../files/{file_id}/download_link.json` | No | Download. Premium gets direct links; free **only** via `nxm://` handoff. Scoped out. |
| `GET /v1/games.json`, `latest_*`, `trending` | No | Discovery / game-list. Mostly redundant with our manifest feed or off-thesis. |

### Auth + rate limits (load-bearing)

- **Auth:** personal API key in the `apikey:` header on every call (`verified-in-repo` — matches `NexusRequests.Headers`). DPAPI-encrypted on-machine, never embedded. Operating law #2 holds for every new feature: no shared key, ever.
- **Compliance gap** (`verified-from-docs`): Nexus acceptable-use says a public-facing app must send `Application-Name` + `Application-Version` headers. We send `apikey` + `Accept` only. **Close this before public release** regardless of which features ship — it's a citizenship fix, not a feature.
- **Rate limits** (`verified-from-docs`, help article 105): **20,000 requests / rolling 24h**, dropping to **500/hour** once the daily quota is spent. Daily resets 00:00 GMT. Burst limit (`likely`, reported not in the help article): ~30 req/sec via nginx 429. Limits returned on **every** response via `x-rl-daily-{limit,remaining,reset}` + `x-rl-hourly-*` (`verified-from-docs`, names cross-checked against FluentNexus). Some routes reportedly don't count toward the quota (`uncertain` — not enumerated publicly).
- **Design implication:** a 200-mod library checked per-mod is 200 calls — fine against 20k/day in isolation, but burns the 500/hour budget fast if a few games are checked back-to-back, and it's a bad-citizen pattern. The bulk `updated.json` call (1 per game) is the lever that keeps us polite. **`SendAsync` must learn to read `x-rl-*` and treat 429 as clean back-off before any volume feature ships.**

## Competitive lens — what Vortex/MO2 do, and our angle

Both Vortex and MO2 (`verified`) use `updated.json` (`fetchRecentUpdates` / update-available indicator) as the rate-sane backbone: intersect returned `mod_id`s with installed mods, deep-fetch only the handful that changed. Both have endorse-from-app and per-row endorsement state — MO2 users built whole batch-endorse plugins because the demand is real. Both register `nxm://` and download via `download_link`; both treat the free-user nxm-only constraint as the central download gate. Vortex treats Collections (mostly GraphQL) as first-class.

**Our differentiated angle:** *we tell you what changed; we never touch your install without you.* The whole project ethos — reversible, drop-to-manage, decent to the modders — turns directly into: surface an "update available" chip but never auto-pull, and make one-click endorse the highest-ethos-per-line feature on the board. Downloads/Collections are where Vortex's "auto-mutate your install" pain lives — exactly what we set out to beat. Stay out of that lane until it's a deliberate, separate decision.

## Ranked feature candidates

Effort/value/risk in the structured payload. Summary of the call:

1. **Richer rows from data already on the wire** (`next-now`) — read the dropped fields in `MapMod` + capture `file_details` in `MapMd5Response`; add nullable fields to `ModMeta` (+ `MergeMeta` + camelCase round-trip test). **Zero net-new API calls.** This is the cheapest, safest, highest-ethos-per-line win. Fixes the wrong "no reliable download count" comment — `mod_downloads` is right there.
2. **Persist the Nexus mod id at intake** (`next-now`, folded into #1) — capture `NexusMd5Match.ModId` onto a new `ModMeta.NexusModId` so every later feature has a stable handle without re-hashing. Tiny, but the prerequisite for #3 and #5. Do it in the same slice as #1.
3. **One-click endorse on the row** (`next-feature`) — first POST in the client; user-initiated, negligible rate cost. Pure honor-the-builders. Needs #2 (id) + the file version from #1. Surface Nexus's "must have downloaded / time-window" refusal gracefully; never auto-endorse.
4. **Rate-limit-aware client hardening** (`next-feature`, prerequisite for #5) — teach `SendAsync` to read `x-rl-*`, stop issuing when remaining is low, treat 429 as clean back-off + re-arm next cycle. Plus the `Application-Name`/`Application-Version` compliance headers. Load-bearing for any volume feature; small but must land *before* #5.
5. **"Updates available" via bulk `updated.json`** (`next-feature`, after #4) — THE headline, done the rate-sane way: one `updated.json` call per game-domain on a ~24h debounce (mirror `UpdateChecker`'s stamp), intersect with installed mod ids, deep-fetch only the changed few. Surface a quiet chip, never auto-pull. Honest framing is the differentiator.
6. **Inline changelog on update** (`later`) — lazy fetch when the user opens it; pairs with #5. Small once update awareness exists. Not worth building before there's an update to explain.
7. **Tracked-mods surfacing** (`later`) — one cheap GET, but it's a website-centric concept that's lower-fit for a drop-to-manage tool. Nice power-user touch; not a priority.
8. **Per-mod update sweep** (`cut`) — one `GetMod` per installed mod. This is the *anti-pattern*: same outcome as #5 at 100-200x the call cost. Cut in favor of the bulk primitive. Listed only to name it explicitly so nobody reaches for it.
9. **Downloads / `nxm://` / Collections** (`cut` for now) — crosses from metadata manager to downloader: premium gating, OS protocol-handler registration, the free-user nxm-only link constraint, large install flows. Explicitly scoped out of the original Nexus design. Revisit as a deliberate separate decision, not creep.
10. **Discovery lists** (`latest_*` / `trending`) (`cut`) — browse surface, not a manager's core loop. Scope creep.

## Recommended first slice

**#1 + #2 together: richer rows + persist the mod id.** One Core change set in `NexusRequests.cs` + `Mod.cs` + `Scanner.cs` (MergeMeta), test-first per the pattern in `NexusRequestsTests.cs`, camelCase round-trip test for the new `ModMeta` fields. Zero new network calls, zero new rate exposure, fully reversible (additive nullable fields — old `metadata.json` round-trips unchanged). It surfaces endorsements/downloads/version on rows and lays the id groundwork that endorse and update-check both need. Ship it, then decide on #4→#5 as the next deliberate step.

One field to be deliberate about: `contains_adult_content`. Capture it, but gate its display — don't auto-expand a thumbnail because of it.

## Open questions for the user

Crisp either/ors, in the structured payload. The load-bearing ones:

- **Update-check trigger:** auto on a 24h debounce (like the definitions feed) vs explicit "Check for updates" button vs both (auto-detect, manual-refresh)?
- **Rate budget:** hard ceiling (stop at N% of hourly remaining) vs best-effort-with-backoff? And: per-game throttle when a user has several games installed?
- **Endorse consent:** per-click only, or offer an opt-in "endorse everything I have installed" batch (with the Nexus download/time-window caveat surfaced)?
- **Persistence depth:** persist last-known-latest-version + last-checked per mod (enables offline "update available" display) vs fetch-fresh-each-cycle (less on disk, less to leak)?
- **Adult-content flag:** capture-and-gate-display vs don't-capture-at-all?
- **Compliance headers:** add `Application-Name`/`Application-Version` now (cheap, correct) or defer to the public-release checklist?

## What NOT to do (carried from the constraints)

- No embedded key, ever. Every call rides the user's runtime key.
- No per-mod update polling — bulk `updated.json` first, deep-fetch the diff.
- Read the `x-rl-*` headers; never retry into a 429 in a loop.
- No HTML scraping — the API exposes author/category/changelog/version.
- No auto-download or auto-update without explicit consent.
- A Nexus feature can never break a working install — offline / 429 / 401 / 404 all degrade silently to embedded/local behavior, same law as the remote manifest feed.
- Don't over-persist or phone home. Store only what a feature needs; the authenticated read calls are the only thing that should leave the machine.
