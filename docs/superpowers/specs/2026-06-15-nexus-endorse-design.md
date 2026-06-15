# Nexus endorse from the app

> Design doc. Grounded in the 2026-06-15 endorse-verification workflow (run `wf_73a66dc0-09f`). Closes the Nexus arc: enrichment (#144) → refresh/updates (#145) → **endorse**. Builds on the rate-limit-hardened client from #145.

**Goal:** Let the user endorse (and un-endorse) the Nexus mods they actually use, one click per row — the give-back half of the Nexus loop. Honors-the-builders, never automatic.

## Verified facts this rests on

- One endpoint family handles both: **POST** `/v1/games/{domain}/mods/{modId}/{endorse|abstain}.json` with body `{ "Version": "<installed version>" }`; returns `{ message, status }` where `status` ∈ `Undecided` | `Abstained` | `Endorsed` (node-nexus-api `endorseMod`, `Nexus.ts:659-672`, `types.ts:54,504-510`).
- The current-user endorse status per mod is also available in bulk: **GET** `/v1/user/endorsements.json` → `[{ mod_id, domain_name, version, date, status }]` across all games (`types.ts:493-499`). One cheap call gives accurate state for the whole library.
- The client is **GET-only today**, but `ApiRequest` already carries a `Body` (`CurseForgeRequests.cs:7`) and `CurseForgeClient.SendAsync` shows the POST pattern (`CurseForgeClient.cs:39-57`: attach `StringContent` JSON when `Body` is set, skip the `Content-Type` header when copying). `NexusClient.SendAsync` ignores `req.Body` (`NexusClient.cs:72-86`) — **this is the one real client gap.** The 429/`x-rl`/ToS-header path from #145 already runs in `SendAsync`, so endorse inherits it.
- `ModMeta` has `NexusModId` (the endorse key) + `EndorsementCount` (others' count), but **no field for the current user's endorse state** — that's new.
- Limits reconfirmed 20k/day + 500/hr (help article 105, reached this run).

## The doc gap (and why it doesn't block)

Nexus's exact refusal **codes** and the post-download **wait-window value** are not in any public source reachable without auth (help.nexusmods, SwaggerHub — 403/404/auth-gated). The two known codes referenced in the client are `NOT_DOWNLOADED_MOD` and `TOO_SOON_AFTER_DOWNLOAD`. **Mitigation:** the endorse response body carries a human-readable `message`. On a 4xx refusal we read that message and surface it (mapping the two known codes to friendlier text), and never throw. We don't need the exact strings to behave correctly — the API tells us why it refused.

## Architecture

**Client write path (Core):**
- `NexusClient.SendAsync` gains body support — mirror `CurseForgeClient`: when `req.Body` is non-null, attach `new StringContent(req.Body, Encoding.UTF8, "application/json")` and skip copying a `Content-Type` header onto `msg.Headers`. GET behavior is unchanged. 429 → `NexusRateLimitException` (already there); `x-rl` capture unchanged.
- `NexusRequests.EndorseRequest(domain, modId, version, EndorseAction action, opts)` → `POST .../mods/{modId}/{endorse|abstain}.json`, body `{ "Version": version }`, the usual auth + ToS headers. `EndorseAction` is an enum `{ Endorse, Abstain }` mapping to the path segment.
- `INexusClient.EndorseAsync(domain, modId, version, action)` → `EndorseOutcome { EndorsedStatus? Status, string? Message, bool Refused }`. On 2xx → `Status` from the response. On a 4xx precondition refusal → `Refused = true` + the human `Message` (parsed from the body; the two known codes mapped to friendly text) — no throw. 429 propagates as `NexusRateLimitException`.
- `NexusRequests.UserEndorsementsRequest(opts)` → `GET /v1/user/endorsements.json`; `MapUserEndorsements` → `IReadOnlyList<NexusEndorsement>` (`record NexusEndorsement(int ModId, string DomainName, string Status)`). `INexusClient.GetUserEndorsementsAsync()`.

**Data (Core):** new persisted field `ModMeta.Endorsed` (`bool?` — null = unknown, true = endorsed, false = abstained/undecided). Three-places: `ModMeta` → `MergeMeta` carry-through → in-memory `Mod.Endorsed` → `MergeMetadata` copy → row VM. camelCase round-trip tested. `Endorsed` is *persisted user intent* (like `IsManual`), not computed — it must survive a rescan.

**State accuracy (Core):** `NexusRefresh.ApplyEndorsements(metas, endorsements, domain)` — pure: given the bulk list, set each mod's `Endorsed = (status == "Endorsed")` for entries matching the active domain. `RefreshAllAsync` (the manual sweep) and the auto-check each make **one** `GetUserEndorsementsAsync()` call and apply it, so hearts reflect reality library-wide without per-mod calls. An endorse/abstain *action* updates `Endorsed` immediately from its response.

**App:** `MainViewModel.ToggleEndorseAsync(row)` — guard on connected + `NexusModId`; pick `EndorseAction` from the current `Endorsed` (`true → Abstain`, else `Endorse`); pass the installed `Version` (`?? NexusLatestVersion`); on success update `Endorsed` + `WriteOneMeta` + status line; on `Refused` show the friendly message; on `NexusRateLimitException` show "rate limit reached — try again later." Never batched, never automatic.

## UI

A heart affordance on each row (in the chip area / icon-button column), visible only when `NexusModId` is set **and** Nexus is connected. Filled accent heart = endorsed; outline = not. Click toggles. Tooltip: "Endorse on Nexus" / "Endorsed — click to retract." Refusals + rate-limit feedback go to the status line (the row stays honest — no optimistic flip on failure).

## Laws / non-negotiables

- Pure-Core: request building, body/POST plumbing, outcome + refusal parsing, the status→bool apply, version selection — all in Core behind tests. App is the button + handler + VM method. `CorePurityTests` green.
- Additive + reversible: one new nullable `ModMeta.Endorsed`; camelCase round-trip; old `metadata.json` round-trips unchanged. Endorse is itself reversible (abstain). No local-file destructive op. No embedded key (personal key only).
- Never auto-endorse: every endorse/abstain is a per-click user action. The bulk list is read-only state sync, not a write.
- API failure never crashes: refusals + offline + 429 degrade to a status line, never an unhandled throw.

## Out of scope (follow-ups)

- A batch "endorse everything I have installed" action (deliberately deferred — consent; per-click only for now).
- The GraphQL collection-endorse path (collections aren't modeled in the launcher).
- Tri-state surfacing of Abstained vs Undecided (the heart only needs endorsed-or-not; the bool captures that).
