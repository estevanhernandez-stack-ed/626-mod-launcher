# Plugin slice — Phase B2: Core goes Nexus-free (read-path rewire) — design

**Date:** 2026-06-18
**Status:** Design — pending spec review
**Branch:** `feat/plugin-host-nexus-slice` (continues the slice)
**Project:** `DP1YCsh7iAN1yAiR8sAd`

## Goal

Finish the Nexus extraction: rewire Core's scan-time identify/enrichment to the Abstractions contract, move the entire Nexus implementation cluster out of Core into the plugin, and relocate the Nexus tests — so **Core ends up truly Nexus-free** and the B1 lean-client duplication is resolved. Decision (2026-06-18): **full removal** — the Store SKU consequently does no Nexus identification at all (mods show as bare filenames there), the accepted cost of the sealed Nexus-free core.

## Grounded coupling (verified)

Core's Nexus use is **injected by parameter**, not held as a field — which makes the rewire tractable:

- `Scanner.Md5IdentifyAsync(ctx, INexusClient nexus, fileNames)` ([Scanner.cs:1255](../../../src/ModManager.Core/Scanner.cs#L1255)) — per-file md5 → `GetByMd5Async` → `NexusMd5Match{ModId, ModMeta}` → `MergeMeta`.
- `Scanner.Md5IdentifyArchivesAsync(ctx, INexusClient nexus, droppedPaths)` ([:1291](../../../src/ModManager.Core/Scanner.cs#L1291)) — archive md5 → `GetByMd5Async` → map to every key the zip installs.
- `Scanner.IdentifyVortexNexusAsync(ctx, INexusClient client)` ([:1346](../../../src/ModManager.Core/Scanner.cs#L1346)) — Vortex-recorded modId → `GetModAsync(domain, id)` → `ModMeta`.
- `Ue4ssLuaInstaller.IdentifyMetadataAsync(ctx, INexusClient nexus, archivePath, modName)` ([Ue4ssLuaInstaller.cs:124](../../../src/ModManager.Core/Ue4ssLuaInstaller.cs#L124)) — archive md5 → `GetByMd5Async`.

All four take `INexusClient` and produce/merge `ModMeta`. `NexusDomains.Effective(ctx.Game)` (manifest-derived, **stays in Core**) supplies the domain.

**Moves vs. stays — "Nexus-free Core" means no Nexus *client/API* code in Core, NOT no Nexus-named helpers on our own types:**
- **Moves to the plugin** (Nexus HTTP/API code): `NexusClient`, `NexusRequests` (reworked — see below), `NexusEndorse`, `NexusRateLimit`, `NexusOptions`. `INexusClient` is **deleted** (replaced by `IModSource`). `NexusMd5Match` (it *holds* a `ModMeta`, so it can't move — **deleted**, replaced by the Abstractions `SourceIdentifyResult`); `NexusUser`/`EndorseOutcome`/`EndorseAction` move or fold into Abstractions.
- **Stays in Core** (operates on our own `ModMeta`, not the Nexus API): `NexusDomains` (manifest facade), and **`NexusRefresh`** — `ResolveModId`/`Overlay`/`ApplyEndorsements`/`SelectCandidates` are `ModMeta`/merge logic, not HTTP. They can't live in the plugin (it's `ModMeta`-free).
- **The intricate straddle the plan must resolve:** `NexusRefresh.ApplyEndorsements` + `SelectCandidates` take the bulk DTOs `NexusEndorsement` / `NexusUpdateEntry` (returned by the bulk endorse-state + updated-by-game calls). Those bulk reads aren't in B1's contract (the B1 review flagged them as the contract gap). B2 must add them to `IModSource` (a bulk endorse-state read + an updated-window read) returning **Abstractions** DTOs, and rework `NexusRefresh` to operate on those Abstractions DTOs (so the bulk-sync logic stays in Core on our types while the HTTP moves to the plugin). This is the subtlest part and the reason B2 likely **sub-stages** (see Plan).

> **B2b also RESTORES a B1-introduced regression (caught by the B2a adversarial review).** B1 prematurely moved `MainViewModel.RefreshNexusStatsAsync` + `NexusUpdatePoll.MaybePollAsync` off the bulk `NexusRefresh.RefreshAllAsync` path onto per-mod `FetchMetadataAsync`, which silently dropped (a) the **library-wide endorse-heart sync** (bulk `GetUserEndorsementsAsync → ApplyEndorsements`, reflecting website endorsements) and (b) the **rate-limit-aware windowed poll** (now per-mod, no 429 backoff, stamp written on partial polls). `NexusRefresh.RefreshAllAsync`/`SelectCandidates`/`ApplyEndorsements` are currently **orphaned**. Adding the bulk endorse-state + updated-window reads to `IModSource` and rewiring those two call sites onto them is exactly how B2b re-enables both features — so B2b must explicitly verify the library-wide heart sync and the windowed/rate-limited poll work again (not just compile). (Reverting them to `NexusClient` now was deliberately *not* done — it would be throwaway work B2b immediately re-does on the contract; the regression is documented in `docs/smoke-tests/pending.md` meanwhile.)

`NexusRequests.MapMod` returns Core's `ModMeta` today — the deepest coupling: in the plugin it must produce the Abstractions DTO instead, and Core's `SourceMetadataMapper` does DTO→`ModMeta`.

## The design crux — growing the contract

Scanner's identify builds a full `ModMeta` (via `MapMod`): `Title, Description, Author, AuthorUrl, Image, Url, Downloads, EndorsementCount, Version, Available, ContainsAdultContent, NexusModId, NexusFileId, Category`. The B1 slim `SourceModMetadata` carries only 5 of these. So the contract must **grow** to carry the identity/credit fields, and identify must return them in one shot (md5_search returns the mod object inline — one call yields ref + full metadata).

**Abstractions changes:**
- Grow `SourceModMetadata` to carry the identity/credit fields Scanner needs:
  `Title, Description, Author, AuthorUrl, ImageUrl, ModUrl, Category, ContainsAdultContent (bool?), NexusFileId (int?)` added to the existing `Endorsements/Downloads/LatestVersion/Available/Endorsed`. (`Endorsed` stays nullable — the heart-wipe guard holds.)
- Change `IdentifyByHashAsync` to return a combined result: `SourceIdentifyResult?` = `(SourceModRef Ref, SourceModMetadata Metadata)` — so a single md5 call yields both the id and the full metadata (no second round-trip), matching today's `NexusMd5Match{ModId, Meta}`.
- `FetchMetadataAsync(ref)` already serves the by-id path (the ref carries domain+modId) — it returns the grown `SourceModMetadata`. The Vortex path uses this.

**Core changes:**
- `SourceMetadataMapper.Apply(ModMeta, SourceModMetadata)` grows to map every new field onto `ModMeta` (preserving the nullable-never-clobber discipline). It produces the Nexus-sourced `ModMeta` that Scanner then `MergeMeta`s exactly as today (Nexus authoritative for identity; curated/CF fills gaps) — the merge semantics are unchanged; only the *source* of the Nexus `ModMeta` changes (DTO-mapped instead of `MapMod`-built).

## The rewire

1. **Scanner's 4 entry points take `IModSource?` instead of `INexusClient`.** Each: resolve `domain` (unchanged, `NexusDomains.Effective`); if `source` is null → `IdentifyResult(0)` (graceful no-op — the zero-plugins/STORE path); else call `IdentifyByHashAsync`/`FetchMetadataAsync`, map the DTO→`ModMeta` via `SourceMetadataMapper`, and `MergeMeta` as today. `Ue4ssLuaInstaller.IdentifyMetadataAsync` the same.
2. **The Nexus cluster moves to `plugins/ModManager.Plugin.Nexus`.** `NexusRequests.MapMod` is reworked to produce the Abstractions `SourceModMetadata` (not `ModMeta`). The plugin's B1 lean re-implementation is **replaced** by the moved full machinery (rate-limit handling, category cache, endorse, bulk endorsements, updated-by-game) now implementing the grown `IModSource`. The B1 duplication is resolved.
3. **App call sites** (where `Scanner.Md5Identify*` / `IdentifyVortexNexus` / `Ue4ssLua.IdentifyMetadata` are invoked) pass `_sources.ById("nexus")` (the `IModSource?`) instead of `_nexus.Client`. `NexusService` keeps the credential store + connection state (the host's credential getter), but its `Client` (the in-Core `NexusClient`) is gone — the App talks to Nexus only through the registry's plugin now.

## Test relocation

- Tests of the **moved impl** (`NexusClientTests`, `NexusRequestsTests`, `CategoryTests`, `NexusPostBodyTests`, `NexusRateLimitTests`, `NexusUpdatedTests`, `NexusEndorseTests`) → a **new plugin test project** `tests/ModManager.Plugin.Nexus.Tests/` referencing the plugin + Abstractions. This is also where the B1-review-requested `NexusModSource` endorse-refusal unit tests land (HttpMessageHandler-mock over `SetEndorsedAsync`).
- **Stay in Core** (corrected 2026-06-18 — B2b-1 made this true): `NexusRefreshTests`, `NexusRefreshSweepTests` test Core's `NexusRefresh` (ModMeta logic), not the client — B2b-1 already switched their fake `INexusClient` → fake `IModSource` + Core DTOs → Abstractions DTOs, so they stay in `tests/ModManager.Tests/`. **`NexusUserEndorsementsTests` is split:** its `ApplyEndorsements` section (now on `SourceEndorsement`) stays in Core; its client-request / `GetUserEndorsementsAsync` sections move to the plugin project in B2b-2 when `NexusRequests`/`NexusClient` are deleted.
- Tests of **Scanner's identify** (`Md5IdentifyTests`, `Md5IdentifyFromsoftTests`, `VortexNexusIdentifyTests`, `Ue4ssLuaMetadataTests`) — these test Core's Scanner using a **fake `INexusClient`** today; they **stay in the Core test project** but switch the fake to a **fake `IModSource`** (Scanner is still Core; only the injected contract type changes).

## STORE / zero-plugins behavior

With identify plugin-provided, the STORE SKU (and FULL before a plugin loads) does **no Nexus identification or enrichment** during scans — mods surface as their filenames with no Nexus title/author/endorse/version/update. Core scanning, intake, enable/disable, profiles, save/INI editors, and the ban-risk gate all work unchanged. This is the accepted sealed-core cost.

## Done-when

- `CorePurityTests` green **and** a grep of `src/ModManager.Core/` for `Nexus` returns only `NexusDomains` (the manifest-derived domain facade, which stays) + the `SourceMetadataMapper` — **no `NexusClient`/`NexusRequests`/`INexusClient`/`NexusOptions` left in Core.**
- The plugin builds referencing **only Abstractions** (the moved cluster maps to DTOs, never `ModMeta`); the B1 lean duplication is gone.
- FULL flavor: scan-time identify + the user-facing actions all work through the plugin (parity with pre-B1 Core behavior, dev-signed plugin).
- STORE flavor: builds 0 errors, loader still absent, app runs with no Nexus surface and no scan identify, core intact.
- Full Core suite green; the relocated plugin tests green in the new project.

## Surfaces touched

| Path | Change |
|---|---|
| `src/ModManager.Plugins.Abstractions/Contract.cs` | grow `SourceModMetadata` (identity/credit fields); `IdentifyByHashAsync` → `SourceIdentifyResult?` (ref + metadata) |
| `src/ModManager.Core/Plugins/SourceMetadataMapper.cs` | map the grown DTO → `ModMeta` (nullable-never-clobber) |
| `src/ModManager.Core/Scanner.cs` | `Md5IdentifyAsync` / `Md5IdentifyArchivesAsync` / `IdentifyVortexNexusAsync` take `IModSource?`; map + `MergeMeta`; null-source → no-op |
| `src/ModManager.Core/Ue4ssLuaInstaller.cs` | `IdentifyMetadataAsync` takes `IModSource?` |
| `src/ModManager.Core/` (Nexus cluster) | **DELETE** `NexusClient`/`NexusRequests`/`NexusEndorse`/`NexusRateLimit`/`NexusUpdateEntry`/`NexusEndorsement`/`INexusClient`/`NexusOptions`/`NexusRefresh` + DTOs (moved) |
| `plugins/ModManager.Plugin.Nexus/` | receives the moved cluster reworked to `IModSource` + DTOs; B1 lean impl replaced |
| `src/ModManager.App/` | call sites pass `_sources.ById("nexus")`; `NexusService.Client` removed (credential store stays) |
| `tests/ModManager.Plugin.Nexus.Tests/` | **NEW** — relocated impl tests + the endorse-refusal unit tests |
| `tests/ModManager.Tests/` | Scanner-identify tests switch the fake from `INexusClient` → `IModSource`; Nexus-impl tests removed (relocated) |
| `src/ModManager.Core/Manifest/` | `NexusDomains` **stays** (manifest-derived domain facade — not Nexus client code) |
