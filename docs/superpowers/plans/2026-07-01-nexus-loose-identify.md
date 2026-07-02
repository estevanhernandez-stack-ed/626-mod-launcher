# Nexus loose-identify Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Fresh implementer per task + two-stage review + fix gate; whole-branch review at the end. Steps use `- [ ]`.

**Goal:** "Identify loose mods on Nexus…" — a review-first batch action that proposes Nexus matches for loose-root rows by name-search and attaches only user-approved matches (`sourceConfidence: "nameSearch"` + `NexusModId`), lighting up endorsements/update-checks via existing machinery.

**Architecture:** A new optional plugin capability (`IModTextSearch`, separate interface — no ABI break), a pure-Core proposal pass (`LooseIdentify`, TDD), a batch-approve dialog (App), and a companion Nexus-plugin implementation in the separate `626-mod-plugins` repo (GraphQL v2), sequenced launcher-first.

**Tech Stack:** .NET 10 / C# (Abstractions: BCL-pure DTOs; Core: pure + xUnit; App: WinUI 3). Plugin repo: its own conventions.

## Global Constraints

- **No plugin ABI break** — the new capability is a SEPARATE interface implemented alongside `IModSource`; `IModSource` itself is untouched. Old plugins must keep loading.
- **Abstractions stays BCL-pure** (no Core/WinUI refs — match Contract.cs's existing style).
- **Review-first is the only write path** — nothing attaches without an explicit user approval in the dialog. `IsManual` rows are never candidates; `MergeMeta` never-clobber stands.
- **Nexus gating pattern** — the action follows `NexusActionsVisibility` + a `NexusSource is IModTextSearch` capability check. NO new `#if FULL` (the plugin gate IS the flavor gate). STORE must still build + seal.
- **camelCase on disk** — metadata entries reuse the existing `ModMeta` shape (no new persisted shape).
- **Never bare `dotnet` at repo root.** Core: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (+ `-p:Configuration=Store`); kill `ModManager.App` first. Seal: `pwsh scripts/check-store-seal.ps1`.
- **Branch `feat/loose-root-mods`** (stacked past #169's commits) — commit onto it, no switching. Conventional commits + trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- **Ground-truth caveat:** exploration line refs (`Scanner.cs:1201` name-search, `NameMatch.cs:43/74`, `MainViewModel.cs:1471` FetchMetadata, `:1512` NexusSource, `Metadata.cs:42` MergeMetadata, `Scanner.WriteManyMeta` ~:1421, ManualMatchDialog) — read before editing; match real shapes.

---

## Task 1: `IModTextSearch` + `SourceSearchHit` (Abstractions)

**Files:**
- Modify: `src/ModManager.Plugins.Abstractions/Contract.cs` (append — match the file's DTO/record style exactly)
- Test: `tests/ModManager.Tests/Plugins/ModTextSearchContractTests.cs` (a light shape test — the contract compiles, records behave)

**Interfaces (produces):**

```csharp
/// <summary>Optional text-search capability. A source that can search its catalog by name for a game
/// domain implements this ALONGSIDE IModSource; the host feature-detects with a type check, so
/// plugins built before this interface keep loading and working unchanged.</summary>
public interface IModTextSearch
{
    Task<IReadOnlyList<SourceSearchHit>> SearchAsync(string gameDomain, string query);
}

/// <summary>One text-search hit — enough for a review dialog row + a follow-up FetchMetadataAsync.</summary>
public sealed record SourceSearchHit(
    string GameDomain, int ModId, string Name, string? Author,
    string? Summary, int? EndorsementCount, string? Url);
```

- [ ] **Step 1:** Failing shape test (record equality + interface assignable via a tiny fake). Run `--filter ModTextSearch` → FAIL.
- [ ] **Step 2:** Append the contract to Contract.cs. Test → PASS. Full Core suite green (Abstractions is referenced by the test project — verify; if not, reference it the way other plugin-contract tests do, or place the test where existing Abstractions tests live — search first).
- [ ] **Step 3:** Both flavor builds + seal (Abstractions ships in both — the seal must not regress).
- [ ] **Step 4: Commit** — `feat(plugins): optional IModTextSearch capability (no ABI break)`.

## Task 2: `LooseIdentify` proposal pass (Core, TDD)

**Files:**
- Create: `src/ModManager.Core/LooseMods/LooseIdentify.cs`
- Test: `tests/ModManager.Tests/LooseMods/LooseIdentifyTests.cs`

**Interfaces:**
- Consumes: `Mod` rows (`Location == "loose-root"`, `Class` carries kind, `Base` the stem), the game's `ModMeta` map (for `IsManual`/`NexusModId`/confidence), `NameMatch.CleanModName` + `NameMatch.PickBestMatch`, `SourceSearchHit` (Task 1). Search injected as `Func<string, Task<IReadOnlyList<SourceSearchHit>>>` per query (Core stays plugin-agnostic).
- Produces:

```csharp
public sealed record LooseIdentifyProposal(string ModKey, string CleanQuery, SourceSearchHit? Match);

public static class LooseIdentify
{
    // Candidates: loose-root rows, excluding loaders (Class=="loader"), rows whose meta IsManual,
    // and rows already identified (meta.NexusModId != null or non-null SourceConfidence).
    public static IReadOnlyList<Mod> Candidates(IReadOnlyList<Mod> rows, IReadOnlyDictionary<string, ModMeta> meta);

    // One proposal per candidate: CleanModName(Base) -> search(query) -> PickBestMatch (threshold 0.5,
    // matching on hit.Name). A throwing/empty search yields Match=null. Never throws.
    public static Task<IReadOnlyList<LooseIdentifyProposal>> ProposeAsync(
        IReadOnlyList<Mod> candidates, Func<string, Task<IReadOnlyList<SourceSearchHit>>> search);

    // Map an APPROVED hit to the ModMeta to persist (merge-in fields only; never sets IsManual):
    // Title=hit.Name, Author, Url=hit.Url, NexusModId=hit.ModId, EndorsementCount,
    // SourceConfidence="nameSearch".
    public static ModMeta ToMeta(SourceSearchHit hit);
}
```

- [ ] **Step 1:** Failing tests — fixture = the real DS2 loose rows (`Zipliner_v1.1`, `DollmanMute`, `DeathStranding2Fix` plugins; `ShaderToggler`, `DeathStranding2UI`, `renodx-deathstranding2` shaders; `dxgi`/`version` loaders): (1) Candidates excludes both loaders always; (2) excludes a row whose meta IsManual; (3) excludes a row with NexusModId set; (4) CleanQuery for `Zipliner_v1.1` == `Zipliner` (via the real CleanModName); (5) ProposeAsync picks the best hit ≥ threshold and yields Match=null below it; (6) a throwing search delegate yields Match=null for that row, others proceed; (7) ToMeta maps fields + `"nameSearch"` confidence and leaves IsManual false. Run `--filter LooseIdentify` → FAIL.
- [ ] **Step 2:** Implement; PASS; full Core suite green.
- [ ] **Step 3: Commit** — `feat(loose-mods): LooseIdentify proposal pass (Core)`.

## Task 3: Menu action + batch-approve dialog + apply (App)

**Files:**
- Create: `src/ModManager.App/LooseIdentifyDialog.xaml` (+ `.xaml.cs`)
- Modify: `src/ModManager.App/MainWindow.xaml` (More-menu item beside the existing Nexus items, `NexusActionsVisibility`-gated), `src/ModManager.App/MainWindow.xaml.cs` (handler), `src/ModManager.App/ViewModels/MainViewModel.cs` (the identify command: gather rows → Candidates → ProposeAsync via the plugin → dialog → WriteManyMeta approved → reload), `docs/smoke-tests/pending.md`.

**Interfaces:**
- Consumes: Task 2's `LooseIdentify`; `NexusSource` (`MainViewModel.cs` ~:1512) with a `is IModTextSearch` capability check; the active game's `NexusGameDomain`; `Scanner.LoadMetadata`/`WriteManyMeta`; `ReloadModsAsync`; dialog conventions (ContentDialog + XamlRoot, mirror ManualMatchDialog/UpdateModsDialog style).
- Produces: menu item **"Identify loose mods on Nexus…"** visible only when: Nexus actions available AND `NexusSource is IModTextSearch` AND the active game has loose-root rows. Flow: no `NexusGameDomain` → a clear message dialog, no search. Else propose → dialog lists each proposal (stem → title · author · summary · endorsements; checkbox, checked by default when matched; greyed uncheckable "no confident match" rows) → **Apply** writes checked rows via `WriteManyMeta` (key = ModKey, value = `LooseIdentify.ToMeta(hit)` merged over existing entry via the existing merge helper so unrelated fields survive) → `ReloadModsAsync`; **Cancel** writes nothing. Status line reports "Identified N of M loose mods."

- [ ] **Step 1:** Read the anchors; build the dialog + command following existing dialog/command conventions. No `#if FULL`.
- [ ] **Step 2: Gate** — kill app; FULL + STORE builds 0 errors; seal OK; full Core suite green.
- [ ] **Step 3: Smoke entry** — on live DS2 (Nexus connected, updated plugin installed): action visible; run → proposals for the plugin/shader stems, none for dxgi/version; approve a subset → rows gain title/author/hearts and update-checks work; corrected manual match survives a re-run; without the updated plugin the action is absent; Store build unaffected.
- [ ] **Step 4: Commit** — `feat(loose-mods): review-first Nexus identify for loose rows (App)`.

## Task 4: Nexus plugin implementation (SEPARATE REPO — `c:/Users/estev/Projects/626-mod-plugins`)

> Sequenced after Tasks 1–3 commit (the plugin builds against the new Abstractions). This task works in the OTHER repo; scout first.

- [ ] **Step 1 (scout):** Map the repo: where the Nexus source class implements `IModSource`, how it references Abstractions (project ref to the launcher checkout? a copied contract?), test conventions, and how a release is cut (README + tools/). Report findings before editing.
- [ ] **Step 2 (live verification — the GOG-epoch pattern):** Verify the GraphQL v2 search against the REAL endpoint (`https://api.nexusmods.com/v2/graphql`) with a real query for the `deathstranding2` domain (e.g. search "Zipliner"): confirm the query shape (`mods` filter by name + gameDomainName), auth requirements (works with apikey header / unauthenticated?), and the response fields (modId, name, author name, summary, endorsements, page url). Record the verified query verbatim in the task report.
- [ ] **Step 3:** Implement `IModTextSearch` on the Nexus source using the verified query (transport: the existing `IPluginHostServices.HttpClient` + credential + AppVersion header pattern already used by the other endpoints). Empty/failed search → empty list, never throws. Tests per the repo's conventions (mock transport; the verified response shape as fixture).
- [ ] **Step 4:** Build + tests green in that repo. Commit on a feature branch there + open a PR. Note in the PR: release must set `minBinaryVersion` to the launcher version carrying `IModTextSearch`; the release itself is human-gated (Este cuts it after the launcher merge ships).

## Ride-along (controller does this directly, not a subagent): restore DS2 `nexusGameDomain`

- [ ] With the app CLOSED: edit `%APPDATA%/ModManagerBuilder/games.json` — DS2 entry gains `"nexusGameDomain": "deathstranding2"`. Verify by re-reading. (Feed re-curation post-#169 fixes fresh adds.)

## Self-review

- **Spec coverage:** contract (T1) · pure proposal pass with all exclusions + injected search (T2) · review-first dialog as the only write path + gating + payoff cascade via WriteManyMeta/NexusModId (T3) · plugin impl with live verification + release coupling (T4) · ride-along domain restore · honest degradation at every step (no-domain message, old-plugin hidden action, empty-search = no-match).
- **No placeholders:** T1/T2 carry complete contract + API shapes and concrete test lists; T3/T4 are integration/scout tasks with exact anchors and explicit deliverables (the established style).
- **Type consistency:** `SourceSearchHit` (T1) flows through `LooseIdentify` (T2) into the dialog + `ToMeta` (T3) and is produced by the plugin (T4). `LooseIdentifyProposal.ModKey` = the row `Base` = the metadata key `MergeMetadata` resolves.
- **Law check:** no ABI break (separate interface), no secrets (existing credential flow), no new persisted shape, review-first only write path, both flavors + seal in every App gate.
