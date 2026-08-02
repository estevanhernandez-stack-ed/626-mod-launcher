# Nexus catalog v3 — thumbnails + sort views — design

**Date:** 2026-08-02
**Status:** Spec (approved-to-spec in-conversation; awaiting written review before build). Builds on the shipped catalog ([v1](2026-08-02-nexus-catalog-design.md), [v2 default listing](2026-08-02-nexus-catalog-default-listing-design.md)). GitHub/FULL-only. **Contract-changing** — spans both repos as a coupled cut: Abstractions 0.13.0 → nexus-v0.13.0 → launcher v0.13.0.

## The problem

The catalog now opens populated and ranks by endorsements, but two gaps make it feel thin:
1. **Text-only rows** — no thumbnails, so it reads like a list, not a storefront.
2. **One view** — endorsements only. Users want to sort (most downloaded, recently updated, recently added).

## Live-verified facts (2026-08-02, api.nexusmods.com/v2/graphql)

- **Thumbnails exist and populate.** The `Mod` node exposes `thumbnailUrl` (+ `thumbnailLargeUrl`, `pictureUrl`, blurred variants). A real palworld query returned usable CDN URLs (`https://staticdelivery.nexusmods.com/mods/6063/images/thumbnails/...png|jpeg`). Use `thumbnailUrl` for list rows.
- **Sort fields for the views exist.** `ModsSort` inputFields: `relevance, name, downloads, uniqueDownloads, endorsements, random, createdAt, updatedAt, size, lastComment`. So: Most endorsed = `endorsements`, Most downloaded = `downloads`, Recently updated = `updatedAt`, Recently added = `createdAt` — all `BaseSortValue { direction: DESC }`.
- **No "trending" sort.** `ModsSort` has no trending/hot field. Nexus's web "Trending" is not a GraphQL sort — **dropped** from the view list (revisit only if a facet-based approach proves cheap; out of scope here).

## Locked decisions

- **Views (dropdown):** Most endorsed (default), Most downloaded, Recently updated, Recently added. No trending.
- **Sort applies to both listing and search.** Default (empty query) uses the selected sort (Most endorsed by default). A typed query uses **Nexus relevance while the dropdown is on "Most endorsed"** (so an exact match isn't buried); picking any other view applies that sort to the search results too.
- **Thumbnails are lean + safe.** One small thumbnail per row (`thumbnailUrl`), fixed size, async load, placeholder while loading / on failure / when null. No full-size images or screenshot galleries (that's a later pass).
- **ABI stays intact.** The shipped 0.12.1 plugin must still load on the 0.13.0 host: the thumbnail field is additive (see below) and the sort capability is a *new* optional interface — an old plugin simply reports no thumbnail and no sort dropdown.
- **Adult exclusion unchanged** — the `adultContent: { value: false, op: EQUALS }` gate stays on every query; no age-gating.

## Contract (Abstractions 0.13.0)

**1. Thumbnail — ABI-safe init-only property (NOT a positional param).** Adding a positional parameter to the `SourceSearchHit` record would change its primary constructor and break the shipped 0.12.1 plugin (it calls the 7-arg ctor). Instead add an init-only property, which leaves the constructor signature untouched:

```csharp
public record SourceSearchHit(
    string GameDomain, int ModId, string Name, string? Author,
    string? Summary, int? EndorsementCount, string? Url)
{
    /// <summary>Small mod thumbnail (Nexus `thumbnailUrl`), or null. Old plugins leave it null.</summary>
    public string? ThumbnailUrl { get; init; }
}
```

Old plugin → `new SourceSearchHit(...)` still binds (ctor unchanged), `ThumbnailUrl` defaults null. New plugin sets it via `with { ThumbnailUrl = ... }` / object-init. Host reads it null-safe. **This is the ABI-safe way to grow a shipped record** — the existing `LegacyPluginAbi`-style test must keep passing.

**2. Sort — new optional interface (never modify `IModCatalog`).** Changing `IModCatalog.SearchCatalogAsync`'s signature would break old implementers, so add a sibling capability (the `IModTextSearch` / `IAuthorizedSend` / `IModCatalog` precedent). Working name `IModCatalogSorted` (finalize at build):

```csharp
public enum CatalogSort { MostEndorsed, MostDownloaded, RecentlyUpdated, RecentlyAdded }

public interface IModCatalogSorted
{
    Task<IReadOnlyList<SourceSearchHit>> SearchCatalogAsync(string gameDomain, string query, CatalogSort sort);
}
```

`IModCatalog` (2-arg) stays for back-compat. A plugin that implements `IModCatalogSorted` gets the dropdown; one that doesn't (old 0.12.x) falls back to `IModCatalog` (most-endorsed only), and the launcher hides/disables the dropdown.

## Plugin (nexus-v0.13.0)

- `NexusModSource` also implements `IModCatalogSorted`. Map `CatalogSort` → the verified `ModsSort` field (`endorsements|downloads|updatedAt|createdAt`, DESC). Empty query = listing with that sort; non-blank + MostEndorsed = relevance (existing name search); non-blank + other = name search with that sort.
- Every query keeps the `adultContent` exclusion. Add `thumbnailUrl` to the node selection in both documents; map it onto `SourceSearchHit.ThumbnailUrl` in `MapSearchNodes`.
- csproj → Abstractions **0.13.0**; `release.yml` `minBinaryVersion` → **0.13.0**. `SearchAsync` (loose-identify) stays byte-for-byte unchanged.
- **Tests:** each `CatalogSort` produces the right sort token (character-match, live-verified fields); `thumbnailUrl` is requested and mapped; adult gate present on all; `SearchAsync` unchanged.

## Launcher (v0.13.0)

- **`MainViewModel`:** overload `SearchCatalogAsync(string query, CatalogSort sort)` that prefers `IModCatalogSorted` when present, else falls back to the existing `IModCatalog` path. A `CatalogSortableAvailable` gate (`NexusSource is IModCatalogSorted`) drives the dropdown's visibility — re-raised at the same three sites as `CatalogVisibility` (the v2 lesson).
- **`NexusCatalogDialog`:** a **sort `ComboBox`** at the top next to the search box (Most endorsed / Most downloaded / Recently updated / Recently added); default Most endorsed. Changing it re-runs the current query with the new sort. Hidden/disabled when `IModCatalogSorted` is absent (old plugin).
- **Thumbnail in the row:** an `Image` (fixed size, e.g. ~96×54 to match Nexus tile ratio) bound to `ThumbnailUrl`. Async remote load with a decode size cap (memory), a neutral placeholder while loading / on error / when null. Rely on WinUI/HTTP image caching; **optional** disk cache mirroring the existing `covers/` pattern (`%LOCALAPPDATA%\ModManagerBuilder\covers`) if scroll perf needs it — decide during build, not required for v3.
- No Core change, no new persisted shape (unless the optional thumbnail disk-cache is added), no `#if FULL` (capability gate is the flavor gate; STORE seal stays green).

## AUP / performance

Thumbnails load from the static CDN (`staticdelivery.nexusmods.com`), not the API — normal browsing traffic. Load only visible rows (list virtualization), cap decode size, cache to avoid refetch. Search still fires on submit / sort-change, one domain, self-timeout — no bulk crawl.

## Release coupling (human-gated)

Contract changes (new interface + record property) → **launcher v0.13.0 first** (publishes Abstractions 0.13.0 to NuGet) → **plugin nexus-v0.13.0** resolves it → feed delivers → thumbnails + views light up. Same ordering as catalog v1. The ThumbnailUrl init-property + additive interface keep the shipped 0.12.1 plugin loading on the 0.13.0 host in the meantime (no thumbnails, no dropdown, endorsement listing intact).

## Non-goals (deferred)

- Full-size images / screenshot galleries / hover-preview.
- Categories, tag filters, adult toggle (adult stays excluded by construction).
- Infinite scroll / pagination beyond the first page (a "load more" could be a v3.1).
- Trending sort (no GraphQL sort field; would need a facet approach).
- `nxm://` one-click install; in-app downloads (Get stays a browser handoff).

## Success criteria

- Catalog rows show a thumbnail (placeholder when missing), and the dropdown switches between most-endorsed / most-downloaded / recently-updated / recently-added, re-querying live.
- Adult content still absent; loose-identify unchanged; STORE build + seal green.
- The shipped 0.12.1 plugin still loads on the 0.13.0 host (ABI intact) — no thumbnails, no dropdown, listing still works.
