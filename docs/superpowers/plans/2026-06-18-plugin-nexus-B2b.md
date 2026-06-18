# Plugin slice — Phase B2b: bulk reads on the contract, restore the regression, delete Core's Nexus cluster

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Finish the extraction. Grow `IModSource` with the bulk reads (endorse-state + updated-window) and a rate-limit signal; rework `NexusRefresh` onto those Abstractions DTOs; **restore the B1-introduced regression** (library-wide heart sync + rate-limit-aware windowed poll); rewire the last call site; then **delete the Core Nexus client cluster** and relocate its tests — so `src/ModManager.Core` is finally Nexus-client-free.

**Spec:** [`2026-06-18-plugin-nexus-B2-design.md`](../specs/2026-06-18-plugin-nexus-B2-design.md). **Branch:** `feat/plugin-host-nexus-slice`.

**Two sub-stages:**
- **B2b-1 (Tasks 1–6): behavior migration.** Everything Nexus moves onto the contract; `NexusRefresh` reworked; the regression restored. Core's `NexusClient`/`INexusClient` still *exist* but end with **zero callers**. Mostly Core-TDD-able (`NexusRefresh` is Core; its tests switch the fake `INexusClient` → fake `IModSource`).
- **B2b-2 (Tasks 7–9): cluster deletion + test relocation.** Delete the now-orphaned Core Nexus cluster; relocate the client-impl tests to a new plugin test project; the "Core is Nexus-free" grep goes clean.

**Build/test (never bare `dotnet` at root; kill `ModManager.App` before App builds):** Core `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`; plugin `dotnet build plugins/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj`; App FULL `-p:Platform=x64`, STORE `+ -p:Configuration=Store`.

---

## B2b-1 — behavior migration

### Task 1: Grow the contract — bulk reads + rate-limit signal

**Files:** `src/ModManager.Plugins.Abstractions/Contract.cs`; test `tests/ModManager.Tests/Plugins/AbstractionsContractTests.cs`.

- [ ] **Step 1:** Add the bulk DTOs + the rate-limit exception + the two `IModSource` methods:

```csharp
/// <summary>One row of the user's bulk endorse state (mirrors Nexus /v1/user/endorsements.json).</summary>
public sealed record SourceEndorsement(int ModId, string DomainName, string Status);

/// <summary>One recently-updated mod in a game window (mirrors Nexus updated.json): unix-seconds file-update time.</summary>
public sealed record SourceUpdateEntry(int ModId, long LatestFileUpdate);

/// <summary>Thrown by a source when the service rate-limits (HTTP 429). Lets a bulk sweep stop and
/// report partial progress without the App referencing any provider-specific exception.</summary>
public sealed class SourceRateLimitException : Exception
{
    public SourceRateLimitException(string? message = null) : base(message ?? "Mod source rate limit reached.") { }
}
```

In `IModSource` add:
```csharp
    /// <summary>Bulk current-user endorse state across all games (one call). Read-only sync.</summary>
    Task<IReadOnlyList<SourceEndorsement>> GetUserEndorsementsAsync();
    /// <summary>Recently-updated mods for a game in a fixed window ("1d"/"1w"/"1m").</summary>
    Task<IReadOnlyList<SourceUpdateEntry>> GetRecentlyUpdatedAsync(string gameDomain, string period);
```

- [ ] **Step 2:** Update the contract test's fake to implement the two new methods (return small fixed lists). Run `--filter AbstractionsContractTests` → fails-to-compile then passes. Commit (`feat(plugins): contract bulk reads (endorse-state + updated-window) + SourceRateLimitException`).

### Task 2: Rework `NexusRefresh` onto `IModSource` + Abstractions DTOs

**Files:** `src/ModManager.Core/NexusRefresh.cs`; tests `tests/ModManager.Tests/Nexus/NexusRefreshTests.cs`, `NexusRefreshSweepTests.cs`, `NexusUserEndorsementsTests.cs` (these test Core's `NexusRefresh` — they **stay in Core**, switching the fake `INexusClient` → fake `IModSource` and the Core DTOs → Abstractions DTOs).

- [ ] **Step 1: Switch the test fakes + DTOs** — the `NexusRefresh` tests build a fake client + `NexusEndorsement`/`NexusUpdateEntry` lists; switch to a fake `IModSource` + `SourceEndorsement`/`SourceUpdateEntry`. Run → fails to compile (NexusRefresh still on `INexusClient`/Core DTOs). The failing step.
- [ ] **Step 2: Rework the methods** (signatures + bodies; `ResolveModId` unchanged):
  - `ApplyEndorsements(IEnumerable<ModMeta>, IEnumerable<SourceEndorsement>, string domain)` — identical body, `SourceEndorsement` instead of `NexusEndorsement` (same `.ModId`/`.DomainName`/`.Status`).
  - `SelectCandidates(IEnumerable<ModMeta>, IEnumerable<SourceUpdateEntry>, DateTime)` — identical body, `SourceUpdateEntry` (same `.ModId`/`.LatestFileUpdate`).
  - `Overlay(ModMeta existing, SourceModMetadata dto)` — **keep the selective-refresh semantics** (refresh only `EndorsementCount`/`Downloads`/`Available`/`NexusLatestVersion` from the DTO; preserve identity, installed `Version`/`NexusFileId`, `Endorsed`, `IsManual`). This is NOT `SourceMetadataMapper.Apply` (which refreshes identity too) — a stats refresh must never clobber a manual match's title. Read `dto.LatestVersion` into `NexusLatestVersion`.
  - `RefreshOneAsync(ModMeta existing, string domain, IModSource source)` — `ResolveModId`; `await source.FetchMetadataAsync(new SourceModRef("nexus", domain, id, existing.Version ?? ""))`; `Overlay(existing, dto)`.
  - `RefreshAllAsync(IEnumerable<ModMeta>, string domain, IModSource source, Func<Task>? throttle)` — same sweep; catch **`SourceRateLimitException`** (not `NexusRateLimitException`) for the partial-stop; the bulk endorse-sync calls `source.GetUserEndorsementsAsync()` (best-effort try/catch) → `ApplyEndorsements`. `NexusRefreshResult` unchanged.
- [ ] **Step 3:** Run `--filter "NexusRefresh|NexusUserEndorsements"` → green; `--filter CorePurity` green (NexusRefresh now references only Abstractions + ModMeta). Commit (`feat(core): NexusRefresh operates on IModSource + Abstractions bulk DTOs`).

### Task 3: Plugin implements the bulk reads + throws `SourceRateLimitException`

**Files:** `plugins/ModManager.Plugin.Nexus/NexusModSource.cs`.

- [ ] **Step 1:** Implement `GetUserEndorsementsAsync` (`GET /v1/user/endorsements.json` → `SourceEndorsement[]`) and `GetRecentlyUpdatedAsync(domain, period)` (`GET /v1/games/{domain}/mods/updated.json?period={period}` → `SourceUpdateEntry[]`), modeled on Core's `NexusRequests` mappers. On HTTP 429 from any call, throw `SourceRateLimitException` (replace the lean impl's silent handling). Build the plugin → 0 errors, refs only Abstractions. Commit (`feat(plugins): Nexus plugin bulk endorse-state + updated-window reads`).

### Task 4: Restore the regression — `RefreshNexusStats` + the poll onto the reworked path

**Files:** `src/ModManager.App/ViewModels/MainViewModel.cs` (`RefreshNexusStatsAsync`), `src/ModManager.App/Services/NexusUpdatePoll.cs`.

- [ ] **Step 1:** Rewire `RefreshNexusStatsAsync` to call `NexusRefresh.RefreshAllAsync(metas, domain, NexusSource, throttle)` (the loaded `IModSource`, not `_nexus.Client`) — restoring the **library-wide endorse-heart sync** (the bulk `GetUserEndorsementsAsync → ApplyEndorsements` inside `RefreshAllAsync`). Persist the result, surface the count + the rate-limited note as before.
- [ ] **Step 2:** Rewire `NexusUpdatePoll.MaybePollAsync` to the **windowed** path: `await source.GetRecentlyUpdatedAsync(domain, NexusRefresh.PeriodFor(elapsed))` → `NexusRefresh.SelectCandidates(metas, updated, lastPoll)` → `NexusRefresh.RefreshAllAsync(candidates, domain, source, throttle)`. Restore the rate-limit discipline: if the result `RateLimited`, **do NOT write the poll stamp** (so a throttled/partial poll retries next launch); write the stamp only on a clean sweep. Catch `SourceRateLimitException` from `GetRecentlyUpdatedAsync` itself the same way (skip stamp).
- [ ] **Step 3:** Kill `ModManager.App`; FULL + STORE build 0 errors. Commit (`fix(app): restore library-wide heart sync + rate-limited windowed poll via the contract`).

### Task 5: Rewire the last identify call site (manual-URL match)

**Files:** `src/ModManager.App/ViewModels/MainViewModel.cs` (`ResolveMatchFromUrlAsync`, ~L1555).

- [ ] **Step 1:** Replace the `_nexus.Client!.GetModAsync(domain, modId)` call with `NexusSource?.FetchMetadataAsync(new SourceModRef("nexus", domain, modId, ""))` mapped to `ModMeta` via `SourceMetadataMapper.Apply(new ModMeta { NexusModId = modId }, dto)` (manual match is identity-authoritative — `Apply` is right here, unlike the stats refresh). Null source → the existing "couldn't match" status. FULL + STORE build 0 errors. Commit (`feat(app): manual-URL Nexus match routes through IModSource`).

### Task 6: B2b-1 checkpoint — everything on the contract, NexusClient orphaned

- [ ] **Step 1:** Full Core suite green; plugin + FULL + STORE build; STORE dll loader-free. **Confirm `NexusClient`/`INexusClient` now have ZERO callers in `src/`** (`grep -rn "INexusClient\|_nexus.Client\|new NexusClient" src/` → only `NexusService.Client` property + the type defs remain). This is the B2b-1 done-state: all behavior on the contract; the cluster is orphaned, ready to delete. Commit any smoke-doc update + **strike the ⚠️ regression note in `pending.md`** (it's restored).

---

## B2b-2 — delete the cluster + relocate tests

### Task 7: Delete the Core Nexus client cluster

**Files:** DELETE from `src/ModManager.Core/`: `NexusClient.cs`, `NexusRequests.cs`, `NexusEndorse.cs`, `NexusRateLimit.cs`, `NexusOptions` (in NexusRequests.cs), `INexusClient` + the client DTOs (`NexusMd5Match`, `NexusUser`, `NexusUpdateEntry`, `NexusEndorsement`, `EndorseOutcome`, `EndorseAction`, `ApiRequest` if Nexus-only). `src/ModManager.App/Services/NexusService.cs`: drop the `Client` property + the `NexusClient`/`NexusOptions` usage; **keep** the credential store + `ConnectAsync`/`ValidateAsync` (rework `ValidateAsync` to a minimal inline `GET /v1/users/validate.json` or move validation behind a tiny contract method — decide in-task; the key store + connection state stay App-side).

- [ ] **Step 1:** Delete the files; fix every resulting compile error (there should be none in `src/` if B2b-1 left zero callers — except `NexusService`). Grounding check first: `grep -rn "NexusClient\|NexusRequests\|INexusClient\|NexusMd5Match\|NexusEndorsement\|NexusUpdateEntry\|NexusRateLimit" src/` and resolve each. `NexusDomains` + `NexusRefresh` + `NexusPollStamp` + `ModMeta.Nexus*` fields STAY (our types). Core + FULL + STORE build 0 errors. Commit (`refactor(core): delete the Nexus client cluster — Core is Nexus-client-free`).

### Task 8: Relocate the client-impl tests to a plugin test project

**Files:** Create `tests/ModManager.Plugin.Nexus.Tests/` (refs the plugin + Abstractions + xUnit). MOVE the tests of the deleted impl: `NexusClientTests`, `NexusRequestsTests`, `CategoryTests`, `NexusPostBodyTests`, `NexusRateLimitTests`, `NexusUpdatedTests`, `NexusEndorseTests` — reworked to test `NexusModSource` (the plugin) instead of the deleted `NexusClient`/`NexusRequests`, plus the B1-review-requested `SetEndorsedAsync` refusal/429 tests. **Stay in `tests/ModManager.Tests/`:** the `NexusRefresh*` tests (switched to fake `IModSource` in B2b-1 — they test Core's `NexusRefresh`) and the Scanner-identify tests (B2a). **`NexusUserEndorsementsTests` is split:** its `ApplyEndorsements` section (on `SourceEndorsement`) stays in Core; its client-request / `GetUserEndorsementsAsync` sections move here (they exercise the deleted `NexusRequests`/`NexusClient`, reworked over `NexusModSource`). Delete from the Core test project only the moved ones.

- [ ] **Step 1:** Scaffold the project; move + rework the impl tests (HttpMessageHandler-mock over `NexusModSource`); remove the obsolete ones from `tests/ModManager.Tests/`. `dotnet test tests/ModManager.Plugin.Nexus.Tests/...` green; `dotnet test tests/ModManager.Tests/...` green. Commit (`test(plugins): relocate Nexus client tests to the plugin test project`).

### Task 9: Final verify — Core is Nexus-free

- [ ] **Step 1:** Full Core suite + plugin tests + FULL + STORE builds 0 errors; STORE dll loader-free. **The done-when grep:** `grep -rn "Nexus" src/ModManager.Core/` returns ONLY `NexusDomains` (manifest facade), `NexusRefresh`/`NexusPollStamp` (ModMeta helpers), `SourceMetadataMapper`, and `ModMeta.Nexus*` field names — **no `NexusClient`/`NexusRequests`/`INexusClient`/`NexusOptions`**. Append the final smoke entries (heart sync restored + verifiable; the cluster gone). Commit.

---

## Notes
- **The regression restore (Task 4) is the highest-value behavior change** — verify the library-wide heart sync (a website endorsement reflects after "Refresh Nexus stats") and the rate-limited poll (a 429 leaves the stamp unwritten) actually work, not just compile. This is what the B2a review flagged.
- **`Overlay` ≠ `SourceMetadataMapper.Apply`:** Overlay preserves identity (for the refresh sweep / manual matches); Apply refreshes it (for identify). Keep them distinct — conflating them re-introduces a clobber.
- **Laws:** Core + Abstractions stay pure; the heart-wipe guard holds (Overlay preserves `Endorsed`; the bulk sync is the only writer, best-effort); plugin refs only Abstractions; no file-op surface.
