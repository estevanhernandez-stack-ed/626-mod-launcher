# The great Nexus catalog — phased design

**Date:** 2026-08-02
**Status:** Spec (approved-to-spec in-conversation; awaiting written review before build). Supersedes the lean "v3 thumbnails + views" spec. Builds on the shipped catalog ([v1](2026-08-02-nexus-catalog-design.md), [v2 default listing](2026-08-02-nexus-catalog-default-listing-design.md)). GitHub/FULL-only. A multi-cut feature line, each phase independently shippable.

## The thesis

The catalog works and opens populated, but it's still "Nexus in a window." The unlock: **we're logged in.** Nexus's GraphQL exposes a whole tier of per-user state a logged-out browser tab never gets. That lets us build a catalog that *knows you* — which is the difference between good and great here. Download stays a browser handoff (operating law + honors-the-builders), but discovery, awareness, and endorsement come in-app.

> Good is the enemy of great when we settle for it. This spec aims at great and phases the work so we never ship an unreviewable lump.

## Live-verified depth (2026-08-02, api.nexusmods.com/v2/graphql — nothing below is assumed)

- **Per-user (viewer) fields on `Mod`** (require the OAuth bearer, which the host already attaches via `IAuthorizedSend`): `viewerDownloaded`, `viewerEndorsed`, `viewerUpdateAvailable`, `viewerTracked`, `viewerBlocked`.
- **Card data:** `thumbnailUrl` / `thumbnailLargeUrl` / `pictureUrl` (real CDN URLs confirmed), `author`, `uploader (User)`, `endorsements`, `downloads`, `version`, `fileSize`, `updatedAt`, `createdAt`, `modCategory (ModCategory)`, `tags`, `summary`, `description`.
- **Filtering (`ModsFilter`):** `categoryName`, `tag`, `hasUpdated`, `name`, `adultContent`, plus downloads/endorsements/fileSize/updatedAt ranges.
- **Sort (`ModsSort`):** `endorsements`, `downloads`, `updatedAt`, `createdAt` (+ relevance/name/size). **No trending** — dropped.
- **Paging:** `mods(offset, count)` + `totalCount`.
- **Detail (`ModRequirements`):** `nexusRequirements`, `dlcRequirements`, `modsRequiringThisMod`.
- **Mutations (in-app actions):** `createModEndorsement` / `abstainFromModEndorsement` (endorse/un-endorse), `trackMod` / `untrackMod` (track). Endorse is already live-tested via the existing authorized path.

## Operating laws (unchanged, load-bearing)

- **Download stays a browser handoff.** No in-app file fetch — Premium-gated + AUP + honors-the-builders (land on the author's page). "Get"/"Download" opens the mod page.
- **No adult content, ever, no age-gating.** `adultContent: { value: false, op: EQUALS }` on every query. Intake remains content-agnostic (unchanged).
- **ABI intact.** Every shipped plugin keeps loading on every later host: grow `SourceSearchHit` only via init-only properties (never new ctor params); add capabilities only as new optional interfaces (never modify existing ones).
- **Authorized reads.** `viewer*` fields only populate when the query is sent with the bearer — route the browse/detail queries through the host's authorized transport. When disconnected, they come back null → badges simply don't show (graceful).
- **AUP.** Search/sort/filter fire on user action; paging is user-driven ("load more"); thumbnails lazy-load visible rows and cache. No bulk crawl.

## Contract evolution (ABI-safe throughout)

`SourceSearchHit` grows via **init-only properties** (the shipped 0.12.1 plugin's 7-arg ctor stays valid; it just leaves the new props null):

```csharp
public record SourceSearchHit(/* existing 7 positional */)
{
    public string? ThumbnailUrl { get; init; }
    public string? LargeImageUrl { get; init; }
    public string? Category { get; init; }
    public string? Version { get; init; }
    public int? DownloadCount { get; init; }
    public System.DateTimeOffset? UpdatedAt { get; init; }
    public long? FileSize { get; init; }
    // Per-user state (null when disconnected / old plugin):
    public bool? ViewerDownloaded { get; init; }
    public bool? ViewerEndorsed { get; init; }
    public bool? ViewerUpdateAvailable { get; init; }
    public bool? ViewerTracked { get; init; }
}
```

New optional interfaces (finalize names at build; `IModCatalog` stays for back-compat):

```csharp
public enum CatalogSort { MostEndorsed, MostDownloaded, RecentlyUpdated, RecentlyAdded }

public sealed record CatalogQuery(         // request envelope (grows without signature churn)
    string GameDomain, string? Text = null, CatalogSort Sort = CatalogSort.MostEndorsed,
    string? Category = null, int Offset = 0, int Count = 20);

public sealed record CatalogPage(IReadOnlyList<SourceSearchHit> Hits, int TotalCount);

public interface IModCatalogBrowse                         // Phase 1: sort + category + paging + viewer state
{
    Task<CatalogPage> BrowseCatalogAsync(CatalogQuery query);
    Task<IReadOnlyList<string>> GetCategoriesAsync(string gameDomain);
}

public sealed record CatalogDetail(/* description, images[], requirements[], version, uploader, stats */);

public interface IModCatalogDetail                         // Phase 2: full detail
{
    Task<CatalogDetail?> GetModDetailAsync(string gameDomain, int modId);
}

public interface IModCatalogActions                        // Phase 2: endorse/track in-app (authorized)
{
    Task<bool> SetEndorsedAsync(string gameDomain, int modId, bool endorsed);
    Task<bool> SetTrackedAsync(string gameDomain, int modId, bool tracked);
}
```

Each is feature-detected (`NexusSource is IModCatalogBrowse`, etc.); absent → the launcher hides that surface. `SearchAsync` (loose-identify) stays byte-for-byte unchanged throughout.

## Phase 1 — the storefront that knows you (launcher v0.13.0 + nexus-v0.13.0)

**Goal:** discovery that reflects your account. The centerpiece is the per-user badges — no browser tab can show these.

- **Cards, not rows.** Thumbnail, name, author, ♥ endorsements + ⬇ downloads, version, updated date, category. Grid/tile layout; lazy-loaded thumbnails with a decode cap + placeholder (optional disk cache mirroring `covers/`).
- **Knows-you badges** off `viewer*`: **Installed** (`viewerDownloaded`), **Endorsed** (`viewerEndorsed`), and the standout **Update available** (`viewerUpdateAvailable`). Requires sending the browse query authorized.
- **Browse with intent:** sort dropdown (endorsed/downloaded/recently updated/recently added) + **category filter** (`GetCategoriesAsync` → `categoryName`) + search, all composable; **load-more** paging via `offset`/`totalCount`.
- **Plugin:** implement `IModCatalogBrowse` (map `CatalogQuery` → filter+sort+offset, request `viewer*` + card fields + `thumbnailUrl`, map to the enriched hit); `GetCategoriesAsync` via the game's categories. Adult gate on all. csproj → Abstractions 0.13.0; `minBinaryVersion` → 0.13.0.
- **Launcher:** `NexusCatalogDialog` (or a new full-window view — decide at build; a dialog may be too small for a real storefront) renders cards + filters + paging; VM adds the browse/paging/category plumbing behind `CatalogBrowseAvailable`.
- **Tests:** plugin — each sort/category/offset builds the right query; `viewer*` + thumbnail requested + mapped; adult gate present; ABI test still green. Launcher — build + STORE seal + smoke.

## Phase 2 — depth + honoring the builders (launcher v0.14.0 + nexus-v0.14.0)

**Goal:** understand a mod without leaving, and give back to authors in-app.

- **In-app detail view:** click a card → `GetModDetailAsync` → description, large image(s), requirements (`nexusRequirements`/`dlcRequirements`), version, uploader, stats. **Download button hands to the browser** (law); the page lands on the author's mod page.
- **Endorse in-app** (`createModEndorsement`/`abstainFromModEndorsement`) and **Track in-app** (`trackMod`/`untrackMod`) via `IModCatalogActions`, sent authorized; optimistic UI reflecting `viewerEndorsed`/`viewerTracked`, revert on failure. Honors-the-builders without forcing a browser trip.
- **Plugin:** implement `IModCatalogDetail` + `IModCatalogActions` (mutations through the authorized transport). **Verify the mutation input/response shapes live before building** (same discipline as every phase here).
- **Tests:** detail mapping; endorse/track call the right mutation + parse success/failure; never throws.

## Phase 3 — the updates surface (launcher v0.15.0, optional standout)

**Goal:** turn `viewerUpdateAvailable` / `ModsFilter.hasUpdated` into a dedicated "mods you have with updates" view across the active game (or all added games) — the catalog becomes a maintenance surface, not just discovery. Scope confirmed after Phases 1–2 land.

## Release coupling (human-gated, per phase)

Each phase changes the contract → **launcher first** (publishes Abstractions x.y.z to NuGet) → **plugin** resolves it → feed delivers. The init-only-property + additive-interface rules keep the prior shipped plugin loading on the newer host in every interim window (degrades: no badges/detail/actions, listing intact).

## Non-goals (still out)

- In-app downloads / `nxm://` one-click (download stays a browser handoff).
- Any adult content or age-gating (excluded by construction).
- Collections (a separate Nexus concept; possible future line).
- Writing mod files or comments; posting on the user's behalf beyond endorse/track.

## Success criteria (per phase)

- **P1:** the catalog shows cards with Installed / Endorsed / **Update-available** badges, filters by category, sorts by the four views, and pages — all reflecting the signed-in account; adult absent; STORE seal green; prior plugin still loads.
- **P2:** a mod opens an in-app detail view; endorse and track work in-app and persist to Nexus; download still opens the browser.
- **P3:** a surface lists the user's mods that have updates available.
