# Nexus catalog — default most-endorsed listing (v2) — design

**Date:** 2026-08-02
**Status:** Spec (approved in-conversation). Enhancement to the shipped in-app Nexus catalog ([2026-08-02-nexus-catalog-design.md](2026-08-02-nexus-catalog-design.md)). GitHub/FULL-only. Spans both repos (launcher v0.12.1 + plugin nexus-v0.12.1). **No Abstractions/contract change** — same `IModCatalog.SearchCatalogAsync(gameDomain, query)` signature.

## The problem

The shipped v1 catalog is a **scoped search box**: nothing appears until the user types a query. That's not a catalog — it forces the user to already know what to hunt for, and typing the game's own name into a search already scoped to that game is a non-answer. A catalog should open *populated*: show the game's mods ranked so the good stuff floats up.

Also fixed here: a v1 bug where **"Browse Nexus (in app)" never appeared** because `CatalogVisibility` was only re-raised on plugin hot-load, not on game-switch / mods-reload / Nexus connect (found during the v1 smoke).

## Locked decisions

- **Open → populated.** Opening Browse Nexus for a game immediately loads a default listing — no search term required.
- **Default sort = most endorsed.** All-time most-endorsed mods first (the ♥ ranking) — the proven/essential mods, best "what should I install" default.
- **Search stays; text search = Nexus relevance.** When the user types, relevance wins (default Nexus ordering) so a niche exact match isn't buried under high-endorsement noise. Only the *default* (empty) view is endorsement-sorted.
- **Adult still excluded server-side.** The `adultContent: { value: false, op: EQUALS }` gate stays on both the listing and the search query. No age-gating, no client-side filtering.
- **Lean text rows unchanged.** Thumbnails are the *next* step (a bigger DTO change), not this pass.
- **No contract change.** Empty query → listing is a plugin-internal branch; the `IModCatalog` signature and `SourceSearchHit` are untouched → the shipped 0.12.0 plugin still loads, and a 0.12.1 launcher with the 0.12.0 plugin degrades to blank-until-search (no crash).

## Live-verified GraphQL (2026-08-02, verify-don't-guess)

`ModsSort` has an `endorsements` field of type `BaseSortValue { direction: SortDirection! }`; `SortDirection` = `ASC | DESC`; `mods(sort: [ModsSort!])` takes a list. Proven against the live v2 endpoint for `palworld`:

```graphql
query CatalogListing($domain: String!) {
  mods(
    filter: { gameDomainName: { value: $domain, op: EQUALS }, adultContent: { value: false, op: EQUALS } }
    sort: [{ endorsements: { direction: DESC } }]
    count: 20
  ) { nodes { modId name summary author endorsements game { domainName } } }
}
```

Returned Palworld's mods in descending endorsement order (MapUnlocker 8929, Pal Analyzer 7562, Carry weight increase 6223, …; totalCount 2725; every node `adultContent:false`). The **only** differences from the v1 `CatalogQuery`: no `name` filter, and the `sort` argument.

## Plugin (nexus-v0.12.1)

`NexusModSource.SearchCatalogAsync(gameDomain, query)`:
- **Blank/whitespace query** → run `CatalogListingQuery` (above): domain + adult-exclusion + `endorsements DESC`, `count: 20`. (v1 returned nothing on blank.)
- **Non-blank query** → unchanged v1 `CatalogQuery` (name WILDCARD + domain + adult-exclusion, relevance order, `count: 10`).
- Same `SendAsync` transport + `MapSearchNodes` mapping for both. `SearchAsync` (loose-identify) stays byte-for-byte unchanged.
- csproj stays on Abstractions **0.12.0** (no bump). `release.yml` `minBinaryVersion` stays `0.12.0`.

**Test (TDD, plugin test project):** blank query builds the listing document (asserts it carries `sort:`/`endorsements`/`DESC` and the `adultContent` gate and NO `name` filter); non-blank builds the name document (carries `name`, no `sort`); both route through the stub host and map nodes → hits. `SearchAsync` document unchanged.

## Launcher (v0.12.1)

- **`MainViewModel.SearchCatalogAsync(query)`**: allow a blank query through to the plugin (v1 short-circuited blank → empty). Keep the domain guard, the ~10s self-timeout, and never-throws → empty-on-failure.
- **`NexusCatalogDialog`**: on open (Loaded), auto-fire `SearchCatalogAsync("")` → render the default listing (Loading → results/empty). Typing + submit still calls with the typed query. Initial "type to search" placeholder becomes the loaded listing.
- **Visibility fix (rides along):** raise `CatalogAvailable` + `CatalogVisibility` at the three sites that already raise `LooseIdentify*` — the mods-reload/game-switch path, the no-game early-return, and `RaiseNexusStateChanged` (Nexus connect/disconnect). Already applied on `fix/catalog-visibility-notify`.

No Core changes, no new persisted shape, no `#if FULL` (capability gate is the flavor gate; STORE seal untouched).

## Release coupling (human-gated)

No contract change → **no NuGet round-trip and no ordering dependency**. Launcher v0.12.1 and plugin nexus-v0.12.1 can be cut in either order. The listing populates once the nexus-v0.12.1 plugin is delivered by the feed; before that a 0.12.1 launcher degrades to blank-until-search.

## Non-goals (still deferred)

- Thumbnails / images (bigger DTO — the next step after this).
- Categories, user-selectable sort/filter, pagination beyond the first page.
- `nxm://` one-click install; in-app downloads (download stays a browser handoff — Get opens the mod page).
- Any in-app adult content or age-gating (excluded by construction).

## Success criteria

- Open Browse Nexus for a game with a Nexus domain and the nexus-v0.12.1 plugin loaded → **the game's most-endorsed, adult-free mods appear immediately**, no typing.
- Typing a term searches (relevance) and narrows; Get opens the mod's Nexus page.
- "Browse Nexus (in app)" appears reliably on game-switch and after Nexus connect (visibility fix).
- Loose-identify (`SearchAsync`) unchanged; STORE build + seal green; shipped 0.12.0 plugin still loads on the 0.12.1 host.
