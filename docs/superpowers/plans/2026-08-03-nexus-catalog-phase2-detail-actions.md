# Nexus Catalog Phase 2 — detail view + endorse/track in-app — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Click a card in the Nexus storefront and understand the mod without leaving the launcher — description, art, requirements, uploader, stats — and **endorse or track it in-app**. Downloading still hands off to the browser.

**Architecture:** Two new optional plugin capabilities (`IModCatalogDetail`, `IModCatalogActions`) alongside Phase 1's `IModCatalogBrowse`. The plugin runs a single-mod GraphQL query and the endorse/track mutations through the existing host-authorized transport. The launcher adds a detail surface and a pure Core helper that turns Nexus's BBCode+HTML description into readable text.

**Tech Stack:** .NET 10, C#, WinUI 3, xUnit, Nexus GraphQL v2.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-02-nexus-catalog-great-design.md` (Phase 2 section). Phase 1 plan (`2026-08-02-nexus-catalog-phase1-storefront.md`) is the pattern to match.
- **ABI-safe, always.** `SourceSearchHit`'s 7-arg positional ctor is FROZEN — grow only with init-only properties. Never modify `IModSource`, `IModTextSearch`, `IModCatalog`, `IAuthorizedSend`, or `IModCatalogBrowse`. New capability = new interface. The shipped nexus-v0.13.0 plugin must still load on the 0.14.0 host.
- **Download stays a browser handoff.** No in-app file fetch. URL opening is `SafeUrl.IsHttpUrl`-gated + `Process.Start(UseShellExecute=true)`.
- **No adult content, no age gate.** Every catalog/browse query keeps `adultContent: { value: false, op: EQUALS }`. The detail query is reached only from an already-filtered card, so it does not re-filter — but it must never be wired to a search that bypasses the gate.
- **Read paths never throw** (empty/null on any failure). **Write paths (endorse/track) never throw either** — they return success/failure and the UI reverts optimistic state.
- **Mutations act on the user's real Nexus account.** Never fire one except in response to an explicit user click. Never call one from a test against the live API (tests use stub hosts only).
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<Nullable>enable</Nullable>` — zero warnings. No WinUI/WinRT in `src/ModManager.Core/`. No `#if FULL` (capability gate is the flavor gate). STORE build + `scripts/check-store-seal.ps1` stay green.
- **Never bare `dotnet build`/`dotnet test` at the repo root.** Launcher tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (STORE adds `-p:Configuration=Store`). Plugin tests: `dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj` in `C:\Users\estev\Projects\626-mod-plugins`.
- **After ANY XAML edit, clean before building:** `rm -rf src/ModManager.App/obj/x64/Debug src/ModManager.App/bin/x64/Debug`. Incremental WinUI codegen goes stale and the app then crashes at `MainWindow.Connect` with an `InvalidCastException`. A `MSB3021` "used by another process" error means a launcher is running — a file lock, not a compile error.
- Two repos; use `git -C <path>` explicitly. Commits: conventional + `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Explicit paths in `git add`, never `-A`.
- Versions: Abstractions → **0.14.0**; plugin csproj PackageReference → 0.14.0; plugin `release.yml` `minBinaryVersion` → 0.14.0 **and** its `--notes` text (they drifted last time). Dev: `dotnet pack src/ModManager.Plugins.Abstractions/ModManager.Plugins.Abstractions.csproj -p:Version=0.14.0 -o C:\Users\estev\Projects\626-mod-plugins\local-nuget`.

## Live-verified GraphQL facts (2026-08-03 — proven; do NOT re-derive or guess)

Verified against `https://api.nexusmods.com/v2/graphql`. **Introspection + read queries only — no mutation was executed.**

- **Detail query:** `mod(modId: ID!, gameId: ID!)`. **Both args are `ID!`, not `Int!`** — `Int!` fails with *"Type mismatch on variable $m and argument modId (Int! / ID!)"*. It takes a **numeric gameId**, NOT a domain slug.
- **Verified detail fields** (live, palworld modId 577): `modId uid name version author summary adultContent endorsements downloads updatedAt pictureUrl thumbnailLargeUrl modCategory { name } uploader { name memberId } viewerEndorsed viewerTracked viewerDownloaded description`.
- **⚠ TRAP — actions key off `uid`, not `modId`.** All four mutations take `modUid`:
  - `createModEndorsement(modUid: String!)` → `CreateModEndorsementMutationPayload { success: Boolean, endorsement: ModEndorsement }`
  - `abstainFromModEndorsement(modUid: String!)` → `AbstainFromModEndorsementMutationPayload { success: Boolean, endorsement }`
  - `trackMod(modUid: ID!)` → `TrackModMutationPayload { success: Boolean, trackedMod: TrackedMod }`
  - `untrackMod(modUid: ID!)` → `UntrackModMutationPayload { success: Boolean, trackedMod }`
  - **Note the inconsistency: endorse takes `String!`, track takes `ID!`.** Pin both in tests.
  - `uid` is a distinct large numeric string (e.g. modId `4733` → uid `"26040386720381"`). Browse and detail MUST return `uid` or no action can fire.
- **⚠ `description` is BBCode + HTML mixed**, e.g. `"[b]Features:[/b]\n<br />[list]\n<br />[*][b]In Game UI..."`. Rendering it raw shows markup. It must be converted (Task 3).
- **Requirements:** `modRequirements { nexusRequirements { totalCount nodes { modId modName notes url externalRequirement } } dlcRequirements { gameExpansion notes } }`.
  - `ModRequirement` is **flat** — there is no nested `modRequired` object.
  - `dlcRequirements` is a single `ModRequirementsDlc { gameExpansion, notes }`, **not** a paged list of nodes.
  - Requirements can be **external**: a live example returned `modId "0"`, `externalRequirement true`, and a GitHub `url`. The UI must handle "not a Nexus mod" (no mod page to link — use `url`).
- **`viewer*` populate under the OAuth bearer** — CONFIRMED on a signed-in rig during the v0.13.0 smoke (Phase 1's previously-unprovable claim). Unauthenticated they return null and degrade cleanly.

## File Structure

**Launcher (`626-mod-launcher`)**

| File | Responsibility |
|---|---|
| `src/ModManager.Plugins.Abstractions/Contract.cs` (modify) | `CatalogDetail`, `CatalogRequirement`, `IModCatalogDetail`, `IModCatalogActions`; add `Uid` + `GameId` init-props to `SourceSearchHit`. |
| `src/ModManager.Core/Nexus/ModDescriptionText.cs` (create) | **Pure** BBCode+HTML → plain text. Core so it is unit-testable. |
| `tests/ModManager.Tests/Nexus/ModDescriptionTextTests.cs` (create) | Converter tests. |
| `tests/ModManager.Tests/Plugins/ModCatalogDetailContractTests.cs` (create) | Contract + ABI assertions. |
| `src/ModManager.App/ViewModels/MainViewModel.cs` (modify) | `CatalogDetailAvailable`/`CatalogActionsAvailable` gates + `GetModDetailAsync` + `SetEndorsedAsync`/`SetTrackedAsync`. |
| `src/ModManager.App/NexusModDetailDialog.xaml(.cs)` (create) | The detail surface. |
| `src/ModManager.App/NexusCatalogView.xaml(.cs)` (modify) | Card click → open detail. |

**Plugin (`626-mod-plugins`)**

| File | Responsibility |
|---|---|
| `src/ModManager.Plugin.Nexus/CatalogQueryBuilder.cs` (modify) | Browse node selection gains `uid` + `gameId`; add the detail document. |
| `src/ModManager.Plugin.Nexus/NexusModSource.cs` (modify) | `IModCatalogDetail` + `IModCatalogActions`; detail mapper; mutation senders. |
| `tests/.../NexusCatalogDetailTests.cs` (create) | Query/mutation shape + mapping tests (stub host only). |

---

### Task 1: Abstractions 0.14.0 — detail + actions contract

**Files:** modify `src/ModManager.Plugins.Abstractions/Contract.cs`; create `tests/ModManager.Tests/Plugins/ModCatalogDetailContractTests.cs`.

**Interfaces produced** (consumed by every later task):

```csharp
/// <summary>One prerequisite for a mod. A requirement may be EXTERNAL (not a Nexus mod) — live data
/// returns modId "0" with externalRequirement true and an off-site Url — so ModId is nullable and the
/// UI must fall back to Url.</summary>
public sealed record CatalogRequirement(string Name, int? ModId, string? Url, string? Notes, bool External);

/// <summary>Full detail for one mod. Description is the RAW Nexus body (BBCode + HTML) — the launcher
/// converts it for display; the plugin does not guess at formatting.</summary>
public sealed record CatalogDetail(
    int ModId, string Uid, string Name, string? Author, string? Uploader, string? Version,
    string? Summary, string? DescriptionRaw, string? ImageUrl, string? Category,
    int? EndorsementCount, int? DownloadCount, System.DateTimeOffset? UpdatedAt, string? Url,
    bool? ViewerEndorsed, bool? ViewerTracked, bool? ViewerDownloaded,
    IReadOnlyList<CatalogRequirement> Requirements);

public interface IModCatalogDetail
{
    /// <param name="gameId">NUMERIC Nexus game id (the detail query takes an id, not a domain slug).</param>
    Task<CatalogDetail?> GetModDetailAsync(int gameId, int modId);
}

/// <summary>Endorse / track on the user's real account. Both key off the mod's UID (NOT its modId).
/// Only ever call in response to an explicit user action. Returns false on any failure; never throws.</summary>
public interface IModCatalogActions
{
    Task<bool> SetEndorsedAsync(string modUid, bool endorsed);
    Task<bool> SetTrackedAsync(string modUid, bool tracked);
}
```

Add to `SourceSearchHit` (init-only, ctor untouched): `public string? Uid { get; init; }` and `public int? GameId { get; init; }` — a card needs both to open detail and to act.

- [ ] **Step 1: Write the failing contract test.** Mirror `tests/ModManager.Tests/Plugins/ModCatalogBrowseContractTests.cs` (read it). Assert: the 7-arg `SourceSearchHit` ctor still exists; `Uid`/`GameId` exist with the right types and default to null on old-style construction; `IModCatalogDetail.GetModDetailAsync` is `(int,int) -> Task<CatalogDetail?>`; `IModCatalogActions` has both methods returning `Task<bool>`; `IModCatalogBrowse`/`IModCatalog`/`IModTextSearch` are unchanged.
- [ ] **Step 2: Run it, confirm it fails** (types don't exist). `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter ModCatalogDetailContractTests`
- [ ] **Step 3: Add the contract** exactly as above, matching the file's XML-doc voice (say WHY — especially the uid-vs-modId trap and why Description is raw).
- [ ] **Step 4: Run focused tests → PASS.**
- [ ] **Step 5: Run the FULL launcher suite** — the legacy-plugin-ABI test MUST stay green.
- [ ] **Step 6: Commit** `feat(catalog): detail + actions contract (Abstractions 0.14.0, ABI-safe)`.

---

### Task 2: Core — BBCode+HTML description → readable text

**Files:** create `src/ModManager.Core/Nexus/ModDescriptionText.cs` + `tests/ModManager.Tests/Nexus/ModDescriptionTextTests.cs`.

This is the messiest part of Phase 2 and the easiest to get wrong, so it lives in Core behind tests rather than in the UI.

**Produces:** `public static string ToPlainText(string? raw)`.

Rules: strip BBCode tags (`[b] [/b] [i] [u] [list] [*] [url=…] [img] [size] [color] [quote] [code] [center]` — case-insensitive, including parameterised forms); convert `<br />`/`<br>` and `[*]` to line breaks; decode the common HTML entities (`&amp; &lt; &gt; &quot; &#39; &nbsp;`); strip any remaining `<...>` tags; collapse 3+ blank lines to one; trim. Never throw — null/empty in, empty out.

- [ ] **Step 1: Write failing tests**, including this real sample (live-captured, palworld modId 577):
  `"[b]Features:[/b]\n<br />[list]\n<br />[*][b]In Game UI for configuring mod settings[/b]\n<br />[/list]"`
  → expect no `[`/`]` markers, no `<br`, "Features:" and the bullet text present on separate lines.
  Cover: null → `""`; plain text unchanged; `[url=https://x]text[/url]` → `text`; entities decoded; nested/unclosed tags don't throw; a 10k-char body completes.
- [ ] **Step 2: Run, confirm failure.** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter ModDescriptionText`
- [ ] **Step 3: Implement** with `System.Text.RegularExpressions` (compiled, non-backtracking where possible). No WinUI/WinRT — Core purity is enforced by `CorePurityTests`.
- [ ] **Step 4: Tests pass; run the full launcher suite.**
- [ ] **Step 5: Commit** `feat(nexus): BBCode+HTML description to readable text`.

---

### Task 3: Plugin — detail query + mapper

**Files:** modify `CatalogQueryBuilder.cs` (browse selection gains `uid gameId`; add `BuildDetail`), `NexusModSource.cs` (implement `IModCatalogDetail`); create `tests/.../NexusCatalogDetailTests.cs`.

Pack Abstractions 0.14.0 to `local-nuget` first (see Global Constraints) and bump both csprojs.

The detail document (types verified live — `ID!` for BOTH args):

```graphql
query Detail($modId: ID!, $gameId: ID!) {
  mod(modId: $modId, gameId: $gameId) {
    modId uid name version author summary description adultContent
    endorsements downloads updatedAt pictureUrl thumbnailLargeUrl
    modCategory { name } uploader { name }
    viewerEndorsed viewerTracked viewerDownloaded
    modRequirements {
      nexusRequirements { totalCount nodes { modId modName notes url externalRequirement } }
      dlcRequirements { notes }
    }
  }
}
```

- [ ] **Step 1: Failing tests** — `BuildDetail` emits `$modId: ID!` and `$gameId: ID!` (NOT `Int!` — pin this; `Int!` is rejected live), requests `uid` and the requirement fields; the browse document now also requests `uid` and `gameId`; the mapper turns a canned response into `CatalogDetail` (including an EXTERNAL requirement with `modId "0"` → `ModId` null, `External` true, `Url` kept) and returns null for `{"data":{"mod":null}}` without throwing.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.** Route through the existing `SendAsync`/`ParseAsync`. Use the tolerant readers (`Str`/`Int`/`BoolN`/`Date`). Never throw → null on any failure. Do NOT modify `SearchAsync`, `SearchCatalogAsync`, or `MapSearchNodes`.
- [ ] **Step 4: Tests pass; full plugin suite green.**
- [ ] **Step 5: Commit** `feat(nexus): single-mod detail query + mapper`.

---

### Task 4: Plugin — endorse/track mutations

**Files:** modify `NexusModSource.cs` (`IModCatalogActions`), `.github/workflows/release.yml` (`minBinaryVersion` → 0.14.0 **and** the `--notes` text); extend the detail tests.

```graphql
mutation Endorse($uid: String!) { createModEndorsement(modUid: $uid) { success } }
mutation Abstain($uid: String!) { abstainFromModEndorsement(modUid: $uid) { success } }
mutation Track($uid: ID!)       { trackMod(modUid: $uid) { success } }
mutation Untrack($uid: ID!)     { untrackMod(modUid: $uid) { success } }
```

**Pin the type split in tests: endorse/abstain use `String!`, track/untrack use `ID!`.** Getting it backwards fails the call.

- [ ] **Step 1: Failing tests** (stub host ONLY — never hit the live API with a mutation): each of the four paths posts the right document with the right variable type and the uid as a variable; `success: true` → `true`; `success: false`, a GraphQL `errors` body, a non-2xx, and a thrown transport all → `false` with no exception escaping.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement** through the authorized transport (the bearer is what makes these act as the signed-in user). Never throw.
- [ ] **Step 4: Full plugin suite green.**
- [ ] **Step 5: Commit** `feat(nexus): endorse + track mutations (IModCatalogActions)`.

---

### Task 5: Launcher VM — detail + actions plumbing

**Files:** modify `src/ModManager.App/ViewModels/MainViewModel.cs`.

Add `CatalogDetailAvailable` / `CatalogActionsAvailable` (`NexusActionsAvailable && NexusSource is IModCatalogDetail` / `IModCatalogActions`), plus:
`Task<CatalogDetail?> GetModDetailAsync(int gameId, int modId)` (self-timeout ~10s, never throws → null) and
`Task<bool> SetEndorsedAsync(string uid, bool endorsed)` / `Task<bool> SetTrackedAsync(string uid, bool tracked)` (never throw → false).

- [ ] **Step 1: Implement**, mirroring the existing `BrowseCatalogAsync` timeout/never-throw shape directly above.
- [ ] **Step 2: Raise the new gates everywhere `CatalogBrowseVisibility` is raised.** There are **four** sites — grep and confirm the counts match. (A missed raise is this feature's recurring bug: it shipped once in v0.12.0 and was nearly repeated in Phase 1.)
- [ ] **Step 3: Build FULL** (0 errors) and run the full launcher suite.
- [ ] **Step 4: Commit** `feat(catalog): VM detail + endorse/track plumbing`.

---

### Task 6: Launcher UI — the detail surface

**Files:** create `src/ModManager.App/NexusModDetailDialog.xaml(.cs)`; modify `NexusCatalogView.xaml(.cs)`; append to `docs/smoke-tests/pending.md`.

Read `NexusCatalogView.xaml(.cs)` and `LooseIdentifyDialog.xaml(.cs)` first and match their patterns and theme resources.

- Clicking a card opens the detail (a `ContentDialog` is fine here — it is a single-item read, unlike the grid).
- Shows: large image (`ImageUrl`, async, decode-capped, placeholder on null/failure — reuse Phase 1's approach), name, author/uploader, version, category, ♥ endorsements + ⬇ downloads, updated date, the converted description (`ModDescriptionText.ToPlainText`, scrollable), and a **Requirements** list (each row: name + notes; click opens the Nexus mod page for internal ones or `Url` for external — `SafeUrl`-gated).
- **Endorse** and **Track** toggle buttons, visible only when `CatalogActionsAvailable`. Optimistic UI: flip immediately, call the VM, **revert on false** and surface a short inline message. Reflect initial state from `ViewerEndorsed`/`ViewerTracked` (null = unknown → show the un-acted state, never a wrong "already endorsed").
- **Download / View on Nexus** button = browser handoff (unchanged law).
- Loading and failure states; a failed detail fetch shows a message and still offers the browser link. Never throws.
- Gate the card-click on `CatalogDetailAvailable` so a 0.13.x plugin (no detail capability) simply doesn't open a dead dialog.

- [ ] **Step 1: Build the dialog + wire the card click.**
- [ ] **Step 2: Clean + build FULL and STORE, run the seal.** (`rm -rf obj/x64/Debug bin/x64/Debug` first — XAML changed.)
- [ ] **Step 3: Run the full launcher suite.**
- [ ] **Step 4: Add smoke entries** — detail opens with real content and readable (not raw-markup) description; requirements list, including an external requirement; endorse toggles and **persists on the Nexus website**; track toggles and persists; a failed action reverts the button; download still opens the browser; older-plugin fallback (no detail on click, storefront unaffected).
- [ ] **Step 5: Commit** `feat(catalog): in-app mod detail with endorse + track`.

---

### Task 7: Whole-branch verification

- [ ] Launcher suite 0 failed; plugin suite 0 failed.
- [ ] FULL 0 errors; STORE 0 errors; `pwsh -File scripts/check-store-seal.ps1` → **STORE seal OK**.
- [ ] Law audit: `adultContent` gate intact on browse/catalog paths; no in-app download; `SearchAsync` byte-for-byte unchanged (`git -C <plugin> diff main -- .../NexusModSource.cs` shows no deletions inside it); no `#if FULL`; no new on-disk JSON shape; no WinUI/WinRT in Core.
- [ ] ABI: shipped nexus-v0.13.0 still loads on the 0.14.0 host.
- [ ] Confirm **no test executes a live mutation** (grep the plugin tests for `api.nexusmods.com` — stub hosts only).

## Release choreography (human-gated)

Contract change → **launcher first**: merge → tag `v0.14.0` (publishes Abstractions 0.14.0) → **verify the package is genuinely RESTORABLE before tagging the plugin** (`curl -s -o /dev/null -w "%{http_code}" https://api.nuget.org/v3-flatcontainer/modmanager.plugins.abstractions/0.14.0/modmanager.plugins.abstractions.0.14.0.nupkg` must be **200**; the flatcontainer *index* listing it is NOT sufficient — that mistake failed the nexus-v0.13.0 run) → merge plugin → tag `nexus-v0.14.0` → publish the launcher draft → refresh plugin → **fully restart** → smoke.
