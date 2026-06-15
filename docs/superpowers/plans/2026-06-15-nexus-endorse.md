# Nexus endorse — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** One-click endorse ⇄ abstain on each Nexus-identified row, with hearts kept accurate library-wide via the bulk endorsements list. See `docs/superpowers/specs/2026-06-15-nexus-endorse-design.md`.

**Architecture:** Add POST-body support to the (currently GET-only) Nexus client, an `EndorseAsync` write that surfaces refusals gracefully, a bulk `GetUserEndorsementsAsync`, a persisted `ModMeta.Endorsed` (three-places), refresh-sweep integration that applies the bulk list, and a heart affordance + VM toggle.

**Tech Stack:** .NET 10, C#, xUnit. Pure-Core + thin App (WinUI 3). camelCase JSON on disk via `AtomicJson`.

**Conventions:** Core behavior starts with a failing xUnit test. `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (Core); `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (App — kill any running `ModManager.App` first; it locks `ModManager.Core.dll`). Never bare `dotnet` at repo root. Conventional commits ending in `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

### Task 1: POST-body support in NexusClient.SendAsync

**Files:**
- Modify: `src/ModManager.Core/NexusClient.cs` (`SendAsync`, ~line 72-86)
- Test: `tests/ModManager.Tests/Nexus/NexusPostBodyTests.cs`

- [ ] **Step 1: Failing test.** With a fake `HttpMessageHandler`, a request built with `Method="POST"` + a non-null `Body` results in the outgoing `HttpRequestMessage` carrying that body as `application/json` content; a GET request (null body) sends no content. (Assert by capturing the handler's received request.)
- [ ] **Step 2: Implement.** Mirror `CurseForgeClient.SendAsync` (`CurseForgeClient.cs:39-57`): when `req.Body is not null`, set `msg.Content = new StringContent(req.Body, System.Text.Encoding.UTF8, "application/json")`; when copying `req.Headers`, skip any `Content-Type` (let `StringContent` set it). Leave the existing `CaptureRateLimit`/429 path intact.
- [ ] **Step 3:** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` green. Commit `feat(nexus): POST-body support in the client (first write path)`.

### Task 2: EndorseRequest + EndorseAsync + graceful refusals

**Files:**
- Modify: `src/ModManager.Core/NexusRequests.cs` (new `EndorseRequest` builder)
- Modify: `src/ModManager.Core/NexusClient.cs` + `INexusClient` (`EndorseAsync`)
- Create: `src/ModManager.Core/NexusEndorse.cs` (`enum EndorseAction { Endorse, Abstain }`, `record EndorseOutcome(string? Status, string? Message, bool Refused)`)
- Test: `tests/ModManager.Tests/Nexus/NexusEndorseTests.cs`

- [ ] **Step 1: Failing tests.** (a) `EndorseRequest("eldenring", 42, "1.0", EndorseAction.Endorse, opts)` builds `POST {base}/v1/games/eldenring/mods/42/endorse.json`, body `{"Version":"1.0"}`, with auth + ToS headers; `EndorseAction.Abstain` → `.../abstain.json`. (b) `EndorseAsync` on a 200 with `{"message":"Endorsed","status":"Endorsed"}` → `EndorseOutcome(Status:"Endorsed", Refused:false)`. (c) A 4xx body `{"message":"NOT_DOWNLOADED_MOD"}` → `Refused:true` with a friendly `Message` ("You need to download this mod before you can endorse it."); `TOO_SOON_AFTER_DOWNLOAD` → friendly wait-window text; an unknown code → the raw `message` surfaced, still `Refused:true`, no throw. (d) a 429 → `NexusRateLimitException` propagates.
- [ ] **Step 2: Implement** the builder (POST + JSON body via `JsonSerializer.Serialize(new Dictionary<string,string>{["Version"]=version})`), `INexusClient.EndorseAsync(string domain, int modId, string version, EndorseAction action)`, and the outcome parsing (read `message`/`status`; on non-2xx that isn't 429, parse the body message → `Refused` outcome with mapped/raw text). Keep a small `FriendlyRefusal(code)` map in Core (the two known codes; default = pass through).
- [ ] **Step 3:** Core tests green. Commit `feat(nexus): endorse/abstain write with graceful precondition refusals`.

### Task 3: ModMeta.Endorsed + three-places carry-through

**Files:**
- Modify: `src/ModManager.Core/Mod.cs` (`ModMeta.Endorsed` bool?; in-memory `Mod.Endorsed` bool?)
- Modify: `src/ModManager.Core/Scanner.cs` (`MergeMeta` carry-through)
- Modify: `src/ModManager.Core/Metadata.cs` (`MergeMetadata` copy)
- Test: extend `tests/ModManager.Tests/MetadataMergeTests.cs` + camelCase round-trip

- [ ] **Step 1: Failing tests.** (a) round-trip: a `ModMeta { Endorsed = true }` serializes with key `"endorsed"` (not Pascal) and round-trips. (b) `MergeMeta` carries `Endorsed` (consistent with sibling fields incl. the `IsManual` short-circuit). (c) `MergeMetadata` copies `Endorsed` onto `Mod`.
- [ ] **Step 2: Implement** the field on `ModMeta` (persisted user intent, like `IsManual`), the in-memory `Mod.Endorsed`, the `MergeMeta` line, the `MergeMetadata` copy.
- [ ] **Step 3:** Core tests green. Commit `feat(nexus): persist Endorsed state (three-places)`.

### Task 4: Bulk user-endorsements list + apply

**Files:**
- Modify: `src/ModManager.Core/NexusRequests.cs` (`UserEndorsementsRequest` + `MapUserEndorsements`)
- Modify: `src/ModManager.Core/NexusClient.cs` + `INexusClient` (`GetUserEndorsementsAsync`)
- Create: `src/ModManager.Core/NexusEndorsement.cs` (`record NexusEndorsement(int ModId, string DomainName, string Status)`)
- Modify: `src/ModManager.Core/NexusRefresh.cs` (`ApplyEndorsements`)
- Test: `tests/ModManager.Tests/Nexus/NexusUserEndorsementsTests.cs`

- [ ] **Step 1: Failing tests.** (a) `UserEndorsementsRequest(opts)` → `GET {base}/v1/user/endorsements.json` with auth + ToS headers. (b) `MapUserEndorsements` maps `[{"mod_id":42,"domain_name":"eldenring","status":"Endorsed"}]` → one `NexusEndorsement(42,"eldenring","Endorsed")`; empty/malformed → empty list. (c) `ApplyEndorsements(metas, endorsements, "eldenring")` sets `Endorsed=true` for metas whose resolved id matches an `"Endorsed"` entry in that domain, `false` for matches with another status, and leaves non-matches untouched.
- [ ] **Step 2: Implement** the builder, mapper, `GetUserEndorsementsAsync` (via the rate-limit-aware `SendAsync`), and the pure `ApplyEndorsements` (reuse `NexusRefresh.ResolveModId`).
- [ ] **Step 3:** Core tests green. Commit `feat(nexus): bulk user-endorsements list + apply to library`.

### Task 5: Refresh-sweep integration

**Files:**
- Modify: `src/ModManager.Core/NexusRefresh.cs` (`RefreshAllAsync` also fetches + applies endorsements)
- Test: extend `tests/ModManager.Tests/Nexus/NexusRefreshSweepTests.cs`

- [ ] **Step 1: Failing test.** `RefreshAllAsync` makes one `GetUserEndorsementsAsync` call and applies it so returned metas carry correct `Endorsed`; a failure of that single call does not abort the stats sweep (endorsements are best-effort — swallow and continue). Count in `NexusRefreshResult` unchanged otherwise.
- [ ] **Step 2: Implement.** Fetch the list once at the start (or end) of the sweep, `ApplyEndorsements`, fold into the returned metas. Guard the endorsements call in its own try/catch so it can't sink the stats refresh.
- [ ] **Step 3:** Core tests green. Commit `feat(nexus): apply endorsement state during the refresh sweep`.

### Task 6: App — VM toggle action

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (`ToggleEndorseAsync`)

- [ ] **Step 1: Implement** `ToggleEndorseAsync(ModRowViewModel row)`: guard `_ctx`, `_nexus.IsConnected`, `NexusDomains.Effective(_ctx.Game)`, `row.Mod.NexusModId`. Choose `EndorseAction` from `row.Mod.Endorsed == true ? Abstain : Endorse`. `version = row.Mod.Version ?? row.Mod.NexusLatestVersion ?? ""`. Call `EndorseAsync`; on `!Refused` set `row.Mod.Endorsed = (action==Endorse)`, `WriteOneMeta`, notify the row, status `Endorsed "{name}" on Nexus.` / `Retracted endorsement for "{name}".`; on `Refused` status = the outcome `Message`; catch `NexusRateLimitException` → "Nexus rate limit reached — try again later."; wrap in `IsBusy`/try-catch like `BackfillNexusAsync`.
- [ ] **Step 2:** App build (kill running app first) → 0 errors. Commit `feat(nexus): ToggleEndorse view-model action`.

### Task 7: App + XAML — the heart affordance

**Files:**
- Modify: `src/ModManager.App/ViewModels/ModRowViewModel.cs` (`EndorseVisibility`, `EndorseGlyph`, `EndorseTooltip`, `IsEndorsed`)
- Modify: `src/ModManager.App/MainWindow.xaml` (heart button in the row) + `MainWindow.xaml.cs` (`OnEndorse`)

- [ ] **Step 1: Implement** VM members: `EndorseVisibility => Mod.NexusModId is not null && <NexusConnected> ? Visible : Collapsed` (thread the connected flag in via the row's existing access to the parent/VM, mirroring how other row members read shared state); `IsEndorsed => Mod.Endorsed == true`; `EndorseGlyph => IsEndorsed ? "" : ""` (filled vs outline heart, Segoe MDL2); `EndorseTooltip => IsEndorsed ? "Endorsed — click to retract" : "Endorse on Nexus"`.
- [ ] **Step 2:** Add a `Button` (transparent, `BorderThickness=0`) with a `FontIcon` bound to `EndorseGlyph`, `Visibility="{x:Bind EndorseVisibility}"`, `ToolTipService.ToolTip="{x:Bind EndorseTooltip, Mode=OneWay}"`, `Click="OnEndorse"`, in the row's chip/icon-button area. `OnEndorse`: extract `row` from `sender` DataContext (mirror `OnManualMatch`/`OnSetMpCompat`), `await ViewModel.ToggleEndorseAsync(row)`. Use `Mode=OneWay` on the glyph/tooltip so the heart flips after a toggle (these are not `OneTime` like the credit bindings).
- [ ] **Step 3:** App build (kill app first) → 0 errors. Commit `feat(nexus): heart endorse button on Nexus-identified rows`.

### Task 8: Smoke checklist + docs

**Files:**
- Modify: `docs/smoke-tests/pending.md`

- [ ] **Step 1:** Append a "Nexus endorse" section: (a) the heart shows only on Nexus-identified rows when connected; (b) clicking endorses (heart fills) and the status line confirms; (c) clicking again abstains (heart empties); (d) a not-yet-downloaded or too-soon mod shows the friendly refusal in the status line, no crash, heart does NOT flip; (e) Refresh Nexus stats / auto-check fills hearts for mods endorsed outside the launcher; (f) `metadata.json` carries `endorsed` and it survives a rescan.
- [ ] **Step 2:** Commit `docs(smoke): Nexus endorse checklist`.

---

## Self-review notes
- Spec coverage: POST plumbing (T1), endorse write + refusals (T2), Endorsed field (T3), bulk list + apply (T4), sweep integration (T5), VM toggle (T6), heart UI (T7), smoke (T8). All spec sections mapped.
- Type consistency: `EndorseAction`/`EndorseOutcome`/`EndorseAsync`/`NexusEndorsement`/`GetUserEndorsementsAsync`/`ApplyEndorsements`/`ToggleEndorseAsync`/`ModMeta.Endorsed`/`Mod.Endorsed`/`EndorseGlyph` used consistently across tasks.
- Laws: Core holds all logic (T1-T5) behind tests; App is wiring (T6-T7). Additive nullable field; atomic camelCase write; never auto-endorse; refusals/offline/429 degrade to a status line; no embedded key. Endorse is reversible by abstain.
