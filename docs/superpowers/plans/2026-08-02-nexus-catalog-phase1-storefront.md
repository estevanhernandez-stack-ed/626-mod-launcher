# Nexus Catalog Phase 1 — the storefront that knows you — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the in-app Nexus catalog from a text list into a storefront that reflects the signed-in account — cards with thumbnails, per-user badges (Installed / Endorsed / **Update available**), category filter, sort views, and load-more paging.

**Architecture:** Additive plugin capability (`IModCatalogBrowse`) carrying a request/response envelope (`CatalogQuery` → `CatalogPage`), so future phases grow without signature churn. The plugin builds one GraphQL v2 document (filter + facets + sort + paging + `viewer*` fields) and maps it to an enriched `SourceSearchHit`. The launcher renders a full-size `NexusCatalogView` (UserControl swapped into a host Grid, mirroring `LibraryView`) instead of the current `ContentDialog`.

**Tech Stack:** .NET 10, C#, WinUI 3 (launcher), xUnit, Nexus GraphQL v2.

## Global Constraints

- **Contract source:** `docs/superpowers/specs/2026-08-02-nexus-catalog-great-design.md` (Phase 1 section). Read it before Task 1.
- **ABI-safe, always.** Never add positional parameters to `SourceSearchHit` (breaks the shipped 0.12.1 plugin's 7-arg ctor) — grow it ONLY with init-only properties. Never modify `IModSource`, `IModTextSearch`, `IModCatalog`, or `IAuthorizedSend` — add new interfaces only. The existing legacy-ABI test must stay green.
- **`SearchAsync` (IModTextSearch) stays byte-for-byte unchanged** — loose-identify must not regress.
- **Adult exclusion on every query:** `adultContent: { value: false, op: EQUALS }`. No age-gating UI, ever. No client-side adult filtering (server-side only).
- **Download stays a browser handoff.** No in-app file fetch. "Get" opens `hit.Url` via the existing `SafeUrl.IsHttpUrl` + `Process.Start(UseShellExecute=true)` pattern.
- **Read-path law:** catalog calls NEVER throw. Offline / non-2xx / GraphQL `errors` / malformed JSON / timeout → empty page. Self-timeout ~10s in the VM (existing `Task.WhenAny` + `Task.Delay` pattern).
- **camelCase JSON on disk** if any new persisted shape appears (none expected in Phase 1).
- **No `#if FULL`** — the capability check IS the flavor gate. STORE build must stay green and `scripts/check-store-seal.ps1` must report **STORE seal OK**.
- **Never run bare `dotnet build`/`dotnet test` at the repo root.** Use:
  - launcher tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
  - launcher app (FULL): `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
  - launcher app (STORE): add `-p:Configuration=Store`
  - plugin tests: `dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj` (in `C:\Users\estev\Projects\626-mod-plugins`)
- **Two repos.** Launcher: `C:\Users\estev\Projects\626-mod-launcher`. Plugin: `C:\Users\estev\Projects\626-mod-plugins`. Use `git -C <path>` explicitly — a tag in the wrong repo has already bitten this project once.
- **Commits:** conventional (`feat(catalog)`, `feat(nexus)`, `test(...)`). Trailer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- **Versions:** Abstractions → `0.13.0` (launcher release publishes it); plugin csproj PackageReference → `0.13.0`; plugin `release.yml` `minBinaryVersion` → `0.13.0`. Dev: local-pack Abstractions into the plugin repo's `local-nuget` before building the plugin (`dotnet pack src/ModManager.Plugins.Abstractions/ModManager.Plugins.Abstractions.csproj -p:Version=0.13.0 -o C:\Users\estev\Projects\626-mod-plugins\local-nuget`).

## Live-verified GraphQL facts (2026-08-02 — do NOT re-derive or guess; these are proven)

Verified against `https://api.nexusmods.com/v2/graphql`:

- `mods(...)` returns **`ModPage`** with `totalCount`, `nodes`, and **`facetsData`**.
- **Sort:** `sort: [ModsSort!]`, `ModsSort` fields include `endorsements`, `downloads`, `updatedAt`, `createdAt`, each `BaseSortValue { direction: SortDirection! }`, `SortDirection = ASC | DESC`. **No trending sort exists.**
- **Filter:** `ModsFilter.categoryName` and `.tag` take `[BaseFilterValue]` (`{ value, op }`) — same shape as `gameDomainName`. `adultContent` takes `[BooleanFilterValue]`.
- **Category filter proven:** `categoryName: { value: "Gameplay", op: EQUALS }` on `palworld` → `totalCount` 2725 → **1238**, every node `modCategory.name == "Gameplay"`.
- **Paging proven:** `offset: 3, count: 2` returned the #4/#5 mods by endorsement (Mod Config Menu 6081, PalEdit 5828).
- **Facets are the ONLY correct category source.** `facets: { categoryName: [] }` returns `facetsData.categoryName` as a name→count map, honoring the adult filter. Live for palworld: `Gameplay 1238, Pals 359, Visuals 239, Characters 214, Utilities 194, User Interface 108, Weapons 107, Miscellaneous 81, Audio 60, Outfits 51, Scripts 48, Animations 25, Palworld 3`.
  **TRAP (already hit during verification):** the root `categories(gameId:)` query returns *collection* categories (`Total Overhaul, Themed, Vanilla Plus, Essentials, Miscellaneous`) — **NOT** mod categories. Do not use it.
- **`viewer*` degrade safely.** Unauthenticated, `viewerDownloaded` / `viewerEndorsed` / `viewerUpdateAvailable` return **`null`** and `viewerTracked` returns `false` — **no error**. So requesting them is always safe.
  **UNPROVEN (must be smoke-tested, do not assert as fact):** that they populate `true` when the request carries the OAuth bearer. The host attaches the bearer via `IAuthorizedSend` (the endorse flow proved authorized requests work), but the `viewer*`-populate case has not been observed. Treat as expected-but-unverified until Este's smoke.
- **Node fields available:** `modId name summary author endorsements downloads version fileSize updatedAt createdAt thumbnailUrl thumbnailLargeUrl pictureUrl adultContent modCategory { name } game { domainName } viewerDownloaded viewerEndorsed viewerUpdateAvailable viewerTracked`.
- **`game(domainName:)`** exists (returns `id`, `name`) if a numeric game id is ever needed. Not needed in Phase 1.

## File Structure

**Launcher repo (`626-mod-launcher`):**

| File | Responsibility |
|---|---|
| `src/ModManager.Plugins.Abstractions/Contract.cs` (modify) | Add `CatalogSort`, `CatalogQuery`, `CatalogCategory`, `CatalogPage`, `IModCatalogBrowse`; add init-only props to `SourceSearchHit`. |
| `tests/ModManager.Tests/Plugins/ModCatalogBrowseContractTests.cs` (create) | Contract + ABI-safety assertions. |
| `src/ModManager.App/ViewModels/MainViewModel.cs` (modify) | `CatalogBrowseAvailable` gate; `BrowseCatalogAsync(CatalogQuery)`; raise new gate at the 3 existing sites. |
| `src/ModManager.App/NexusCatalogView.xaml(.cs)` (create) | Full-size storefront UserControl: filter bar + card grid + load-more. |
| `src/ModManager.App/MainWindow.xaml(.cs)` (modify) | Host Grid for the catalog view; "Browse Nexus (in app)" opens the view instead of the dialog. |
| `src/ModManager.App/NexusCatalogDialog.xaml(.cs)` (delete) | Superseded by the view. |
| `docs/smoke-tests/pending.md` (modify) | Phase 1 smoke entries (incl. the unproven `viewer*` populate check). |

**Plugin repo (`626-mod-plugins`):**

| File | Responsibility |
|---|---|
| `src/ModManager.Plugin.Nexus/CatalogQueryBuilder.cs` (create) | Pure function: `CatalogQuery` → (GraphQL document, variables). Isolated so it is unit-testable without HTTP. |
| `src/ModManager.Plugin.Nexus/NexusModSource.cs` (modify) | Implement `IModCatalogBrowse`; map `ModPage` → `CatalogPage`. |
| `tests/ModManager.Plugin.Nexus.Tests/NexusCatalogBrowseTests.cs` (create) | Query-shape + mapping tests. |
| `src/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj` (modify) | Abstractions → 0.13.0. |
| `.github/workflows/release.yml` (modify) | `minBinaryVersion` → 0.13.0. |

---

### Task 1: Abstractions 0.13.0 — the browse contract

**Files:**
- Modify: `src/ModManager.Plugins.Abstractions/Contract.cs`
- Test: `tests/ModManager.Tests/Plugins/ModCatalogBrowseContractTests.cs` (create)

**Interfaces:**
- Consumes: existing `SourceSearchHit`, `IModCatalog`.
- Produces: `CatalogSort`, `CatalogQuery`, `CatalogCategory`, `CatalogPage`, `IModCatalogBrowse`, and the new `SourceSearchHit` init-only properties — consumed by Tasks 2–6.

- [ ] **Step 1: Write the failing contract test**

Create `tests/ModManager.Tests/Plugins/ModCatalogBrowseContractTests.cs`. Mirror the style of the existing `tests/ModManager.Tests/Plugins/ModCatalogContractTests.cs` (read it first).

```csharp
using System.Reflection;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Plugins;

// Phase 1 browse contract. The ABI rules matter more than the shape: the shipped nexus-v0.12.1 plugin
// must still load on this host, which means SourceSearchHit's 7-arg constructor is frozen and the
// pre-existing interfaces are untouched. New capability = new interface.
public class ModCatalogBrowseContractTests
{
    [Fact]
    public void SourceSearchHit_positional_constructor_is_unchanged()
    {
        // The shipped 0.12.1 plugin calls this exact 7-arg ctor. Adding a positional parameter would
        // break it at load time (MissingMethodException), so growth happens via init-only properties.
        var ctor = typeof(SourceSearchHit).GetConstructor(new[]
        {
            typeof(string), typeof(int), typeof(string), typeof(string),
            typeof(string), typeof(int?), typeof(string),
        });
        Assert.NotNull(ctor);
    }

    [Fact]
    public void SourceSearchHit_exposes_the_phase1_init_only_properties()
    {
        foreach (var (name, type) in new (string, Type)[]
        {
            ("ThumbnailUrl", typeof(string)),
            ("Category", typeof(string)),
            ("Version", typeof(string)),
            ("DownloadCount", typeof(int?)),
            ("UpdatedAt", typeof(DateTimeOffset?)),
            ("ViewerDownloaded", typeof(bool?)),
            ("ViewerEndorsed", typeof(bool?)),
            ("ViewerUpdateAvailable", typeof(bool?)),
            ("ViewerTracked", typeof(bool?)),
        })
        {
            var p = typeof(SourceSearchHit).GetProperty(name);
            Assert.NotNull(p);
            Assert.Equal(type, p!.PropertyType);
        }
    }

    [Fact]
    public void Old_style_hit_construction_still_compiles_and_leaves_new_props_null()
    {
        // Exactly what an older plugin does.
        var hit = new SourceSearchHit("palworld", 1, "Mod", "Author", "Summary", 10, "https://x/1");
        Assert.Null(hit.ThumbnailUrl);
        Assert.Null(hit.ViewerUpdateAvailable);
    }

    [Fact]
    public void IModCatalogBrowse_has_the_expected_shape()
    {
        var m = typeof(IModCatalogBrowse).GetMethod("BrowseCatalogAsync");
        Assert.NotNull(m);
        Assert.Equal(typeof(Task<CatalogPage>), m!.ReturnType);
        var ps = m.GetParameters();
        Assert.Single(ps);
        Assert.Equal(typeof(CatalogQuery), ps[0].ParameterType);
    }

    [Fact]
    public void CatalogSort_covers_the_four_verified_views()
    {
        // No Trending: ModsSort has no trending field (live-verified 2026-08-02).
        Assert.Equal(
            new[] { "MostEndorsed", "MostDownloaded", "RecentlyUpdated", "RecentlyAdded" },
            Enum.GetNames(typeof(CatalogSort)));
    }

    [Fact]
    public void CatalogQuery_defaults_to_most_endorsed_first_page()
    {
        var q = new CatalogQuery("palworld");
        Assert.Null(q.Text);
        Assert.Equal(CatalogSort.MostEndorsed, q.Sort);
        Assert.Null(q.Category);
        Assert.Equal(0, q.Offset);
        Assert.Equal(20, q.Count);
    }

    [Fact]
    public void Existing_catalog_and_search_interfaces_are_untouched()
    {
        // ABI: old plugins implement these; their signatures are frozen.
        var catalog = typeof(IModCatalog).GetMethod("SearchCatalogAsync");
        Assert.NotNull(catalog);
        Assert.Equal(2, catalog!.GetParameters().Length);

        var search = typeof(IModTextSearch).GetMethod("SearchAsync");
        Assert.NotNull(search);
        Assert.Equal(2, search!.GetParameters().Length);
    }
}
```

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter ModCatalogBrowseContractTests`
Expected: FAIL — `CatalogSort` / `CatalogQuery` / `CatalogPage` / `IModCatalogBrowse` do not exist (compile error).

- [ ] **Step 3: Add the contract**

In `src/ModManager.Plugins.Abstractions/Contract.cs`, append (do NOT modify existing declarations). Add the init-only properties to `SourceSearchHit` by converting its declaration to a body form — the positional parameter list stays EXACTLY as-is:

```csharp
public record SourceSearchHit(
    string GameDomain, int ModId, string Name, string? Author,
    string? Summary, int? EndorsementCount, string? Url)
{
    /// <summary>Small mod thumbnail (Nexus <c>thumbnailUrl</c>), or null. Old plugins leave it null.</summary>
    public string? ThumbnailUrl { get; init; }
    /// <summary>Mod category name (Nexus <c>modCategory.name</c>), e.g. "Gameplay".</summary>
    public string? Category { get; init; }
    /// <summary>Author-published version string.</summary>
    public string? Version { get; init; }
    /// <summary>Total downloads.</summary>
    public int? DownloadCount { get; init; }
    /// <summary>Last update timestamp.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    // Per-user state. Null = unknown (disconnected, or an older plugin that never sets it) — the UI
    // shows a badge only when the value is explicitly true, so null/false both mean "no badge".
    public bool? ViewerDownloaded { get; init; }
    public bool? ViewerEndorsed { get; init; }
    public bool? ViewerUpdateAvailable { get; init; }
    public bool? ViewerTracked { get; init; }
}

/// <summary>Catalog sort views. Each maps to a live-verified <c>ModsSort</c> field; there is deliberately
/// no Trending — the schema has no trending sort.</summary>
public enum CatalogSort { MostEndorsed, MostDownloaded, RecentlyUpdated, RecentlyAdded }

/// <summary>A catalog browse request. A record envelope so later phases add options without changing
/// the interface signature. <paramref name="Text"/> null/blank = the default listing (no name filter).</summary>
public sealed record CatalogQuery(
    string GameDomain,
    string? Text = null,
    CatalogSort Sort = CatalogSort.MostEndorsed,
    string? Category = null,
    int Offset = 0,
    int Count = 20);

/// <summary>One category bucket with its mod count, from the browse response's facet data.</summary>
public sealed record CatalogCategory(string Name, int Count);

/// <summary>One page of catalog results. <paramref name="Categories"/> rides along on the same response
/// (facets), so the launcher needs no second round-trip to populate the category filter.</summary>
public sealed record CatalogPage(
    IReadOnlyList<SourceSearchHit> Hits,
    int TotalCount,
    IReadOnlyList<CatalogCategory> Categories)
{
    public static CatalogPage Empty { get; } =
        new(Array.Empty<SourceSearchHit>(), 0, Array.Empty<CatalogCategory>());
}

/// <summary>Optional capability: rich catalog browse (sort views, category filter, paging, per-user
/// state). Distinct from <see cref="IModCatalog"/>, which stays for back-compat — a host feature-detects
/// with <c>source is IModCatalogBrowse</c> and falls back to the simpler interface when absent.</summary>
public interface IModCatalogBrowse
{
    Task<CatalogPage> BrowseCatalogAsync(CatalogQuery query);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter ModCatalogBrowseContractTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Run the full launcher suite (ABI guard must stay green)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS, ~1438 passed / 0 failed. The legacy-plugin-ABI test MUST still pass.

- [ ] **Step 6: Commit**

```bash
git -C C:/Users/estev/Projects/626-mod-launcher add src/ModManager.Plugins.Abstractions/Contract.cs tests/ModManager.Tests/Plugins/ModCatalogBrowseContractTests.cs
git -C C:/Users/estev/Projects/626-mod-launcher commit -m "feat(catalog): IModCatalogBrowse contract + enriched SourceSearchHit (ABI-safe)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Plugin — the browse query builder (pure, testable)

**Files:**
- Create: `C:\Users\estev\Projects\626-mod-plugins\src\ModManager.Plugin.Nexus\CatalogQueryBuilder.cs`
- Test: `C:\Users\estev\Projects\626-mod-plugins\tests\ModManager.Plugin.Nexus.Tests\NexusCatalogBrowseTests.cs` (create)
- Modify: `src/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj` (Abstractions → 0.13.0)

**Interfaces:**
- Consumes: `CatalogQuery`, `CatalogSort` (Task 1).
- Produces: `CatalogQueryBuilder.Build(CatalogQuery) -> (string Document, Dictionary<string, object?> Variables)` — used by Task 4.

**Before you start:** local-pack Abstractions 0.13.0 so the plugin can resolve it:
```bash
dotnet pack C:/Users/estev/Projects/626-mod-launcher/src/ModManager.Plugins.Abstractions/ModManager.Plugins.Abstractions.csproj -p:Version=0.13.0 -o C:/Users/estev/Projects/626-mod-plugins/local-nuget
```
Then set `<PackageReference Include="ModManager.Plugins.Abstractions" Version="0.13.0" />` in the plugin csproj.

**Design notes (read before writing):**
- GraphQL requires every DECLARED variable to be USED. Build the variable declaration list dynamically — declare `$name` only when filtering by text, `$category` only when filtering by category.
- Sort tokens come from a controlled enum (never user text), so injecting the sort fragment into the document is safe. User-supplied text/category ALWAYS ride as JSON variables, never spliced into the document.
- **Relevance rule:** when there IS text AND sort is `MostEndorsed`, emit **no `sort:` argument** (Nexus default relevance) so an exact match is not buried. Any other sort applies even with text.

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Plugin.Nexus.Tests/NexusCatalogBrowseTests.cs`:

```csharp
using ModManager.Plugins.Abstractions;

namespace ModManager.Plugin.Nexus.Tests;

// Query-shape tests for the Phase 1 browse document. Every token asserted here was LIVE-VERIFIED against
// api.nexusmods.com/v2/graphql on 2026-08-02 — a guessed field/sort is silently ignored by the server and
// would return unfiltered or unsorted results, so the exact fragments are pinned.
public class CatalogQueryBuilderTests
{
    private static string Doc(CatalogQuery q) => CatalogQueryBuilder.Build(q).Document;

    [Fact]
    public void Default_listing_is_endorsement_sorted_and_adult_excluded()
    {
        var doc = Doc(new CatalogQuery("palworld"));
        Assert.Contains("sort: [{ endorsements: { direction: DESC } }]", doc);
        Assert.Contains("adultContent: { value: false, op: EQUALS }", doc);
        Assert.DoesNotContain("name: { value:", doc);       // no text filter on the default view
        Assert.DoesNotContain("categoryName: { value:", doc); // no category filter by default
    }

    [Theory]
    [InlineData(CatalogSort.MostDownloaded, "sort: [{ downloads: { direction: DESC } }]")]
    [InlineData(CatalogSort.RecentlyUpdated, "sort: [{ updatedAt: { direction: DESC } }]")]
    [InlineData(CatalogSort.RecentlyAdded, "sort: [{ createdAt: { direction: DESC } }]")]
    public void Each_sort_view_emits_its_verified_token(CatalogSort sort, string expected)
    {
        Assert.Contains(expected, Doc(new CatalogQuery("palworld", Sort: sort)));
    }

    [Fact]
    public void Text_search_with_default_sort_uses_relevance_no_sort_argument()
    {
        // An exact match must not be buried under popularity.
        var doc = Doc(new CatalogQuery("palworld", Text: "minimap"));
        Assert.Contains("name: { value: $name, op: WILDCARD }", doc);
        Assert.DoesNotContain("sort:", doc);
    }

    [Fact]
    public void Text_search_with_explicit_sort_keeps_that_sort()
    {
        var doc = Doc(new CatalogQuery("palworld", Text: "minimap", Sort: CatalogSort.MostDownloaded));
        Assert.Contains("name: { value: $name, op: WILDCARD }", doc);
        Assert.Contains("sort: [{ downloads: { direction: DESC } }]", doc);
    }

    [Fact]
    public void Category_filter_uses_the_verified_categoryName_shape_and_a_variable()
    {
        var built = CatalogQueryBuilder.Build(new CatalogQuery("palworld", Category: "Gameplay"));
        Assert.Contains("categoryName: { value: $category, op: EQUALS }", built.Document);
        Assert.Equal("Gameplay", built.Variables["category"]);
        // User-supplied text never lands in the document itself.
        Assert.DoesNotContain("Gameplay", built.Document);
    }

    [Fact]
    public void Requests_facets_viewer_state_and_card_fields()
    {
        var doc = Doc(new CatalogQuery("palworld"));
        Assert.Contains("facets: { categoryName: [] }", doc); // the ONLY correct mod-category source
        Assert.Contains("totalCount", doc);
        Assert.Contains("facetsData", doc);
        foreach (var f in new[]
        {
            "thumbnailUrl", "downloads", "version", "updatedAt", "modCategory",
            "viewerDownloaded", "viewerEndorsed", "viewerUpdateAvailable", "viewerTracked",
        })
            Assert.Contains(f, doc);
    }

    [Fact]
    public void Paging_rides_as_variables()
    {
        var built = CatalogQueryBuilder.Build(new CatalogQuery("palworld", Offset: 40, Count: 20));
        Assert.Equal(40, built.Variables["offset"]);
        Assert.Equal(20, built.Variables["count"]);
        Assert.Contains("offset: $offset", built.Document);
        Assert.Contains("count: $count", built.Document);
    }

    [Fact]
    public void Only_used_variables_are_declared()
    {
        // GraphQL rejects a declared-but-unused variable, so the declaration list must be dynamic.
        var noText = Doc(new CatalogQuery("palworld"));
        Assert.DoesNotContain("$name:", noText);
        Assert.DoesNotContain("$category:", noText);

        var withBoth = Doc(new CatalogQuery("palworld", Text: "x", Category: "Gameplay"));
        Assert.Contains("$name: String", withBoth);
        Assert.Contains("$category: String", withBoth);
    }

    [Fact]
    public void Blank_text_is_treated_as_no_text()
    {
        var doc = Doc(new CatalogQuery("palworld", Text: "   "));
        Assert.DoesNotContain("name: { value:", doc);
        Assert.Contains("sort: [{ endorsements: { direction: DESC } }]", doc); // falls back to the listing
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj --filter CatalogQueryBuilderTests`
Expected: FAIL — `CatalogQueryBuilder` does not exist.

- [ ] **Step 3: Implement the builder**

Create `src/ModManager.Plugin.Nexus/CatalogQueryBuilder.cs`:

```csharp
using ModManager.Plugins.Abstractions;

namespace ModManager.Plugin.Nexus;

/// <summary>
/// Builds the GraphQL v2 browse document + variables for a <see cref="CatalogQuery"/>. Pure and
/// HTTP-free so the exact query shape is unit-testable.
///
/// <para>Every token below was LIVE-VERIFIED against api.nexusmods.com/v2/graphql on 2026-08-02:
/// <c>ModsSort</c> exposes endorsements/downloads/updatedAt/createdAt as
/// <c>BaseSortValue { direction: SortDirection! }</c>; <c>ModsFilter.categoryName</c> takes a
/// <c>BaseFilterValue { value, op }</c> (proven: palworld 2725 -> 1238 for "Gameplay"); paging via
/// <c>offset</c>/<c>count</c> with <c>totalCount</c>; and <c>facets: { categoryName: [] }</c> returns
/// <c>facetsData.categoryName</c> as a name->count map. NOTE the root <c>categories(gameId:)</c> query
/// returns COLLECTION categories, not mod categories — facets are the only correct source.</para>
///
/// <para>Security/robustness: user-supplied text and category ALWAYS ride as JSON variables, never
/// spliced into the document. Sort tokens come from a closed enum. GraphQL rejects declared-but-unused
/// variables, so the declaration list is built dynamically.</para>
/// </summary>
internal static class CatalogQueryBuilder
{
    internal readonly record struct Built(string Document, Dictionary<string, object?> Variables);

    // The node selection — card fields + per-user viewer state. viewer* return null (not an error) when
    // the request is unauthenticated, so they are always safe to request.
    private const string NodeFields =
        "modId name summary author endorsements downloads version fileSize updatedAt thumbnailUrl " +
        "modCategory { name } game { domainName } " +
        "viewerDownloaded viewerEndorsed viewerUpdateAvailable viewerTracked";

    internal static Built Build(CatalogQuery query)
    {
        var hasText = !string.IsNullOrWhiteSpace(query.Text);
        var hasCategory = !string.IsNullOrWhiteSpace(query.Category);

        var vars = new Dictionary<string, object?>
        {
            ["domain"] = query.GameDomain,
            ["offset"] = query.Offset,
            ["count"] = query.Count,
        };

        var decls = new List<string> { "$domain: String!", "$offset: Int", "$count: Int" };
        var filters = new List<string>
        {
            "gameDomainName: { value: $domain, op: EQUALS }",
            "adultContent: { value: false, op: EQUALS }",
        };

        if (hasText)
        {
            decls.Add("$name: String");
            filters.Add("name: { value: $name, op: WILDCARD }");
            vars["name"] = query.Text!.Trim();
        }

        if (hasCategory)
        {
            decls.Add("$category: String");
            filters.Add("categoryName: { value: $category, op: EQUALS }");
            vars["category"] = query.Category!.Trim();
        }

        // Relevance rule: a text search under the default view uses Nexus relevance (no sort argument)
        // so an exact match is not buried by popularity. Any explicit view sorts even with text.
        var sortArg = hasText && query.Sort == CatalogSort.MostEndorsed ? "" : $"{SortFragment(query.Sort)}, ";

        var doc =
            $"query Browse({string.Join(", ", decls)}) {{ " +
            $"mods(filter: {{ {string.Join(", ", filters)} }}, " +
            $"facets: {{ categoryName: [] }}, " +
            $"{sortArg}offset: $offset, count: $count) {{ " +
            $"totalCount facetsData nodes {{ {NodeFields} }} }} }}";

        return new Built(doc, vars);
    }

    private static string SortFragment(CatalogSort sort) => sort switch
    {
        CatalogSort.MostDownloaded => "sort: [{ downloads: { direction: DESC } }]",
        CatalogSort.RecentlyUpdated => "sort: [{ updatedAt: { direction: DESC } }]",
        CatalogSort.RecentlyAdded => "sort: [{ createdAt: { direction: DESC } }]",
        _ => "sort: [{ endorsements: { direction: DESC } }]",
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj --filter CatalogQueryBuilderTests`
Expected: PASS (all builder tests).

- [ ] **Step 5: Commit**

```bash
git -C C:/Users/estev/Projects/626-mod-plugins add src/ModManager.Plugin.Nexus/CatalogQueryBuilder.cs tests/ModManager.Plugin.Nexus.Tests/NexusCatalogBrowseTests.cs src/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj
git -C C:/Users/estev/Projects/626-mod-plugins commit -m "feat(nexus): catalog browse query builder (sort/category/paging/facets/viewer)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Plugin — response mapping to `CatalogPage`

**Files:**
- Modify: `src/ModManager.Plugin.Nexus/NexusModSource.cs` (add a mapping helper; do NOT touch `SearchAsync` or `MapSearchNodes`)
- Test: `tests/ModManager.Plugin.Nexus.Tests/NexusCatalogBrowseTests.cs` (append a mapping test class)

**Interfaces:**
- Consumes: the JSON shape `{ data: { mods: { totalCount, facetsData: { categoryName: {name:count} }, nodes: [...] } } }`.
- Produces: `MapBrowsePage(JsonElement root, string gameDomain) -> CatalogPage` — used by Task 4.

- [ ] **Step 1: Write the failing mapping tests**

Append to `tests/ModManager.Plugin.Nexus.Tests/NexusCatalogBrowseTests.cs`:

```csharp
using System.Text.Json;

public class CatalogPageMappingTests
{
    // A representative response in the exact live shape (field names verified 2026-08-02).
    private const string Json = """
    { "data": { "mods": {
        "totalCount": 2727,
        "facetsData": { "categoryName": { "Gameplay": 1238, "Pals": 359, "Visuals": 239 } },
        "nodes": [
          { "modId": 577, "name": "MapUnlocker", "summary": "Unlocks the map.", "author": "W1ns",
            "endorsements": 8930, "downloads": 500000, "version": "1.2", "fileSize": 1024,
            "updatedAt": "2026-01-22T10:04:07Z",
            "thumbnailUrl": "https://staticdelivery.nexusmods.com/mods/6063/images/thumbnails/x.png",
            "modCategory": { "name": "Gameplay" }, "game": { "domainName": "palworld" },
            "viewerDownloaded": true, "viewerEndorsed": false,
            "viewerUpdateAvailable": true, "viewerTracked": false }
        ] } } }
    """;

    private static CatalogPage Map(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return NexusModSource.MapBrowsePageForTests(doc.RootElement, "palworld");
    }

    [Fact]
    public void Maps_total_count_and_categories_from_facets()
    {
        var page = Map(Json);
        Assert.Equal(2727, page.TotalCount);
        Assert.Equal(3, page.Categories.Count);
        // Ordered by count descending so the filter dropdown leads with the biggest buckets.
        Assert.Equal("Gameplay", page.Categories[0].Name);
        Assert.Equal(1238, page.Categories[0].Count);
    }

    [Fact]
    public void Maps_card_fields_and_viewer_state()
    {
        var hit = Assert.Single(Map(Json).Hits);
        Assert.Equal(577, hit.ModId);
        Assert.Equal("MapUnlocker", hit.Name);
        Assert.Equal(8930, hit.EndorsementCount);
        Assert.Equal(500000, hit.DownloadCount);
        Assert.Equal("1.2", hit.Version);
        Assert.Equal("Gameplay", hit.Category);
        Assert.StartsWith("https://staticdelivery.nexusmods.com/", hit.ThumbnailUrl);
        Assert.Equal(2026, hit.UpdatedAt!.Value.Year);
        Assert.True(hit.ViewerDownloaded);
        Assert.False(hit.ViewerEndorsed);
        Assert.True(hit.ViewerUpdateAvailable);
    }

    [Fact]
    public void Builds_the_mod_url_from_domain_and_id()
    {
        Assert.Equal("https://www.nexusmods.com/palworld/mods/577", Assert.Single(Map(Json).Hits).Url);
    }

    [Fact]
    public void Missing_or_null_fields_degrade_to_null_not_throw()
    {
        // Unauthenticated responses null the viewer fields; sparse mods omit optional fields entirely.
        var sparse = """
        { "data": { "mods": { "totalCount": 1, "nodes": [
            { "modId": 5, "name": "Bare", "game": { "domainName": "palworld" },
              "viewerDownloaded": null, "viewerEndorsed": null } ] } } }
        """;
        var hit = Assert.Single(Map(sparse).Hits);
        Assert.Equal("Bare", hit.Name);
        Assert.Null(hit.ThumbnailUrl);
        Assert.Null(hit.ViewerDownloaded);
        Assert.Null(hit.UpdatedAt);
        Assert.Empty(Map(sparse).Categories); // no facetsData -> no categories, no throw
    }

    [Fact]
    public void Malformed_shape_yields_an_empty_page()
    {
        Assert.Empty(Map("""{ "errors": [ { "message": "nope" } ] }""").Hits);
        Assert.Equal(0, Map("""{ "data": { } }""").TotalCount);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj --filter CatalogPageMappingTests`
Expected: FAIL — `MapBrowsePageForTests` does not exist.

- [ ] **Step 3: Implement the mapping**

In `NexusModSource.cs`, add (read the existing `MapSearchNodes` + `Int(...)` helpers first and match their defensive style — every level guarded, never throws):

```csharp
    /// <summary>Test seam for <see cref="MapBrowsePage"/> (the mapper is pure; this keeps the test from
    /// needing HTTP).</summary>
    internal static CatalogPage MapBrowsePageForTests(JsonElement root, string gameDomain)
        => MapBrowsePage(root, gameDomain);

    /// <summary>Map a browse response (<c>{ data: { mods: { totalCount, facetsData, nodes } } }</c>) onto a
    /// <see cref="CatalogPage"/>. Defensive at every level: a missing link anywhere yields an empty page
    /// rather than throwing (read-path law). Categories come from <c>facetsData.categoryName</c> — the only
    /// correct mod-category source — ordered by count descending.</summary>
    private static CatalogPage MapBrowsePage(JsonElement root, string gameDomain)
    {
        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("mods", out var mods) ||
            mods.ValueKind != JsonValueKind.Object)
            return CatalogPage.Empty;

        var total = mods.TryGetProperty("totalCount", out var tc) && tc.TryGetInt32(out var t) ? t : 0;

        var categories = new List<CatalogCategory>();
        if (mods.TryGetProperty("facetsData", out var facets) &&
            facets.ValueKind == JsonValueKind.Object &&
            facets.TryGetProperty("categoryName", out var cats) &&
            cats.ValueKind == JsonValueKind.Object)
        {
            foreach (var c in cats.EnumerateObject())
                if (c.Value.ValueKind == JsonValueKind.Number && c.Value.TryGetInt32(out var n))
                    categories.Add(new CatalogCategory(c.Name, n));
            categories.Sort((a, b) => b.Count.CompareTo(a.Count));
        }

        var hits = new List<SourceSearchHit>();
        if (mods.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object) continue;
                if (Int(node, "modId") is not { } modId) continue;
                var name = Str(node, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var domain = node.TryGetProperty("game", out var g) && g.ValueKind == JsonValueKind.Object
                    ? Str(g, "domainName") ?? gameDomain
                    : gameDomain;

                hits.Add(new SourceSearchHit(
                    domain, modId, name!, Str(node, "author"), Str(node, "summary"),
                    Int(node, "endorsements"), $"https://www.nexusmods.com/{domain}/mods/{modId}")
                {
                    ThumbnailUrl = Str(node, "thumbnailUrl"),
                    Version = Str(node, "version"),
                    DownloadCount = Int(node, "downloads"),
                    UpdatedAt = Date(node, "updatedAt"),
                    Category = node.TryGetProperty("modCategory", out var mc) && mc.ValueKind == JsonValueKind.Object
                        ? Str(mc, "name") : null,
                    ViewerDownloaded = Bool(node, "viewerDownloaded"),
                    ViewerEndorsed = Bool(node, "viewerEndorsed"),
                    ViewerUpdateAvailable = Bool(node, "viewerUpdateAvailable"),
                    ViewerTracked = Bool(node, "viewerTracked"),
                });
            }
        }

        return new CatalogPage(hits, total, categories);
    }

    // Null-safe scalar readers (mirror the existing Int helper's contract: null on absent/wrong-kind).
    private static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Bool(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

    private static DateTimeOffset? Date(JsonElement o, string name)
        => Str(o, name) is { } s && DateTimeOffset.TryParse(s, out var d) ? d : null;
```

**Note:** if `Str` / `Bool` already exist in the file with the same semantics, reuse them instead of adding duplicates.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj --filter CatalogPageMappingTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C C:/Users/estev/Projects/626-mod-plugins add -u
git -C C:/Users/estev/Projects/626-mod-plugins commit -m "feat(nexus): map browse response to CatalogPage (facet categories + viewer state)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Plugin — implement `IModCatalogBrowse` + ship metadata

**Files:**
- Modify: `src/ModManager.Plugin.Nexus/NexusModSource.cs` (class declaration + method)
- Modify: `.github/workflows/release.yml` (`minBinaryVersion` → `0.13.0`)
- Test: append to `tests/ModManager.Plugin.Nexus.Tests/NexusCatalogBrowseTests.cs`

**Interfaces:**
- Consumes: `CatalogQueryBuilder.Build` (Task 2), `MapBrowsePage` (Task 3), existing `SendAsync` / `ParseAsync` transport.
- Produces: `NexusModSource : IModCatalogBrowse` — feature-detected by Task 5.

- [ ] **Step 1: Write the failing end-to-end test**

Append (reuse the `CaptureHost` stub already in `NexusCatalogTests.cs` — copy its pattern or extract it to a shared file):

```csharp
public class BrowseCatalogAsyncTests
{
    [Fact]
    public async Task Browse_posts_the_built_document_and_returns_a_mapped_page()
    {
        var host = new CaptureHost(); // returns a canned ModPage body
        var src = new NexusModSource(host);
        var page = await ((IModCatalogBrowse)src).BrowseCatalogAsync(
            new CatalogQuery("palworld", Sort: CatalogSort.MostDownloaded, Category: "Gameplay"));

        Assert.NotNull(host.LastBody);
        Assert.Contains("sort: [{ downloads: { direction: DESC } }]", host.LastBody);
        Assert.Contains("adultContent: { value: false, op: EQUALS }", host.LastBody);
        Assert.NotNull(page);
    }

    [Fact]
    public async Task Browse_never_throws_on_a_failed_request()
    {
        var src = new NexusModSource(new FailingHost()); // handler throws HttpRequestException
        var page = await ((IModCatalogBrowse)src).BrowseCatalogAsync(new CatalogQuery("palworld"));
        Assert.Empty(page.Hits);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Search_and_catalog_paths_are_unaffected()
    {
        // The old interfaces still work — an older host may only know about them.
        var host = new CaptureHost();
        var src = new NexusModSource(host);
        _ = await ((IModCatalog)src).SearchCatalogAsync("palworld", "minimap");
        Assert.Contains("adultContent", host.LastBody!);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj --filter BrowseCatalogAsyncTests`
Expected: FAIL — `NexusModSource` does not implement `IModCatalogBrowse`.

- [ ] **Step 3: Implement**

Add `IModCatalogBrowse` to the class declaration (append to the existing interface list; do not remove any):

```csharp
public sealed class NexusModSource : IModSource, IModTextSearch, IModCatalog, IAuthorizedSend, IModCatalogBrowse
```

(Match the actual existing declaration — only ADD `IModCatalogBrowse`.)

Then add the method next to `SearchCatalogAsync`:

```csharp
    /// <summary>
    /// Phase 1 catalog browse: one authorized GraphQL call carrying sort + optional category + paging +
    /// facets + per-user <c>viewer*</c> state. Routed through the shared <see cref="SendAsync"/> transport,
    /// so the host attaches the OAuth bearer (<see cref="IAuthorizedSend"/>) and the plugin never sees it —
    /// which is what makes the viewer fields populate for the signed-in user.
    ///
    /// <para>Read-path law: NEVER throws. Offline / non-2xx / GraphQL errors / malformed JSON all yield
    /// <see cref="CatalogPage.Empty"/>. Adult content is excluded server-side on every query.</para>
    /// </summary>
    public async Task<CatalogPage> BrowseCatalogAsync(CatalogQuery query)
    {
        try
        {
            var built = CatalogQueryBuilder.Build(query);
            var body = JsonSerializer.Serialize(new { query = built.Document, variables = built.Variables });

            using var res = await SendAsync(HttpMethod.Post, $"{Base}/v2/graphql", body);
            if (!res.IsSuccessStatusCode) return CatalogPage.Empty;

            using var doc = await ParseAsync(res);
            return MapBrowsePage(doc.RootElement, query.GameDomain);
        }
        catch (HttpRequestException) { /* offline / DNS / TLS */ }
        catch (JsonException) { /* malformed body */ }
        catch (OperationCanceledException) { /* timeout / cancellation */ }
        return CatalogPage.Empty;
    }
```

Then bump `.github/workflows/release.yml`: `"minBinaryVersion": "0.13.0"`.

- [ ] **Step 4: Run the full plugin suite**

Run: `dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj`
Expected: PASS, 0 failed (68 existing + the new browse tests).

- [ ] **Step 5: Commit**

```bash
git -C C:/Users/estev/Projects/626-mod-plugins add -u
git -C C:/Users/estev/Projects/626-mod-plugins commit -m "feat(nexus): implement IModCatalogBrowse (authorized browse, minBinaryVersion 0.13.0)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Launcher VM — browse plumbing + gate

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `IModCatalogBrowse`, `CatalogQuery`, `CatalogPage` (Task 1); existing `NexusSource`, `_ctx`, `NexusDomains.Effective`, `NexusActionsAvailable`.
- Produces: `CatalogBrowseAvailable`, `CatalogBrowseVisibility`, `BrowseCatalogAsync(...)` — consumed by Task 6.

**Context:** `CatalogAvailable` / `CatalogVisibility` already exist (~line 1596) and are raised at THREE sites — the mods-reload/game-switch path, the no-game early return, and `RaiseNexusStateChanged`. A Phase-0 bug was exactly a missed raise, so the new gate MUST be raised at all three.

- [ ] **Step 1: Add the gate + browse method**

Next to `CatalogAvailable`:

```csharp
    /// <summary>Rich catalog browse (cards, sort views, category filter, paging, per-user badges) —
    /// available when the loaded plugin implements the Phase 1 capability. Older plugins fall back to the
    /// simpler <see cref="CatalogAvailable"/> path, and STORE/no-plugin leaves both false.</summary>
    public bool CatalogBrowseAvailable =>
        NexusActionsAvailable && NexusSource is IModCatalogBrowse && ActiveGameHasNexusDomain;
    public Visibility CatalogBrowseVisibility => CatalogBrowseAvailable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Fetch one catalog page for the active game. Self-timeouts (~10s) so a hung request can't
    /// wedge the view; never throws (empty page on any failure).</summary>
    public async Task<CatalogPage> BrowseCatalogAsync(
        string? text, CatalogSort sort, string? category, int offset, int count = 20)
    {
        if (_ctx is null || NexusSource is not IModCatalogBrowse browse) return CatalogPage.Empty;
        var domain = NexusDomains.Effective(_ctx.Game);
        if (string.IsNullOrWhiteSpace(domain)) return CatalogPage.Empty;
        try
        {
            var call = browse.BrowseCatalogAsync(new CatalogQuery(domain!, text, sort, category, offset, count));
            var done = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            return done == call ? await call.ConfigureAwait(false) : CatalogPage.Empty;
        }
        catch { return CatalogPage.Empty; }
    }
```

- [ ] **Step 2: Raise the gate at all three existing sites**

Wherever `OnPropertyChanged(nameof(CatalogVisibility));` appears (three places), add directly beneath:

```csharp
            OnPropertyChanged(nameof(CatalogBrowseAvailable));
            OnPropertyChanged(nameof(CatalogBrowseVisibility));
```

Match the surrounding indentation at each site (two are 12-space, one is 8-space).

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors. (A file-copy `MSB3021` error means a launcher instance is running — close it and retry; that is not a compile failure.)

- [ ] **Step 4: Commit**

```bash
git -C C:/Users/estev/Projects/626-mod-launcher add src/ModManager.App/ViewModels/MainViewModel.cs
git -C C:/Users/estev/Projects/626-mod-launcher commit -m "feat(catalog): VM browse plumbing + CatalogBrowseAvailable gate

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: Launcher UI — the storefront view

**Files:**
- Create: `src/ModManager.App/NexusCatalogView.xaml` + `.xaml.cs`
- Modify: `src/ModManager.App/MainWindow.xaml` + `.xaml.cs`
- Delete: `src/ModManager.App/NexusCatalogDialog.xaml` + `.xaml.cs`
- Modify: `docs/smoke-tests/pending.md`

**Interfaces:**
- Consumes: `MainViewModel.BrowseCatalogAsync`, `CatalogBrowseVisibility`, `CatalogPage`, `SourceSearchHit` (Tasks 1/5).

**Context — read these first:**
- `src/ModManager.App/LibraryView.xaml(.cs)` — the precedent for a full-size UserControl swapped into a host Grid (`LibraryHost` in `MainWindow.xaml`, ~line 627). The catalog follows this pattern, NOT a `ContentDialog`; a storefront grid needs the room.
- `src/ModManager.App/NexusCatalogDialog.xaml.cs` — the Row wrapper, `TrimSummary`, and the Get handler (`SafeUrl.IsHttpUrl` + `Process.Start(UseShellExecute = true)`) all port over.
- `MainWindow.xaml` "Find mods" `DropDownButton` — the existing "Browse Nexus (in app)" item.

- [ ] **Step 1: Build the view**

`NexusCatalogView.xaml` — a UserControl with:
- **Filter bar (top):** search `TextBox` (submit on Enter + button), sort `ComboBox` (Most endorsed / Most downloaded / Recently updated / Recently added), category `ComboBox` (bound to the page's `Categories`, first item "All categories", each shown as `Name (Count)`), and a result-count line (`{loaded} of {totalCount}`).
- **Card grid (middle):** `GridView` (or `ItemsRepeater` with a wrap layout) over a `Card` view-model. Each card: thumbnail `Image` (fixed ~96×54, `Stretch="UniformToFill"`, neutral placeholder when `ThumbnailUrl` is null/fails), bold name, author, `♥ endorsements` + `⬇ downloads`, category + version + updated date, badges row, and a **Get** button.
- **Badges:** show ONLY when the value is explicitly `true` — `Installed` (`ViewerDownloaded`), `Endorsed` (`ViewerEndorsed`), `Update available` (`ViewerUpdateAvailable`). Null/false = no badge (null means unknown/disconnected). Give **Update available** the strongest visual weight — it is the standout.
- **Load more (bottom):** button visible only while `loaded < TotalCount`; appends the next page (`offset += count`).

Expose a `Card` class mirroring the old `Row` (Name/Author/Endorsements/Summary + the new fields and `Visibility` helpers per badge), so XAML binds simple strings/visibilities.

Behavior in `.xaml.cs`:
- `LoadAsync()` on first show and on any filter change → reset `offset = 0`, replace items.
- Sort/category change → reload from offset 0. Search submit → reload from offset 0.
- Load-more → fetch next offset, **append**.
- A single in-flight guard (ignore/cancel overlapping requests) so fast filter clicks can't interleave pages out of order.
- States: Loading / results / empty (`No Nexus mods found for {game}.` or `No results for '{query}'.`).
- Get: unchanged browser handoff.

- [ ] **Step 2: Host it in MainWindow**

- Add a `CatalogHost` Grid alongside `LibraryHost`, hidden by default.
- Change `OnBrowseNexusInApp` to create/show `NexusCatalogView` in that host (hide the game panel) instead of showing the dialog; add a back affordance returning to the game view.
- Keep the menu item bound to `CatalogVisibility` OR `CatalogBrowseVisibility` — with a 0.12.x plugin the rich view is unavailable, so in that case keep opening the simple list. Simplest correct approach: bind the item to `CatalogVisibility` (true for both) and inside the handler pick the rich view when `CatalogBrowseAvailable`, else the old simple path. **Decide and document in the code comment.**
- Delete `NexusCatalogDialog.*` only if the fallback no longer needs it; otherwise keep it for the older-plugin path.

- [ ] **Step 3: Build FULL + STORE and run the seal**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Configuration=Store
pwsh -File scripts/check-store-seal.ps1
```
Expected: 0 errors both flavors; **STORE seal OK**.

- [ ] **Step 4: Add smoke entries**

Append to `docs/smoke-tests/pending.md`:
- Open Browse Nexus on a game with a Nexus domain → the storefront opens **populated** with cards + thumbnails, most-endorsed first.
- **Badges (the unproven bit):** while connected to Nexus, confirm at least one card shows **Installed** / **Endorsed** for a mod you have — this verifies `viewer*` populate under the bearer, which was NOT provable pre-release. If no badges EVER appear, suspect the viewer fields are not populating on the authorized request and check the raw response.
- Category dropdown lists real mod categories with counts (Gameplay, Pals, Visuals…) and filtering narrows results.
- Each sort view reorders; text search returns relevant hits; **no adult listings** anywhere.
- Load more appends the next page without duplicates.
- Get opens the mod's Nexus page in the browser.
- Gating: absent on the Store build / a game with no Nexus domain; with a 0.12.x plugin the old simple list still works.

- [ ] **Step 5: Commit**

```bash
git -C C:/Users/estev/Projects/626-mod-launcher add src/ModManager.App docs/smoke-tests/pending.md
git -C C:/Users/estev/Projects/626-mod-launcher commit -m "feat(catalog): storefront view — cards, badges, category filter, sort, paging

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: Whole-branch verification

**Files:** none (verification only).

- [ ] **Step 1: Full launcher suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: 0 failed. The legacy-plugin-ABI test green (the shipped 0.12.1 plugin still loads on a 0.13.0 host).

- [ ] **Step 2: Full plugin suite**

Run: `dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj` (in the plugin repo)
Expected: 0 failed.

- [ ] **Step 3: Both flavors + seal**

As Task 6 Step 3. Expected: 0 errors; STORE seal OK.

- [ ] **Step 4: Confirm no operating-law regressions**

Grep the diff and confirm:
- `adultContent: { value: false, op: EQUALS }` present on every catalog query path; no client-side adult filtering anywhere.
- No in-app download — the only outbound "Get" is `Process.Start` on a `SafeUrl.IsHttpUrl`-validated URL.
- `SearchAsync` (loose-identify) byte-for-byte unchanged: `git -C <plugin> diff master -- src/ModManager.Plugin.Nexus/NexusModSource.cs` shows no edits inside it.
- No `#if FULL` added; no new on-disk JSON shape.

---

## Release choreography (human-gated — Este triggers the tags)

Contract change → **launcher first**:

1. PR + merge the launcher branch → `master`.
2. Tag **v0.13.0** on the launcher → CI builds the DRAFT release AND publishes **Abstractions 0.13.0** to NuGet.
3. Wait for 0.13.0 to appear on nuget.org (indexing lag is normal; verify with
   `curl -s https://api.nuget.org/v3-flatcontainer/modmanager.plugins.abstractions/index.json`).
4. PR + merge the plugin branch → `main`.
5. Tag **nexus-v0.13.0** in the PLUGIN repo (`git -C C:/Users/estev/Projects/626-mod-plugins tag ...`) → CI signs the plugin + pushes the signed feed.
6. Publish the launcher DRAFT, update the install, Settings → "Install / refresh Nexus plugin", then **fully quit and relaunch** (plugins load once at startup).
7. Smoke per `docs/smoke-tests/pending.md`.
