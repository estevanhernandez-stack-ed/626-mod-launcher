# Nexus by-mod-id poll — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Refresh Nexus stats for the installed library by polling Nexus *by mod id* (no archive), and flag updates-available — via a manual sweep and a debounced auto-check.

**Architecture:** One Core refresh primitive (`GetMod` by id → refresh stats + capture latest version, preserve installed version) fed two ways: a full manual sweep and an `updated.json`-narrowed auto-check. Rate-limit-hardened client underneath. See `docs/superpowers/specs/2026-06-14-nexus-by-mod-id-poll-design.md`.

**Tech Stack:** .NET 10, C#, xUnit. Pure-Core + thin App (WinUI 3). camelCase JSON on disk via `AtomicJson`.

**Conventions:** Every Core behavior starts with a failing xUnit test in `tests/ModManager.Tests/`. `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` for Core; `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` for App (kill any running `ModManager.App` first — it locks `ModManager.Core.dll`). Never bare `dotnet build` at root.

---

### Task 1: Rate-limit hardening on the Nexus client

**Files:**
- Modify: `src/ModManager.Core/NexusRequests.cs` (the `Headers` builder, ~line 28-33)
- Modify: `src/ModManager.Core/NexusClient.cs` (`SendAsync`, ~line 42-55)
- Create: `src/ModManager.Core/NexusRateLimit.cs` (record + typed exception)
- Test: `tests/ModManager.Tests/Nexus/NexusRateLimitTests.cs`

- [ ] **Step 1: Failing tests.** `NexusRateLimit.Parse` reads `x-rl-daily-remaining` / `x-rl-hourly-remaining` (+ `-limit`) from a header dictionary into `NexusRateLimit { int? DailyRemaining, HourlyRemaining, DailyLimit, HourlyLimit }`; missing headers → nulls. A 429 response surfaces as `NexusRateLimitException` (subtype carrying the parsed `NexusRateLimit`), not bare `HttpRequestException`.
- [ ] **Step 2: Implement.** Add `NexusRateLimit` record + `Parse(IEnumerable<KeyValuePair<string,IEnumerable<string>>>)`. Add `NexusRateLimitException : Exception`. In `SendAsync`: after the send, parse the headers; if `StatusCode == 429` throw `NexusRateLimitException(parsed)`; otherwise keep existing behavior. Expose the most-recent `NexusRateLimit` on the client (property `LastRateLimit`).
- [ ] **Step 3: ToS headers.** In `NexusRequests.Headers`, always add `Application-Name: 626-mod-launcher` and `Application-Version: {assembly version}` (pass the version into `NexusOptions` or read from the entry assembly in the client; keep `NexusRequests` pure by passing the version string through `NexusOptions`). Add a test asserting both headers are present on a built request.
- [ ] **Step 4:** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` → green. Commit `feat(nexus): rate-limit-aware client (x-rl headers, typed 429) + ToS app headers`.

### Task 2: `updated.json` wiring

**Files:**
- Modify: `src/ModManager.Core/NexusRequests.cs` (new `UpdatedRequest` builder + `MapUpdatedResponse` mapper)
- Modify: `src/ModManager.Core/NexusClient.cs` (`GetRecentlyUpdatedAsync`) + `INexusClient`
- Create: `src/ModManager.Core/NexusUpdateEntry.cs` (`record NexusUpdateEntry(int ModId, long LatestFileUpdate, long LatestModActivity)`)
- Test: `tests/ModManager.Tests/Nexus/NexusUpdatedTests.cs`

- [ ] **Step 1: Failing tests.** `UpdatedRequest("eldenring", "1w", opts)` builds `GET {base}/v1/games/eldenring/mods/updated.json?period=1w` with the auth + ToS headers. `MapUpdatedResponse(json)` maps `[{"mod_id":123,"latest_file_update":1700000000,"latest_mod_activity":1700000001}]` → one `NexusUpdateEntry(123, 1700000000, 1700000001)`. Empty array → empty list.
- [ ] **Step 2: Implement** the builder + mapper (reuse the `Long`/`Int` helpers) + `INexusClient.GetRecentlyUpdatedAsync(string domain, string period)` → `Task<IReadOnlyList<NexusUpdateEntry>>`, sending via the same `SendAsync` path (so it's rate-limit-aware).
- [ ] **Step 3:** Core tests green. Commit `feat(nexus): wire updated.json (bulk recently-updated by game)`.

### Task 3: `NexusLatestVersion` field + three-places carry-through + update-available compare

**Files:**
- Modify: `src/ModManager.Core/Mod.cs` (`ModMeta.NexusLatestVersion` string?; in-memory `Mod.NexusLatestVersion` string? + computed `bool UpdateAvailable`)
- Modify: `src/ModManager.Core/Scanner.cs` (`MergeMeta` carry-through, ~line 1141-1170)
- Modify: `src/ModManager.Core/Metadata.cs` (`MergeMetadata` copy, ~line 56-65)
- Test: `tests/ModManager.Tests/MetadataMergeTests.cs` (extend existing) + a camelCase round-trip test

- [ ] **Step 1: Failing tests.** (a) camelCase round-trip: serializing a `ModMeta` with `NexusLatestVersion="2.1"` emits `"nexusLatestVersion"` (not Pascal) and round-trips. (b) `MergeMeta` carries `NexusLatestVersion` through (curated `?? cf` per-field, `IsManual` short-circuit consistent with siblings). (c) `MergeMetadata` copies `NexusLatestVersion` onto `Mod`; `Mod.UpdateAvailable` is true iff `NexusLatestVersion` is non-null and `!= Version`, false when equal or when `NexusLatestVersion` is null.
- [ ] **Step 2: Implement** the field on `ModMeta`, the in-memory `Mod.NexusLatestVersion` + `UpdateAvailable => NexusLatestVersion is { } v && v != Version`, the `MergeMeta` line, the `MergeMetadata` copy.
- [ ] **Step 3:** Core tests green. Commit `feat(nexus): persist NexusLatestVersion + computed UpdateAvailable (three-places)`.

### Task 4: The refresh primitive + id resolution (Core service)

**Files:**
- Create: `src/ModManager.Core/NexusRefresh.cs`
- Test: `tests/ModManager.Tests/Nexus/NexusRefreshTests.cs` (use a fake `INexusClient`)

- [ ] **Step 1: Failing tests.** (a) `NexusRefresh.ResolveModId(meta)` returns `NexusModId` when set; else parses `Url` via `ModSiteUrl` (Nexus only) → id; else null (CurseForge URL → null). (b) `RefreshOneAsync(existing, domain, client)`: given a fake client returning a mod with endorsements/downloads/version="2.1"/available, returns an updated `ModMeta` with refreshed `EndorsementCount`/`Downloads`/`Available`, `NexusLatestVersion="2.1"`, and the **installed** `Version`/`NexusFileId` **unchanged**. (c) a meta with no resolvable id → returns null (skipped), no client call. (d) client throws `NexusRateLimitException` → propagates (caller handles).
- [ ] **Step 2: Implement** `ResolveModId` + `RefreshOneAsync`. Map only stats + latest version from the `GetMod` result; copy them onto a clone of `existing` (preserve installed fields, `IsManual`, `Source`, `Title`, etc.).
- [ ] **Step 3:** Core tests green. Commit `feat(nexus): RefreshOne primitive + mod-id resolution (id or parsed URL)`.

### Task 5: Sweep + candidate selection + period selection (Core orchestration)

**Files:**
- Modify: `src/ModManager.Core/NexusRefresh.cs` (add `RefreshAllAsync`, `SelectCandidates`, `PeriodFor`)
- Test: `tests/ModManager.Tests/Nexus/NexusRefreshSweepTests.cs`

- [ ] **Step 1: Failing tests.** (a) `PeriodFor(elapsed)`: `<1d → "1d"`, `<7d → "1w"`, else `"1m"`. (b) `SelectCandidates(installedMetas, updatedEntries, baselineUtc)` returns metas whose resolved id is in the entry set AND `latest_file_update` (unix → DateTime) > the mod's baseline. (c) `RefreshAllAsync(metas, domain, client, throttle)` calls `RefreshOneAsync` per resolvable meta, returns `NexusRefreshResult { int Refreshed, int UpdatesAvailable, bool RateLimited, IReadOnlyList<ModMeta> Updated }`; on a `NexusRateLimitException` mid-sweep it stops, sets `RateLimited=true`, and returns partial progress (no throw).
- [ ] **Step 2: Implement.** Throttle = an injectable delay/concurrency cap (default small, under the burst ceiling); keep the method testable by injecting a no-op delay in tests. `UpdatesAvailable` counts results where latest != installed.
- [ ] **Step 3:** Core tests green. Commit `feat(nexus): sweep + candidate/period selection (rate-limit-stop, partial progress)`.

### Task 6: Persist the sweep results + per-game poll stamp

**Files:**
- Modify: `src/ModManager.Core/Scanner.cs` (a `WriteManyMeta(ctx, IEnumerable<(name, meta)>)` helper if not present, reusing `LoadMetadata`/`SaveMetadata`)
- Create: `src/ModManager.Core/NexusPollStamp.cs` (read/write a per-game last-poll timestamp; pure given a path)
- Test: `tests/ModManager.Tests/Nexus/NexusPollStampTests.cs` + a `WriteManyMeta` round-trip test (temp dir)

- [ ] **Step 1: Failing tests.** `NexusPollStamp.ShouldPoll(lastUtc, nowUtc, TimeSpan.FromHours(24))` true when >24h or null. Read-after-write round-trips the stamp. `WriteManyMeta` persists updated metas and they reload via `LoadMetadata` with `NexusLatestVersion` intact (camelCase).
- [ ] **Step 2: Implement.** Keep `NexusPollStamp` pure (path in, value out); the App passes `%LOCALAPPDATA%\ModManagerBuilder\last-nexus-poll-<gameId>.txt`.
- [ ] **Step 3:** Core tests green. Commit `feat(nexus): persist sweep results + per-game poll stamp`.

### Task 7: App — manual "Refresh Nexus stats" action + menu wiring

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (`RefreshNexusStatsAsync`)
- Modify: `src/ModManager.App/MainWindow.xaml` (menu item next to Backfill, ~line 78) + `MainWindow.xaml.cs` (`OnNexusRefresh`)

- [ ] **Step 1: Implement** `RefreshNexusStatsAsync()`: guard on `_ctx`, `_nexus.IsConnected`, `NexusDomains.Effective(_ctx.Game)`; gather identified metas; `NexusRefresh.RefreshAllAsync`; `WriteManyMeta`; `ReloadModsAsync`; status = `Refreshed {n} mod(s), {m} update(s) available` or `Nexus rate limit reached — try again later.` Wrap in `IsBusy`/try-catch like `BackfillNexusAsync`.
- [ ] **Step 2:** Add `MenuFlyoutItem Text="Refresh Nexus stats…" Click="OnNexusRefresh"` (with tooltip) as a sibling of Backfill; `OnNexusRefresh` → `await ViewModel.RefreshNexusStatsAsync()`.
- [ ] **Step 3:** `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (kill running app first) → 0 errors. Commit `feat(nexus): manual Refresh Nexus stats action`.

### Task 8: App — debounced auto-check service + setting

**Files:**
- Create: `src/ModManager.App/Services/NexusUpdatePoll.cs` (modeled on `UpdateChecker`)
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (invoke after game load) + `Services/AppSettingsService.cs` + `SettingsDialog.xaml(.cs)` (the toggle)

- [ ] **Step 1: Implement** `NexusUpdatePoll.MaybePollAsync(ctx, nexus, settings)`: bail unless setting on + connected + domain set + `NexusPollStamp.ShouldPoll`; `GetRecentlyUpdatedAsync(domain, NexusRefresh.PeriodFor(elapsed))`; `SelectCandidates`; `RefreshAllAsync` over candidates; `WriteManyMeta`; write stamp. Swallow all exceptions (offline/429/bad data) — log via `AppDiagnostics`, never surface a crash.
- [ ] **Step 2:** Setting `AutoCheckModUpdates` (default true) in `AppSettingsService` + a toggle in `SettingsDialog` ("Check for mod updates automatically"). Invoke `MaybePollAsync` once after `ReloadModsAsync` on game load (fire-and-forget, off the UI hot path), then refresh rows if it changed anything.
- [ ] **Step 3:** App build (kill app first) → 0 errors. Commit `feat(nexus): debounced auto-check for mod updates + setting`.

### Task 9: App + XAML — the UPDATE chip

**Files:**
- Modify: `src/ModManager.App/ViewModels/ModRowViewModel.cs` (`UpdateAvailableVisibility`, `UpdateTooltip`)
- Modify: `src/ModManager.App/MainWindow.xaml` (chip in `StackPanel Grid.Column="2"`, ~line 445)

- [ ] **Step 1: Implement** `UpdateAvailableVisibility => Mod.UpdateAvailable ? Visible : Collapsed`; `UpdateTooltip => $"Nexus has {Mod.NexusLatestVersion} — you have {Mod.Version}"`. Extend `HasAnyCredit`/chip-area logic if needed so the chip participates.
- [ ] **Step 2:** Add an "UPDATE" chip (accent brush, matching existing chip style) bound `Visibility="{x:Bind UpdateAvailableVisibility}"` with `ToolTipService.ToolTip="{x:Bind UpdateTooltip}"`, OneTime, unqualified bindings (row VM is DataContext, matching siblings).
- [ ] **Step 3:** App build (kill app first) → 0 errors. Commit `feat(nexus): UPDATE chip on rows with a newer Nexus version`.

### Task 10: Smoke checklist + docs

**Files:**
- Modify: `docs/smoke-tests/pending.md`

- [ ] **Step 1:** Append a "Nexus by-mod-id poll" section: (a) Refresh Nexus stats fills endorsements/downloads on the existing library with no archive; (b) a mod with a newer Nexus version shows the UPDATE chip + correct tooltip; (c) auto-check flags a recently-updated mod within 24h of launch; (d) offline / rate-limited runs degrade silently (no crash, status reports it for the manual path); (e) `metadata.json` carries `nexusLatestVersion` and survives a rescan.
- [ ] **Step 2:** Commit `docs(smoke): Nexus by-mod-id poll checklist`.

---

## Self-review notes
- Spec coverage: client hardening (T1), updated.json (T2), data field + merge (T3), primitive (T4), sweep/candidate/period (T5), persistence/stamp (T6), manual action (T7), auto-check + setting (T8), chip (T9), smoke (T10). All spec sections mapped.
- Type consistency: `RefreshOneAsync`/`RefreshAllAsync`/`ResolveModId`/`SelectCandidates`/`PeriodFor`/`NexusRefreshResult`/`NexusUpdateEntry`/`NexusRateLimit`/`NexusRateLimitException`/`NexusPollStamp.ShouldPoll`/`NexusLatestVersion`/`UpdateAvailable` used consistently across tasks.
- Laws: Core holds all logic (T1-T6) behind tests; App is wiring (T7-T9). Additive nullable field only; atomic camelCase writes; silent fallback on API failure; no bundled binaries; personal key only.
