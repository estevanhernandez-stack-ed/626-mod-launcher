# Nexus Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An in-app Nexus browse/discover surface for the active game — search results shown in-app, "Get" hands off to the browser — with adult content excluded server-side so the launcher needs no age-gating.

**Architecture:** A new optional plugin capability `IModCatalog.SearchCatalogAsync` (Abstractions 0.12.0, implemented by nexus-v0.12.0) runs the Nexus GraphQL mods search with an adult-content exclusion filter — distinct from the existing `IModTextSearch.SearchAsync` (loose-identify), which stays unfiltered. The launcher gates a "Browse Nexus (in app)" dialog on `NexusSource is IModCatalog`; results reuse `SourceSearchHit`; "Get" opens the mod's Nexus page in the browser and the downloaded file drops into the existing intake.

**Tech Stack:** .NET 10, C#, WinUI 3 (launcher), xUnit, Nexus GraphQL v2.

## Global Constraints

- **ABI:** never modify `IModTextSearch`, `IModSource`, or any DTO. Add `IModCatalog` as a NEW separate optional interface (the `IModTextSearch`/`IAuthorizedSend` precedent). Reuse `SourceSearchHit`. The shipped 0.11.0 plugin must still load on the 0.12.0 host (additive-only).
- **Adult exclusion is server-side, catalog-only.** The plugin's `SearchCatalogAsync` filters adult mods out of the query; `SearchAsync` (loose-identify) stays byte-for-byte unchanged. The launcher never sees adult hits — nothing to filter or message client-side. No age-gating UI, ever.
- **Download stays a browser handoff.** "Get" opens `hit.Url`; the app never fetches a mod file. Intake is untouched and content-agnostic.
- **GitHub/FULL-only, seal untouched.** Gate on `NexusSource is IModCatalog` — the capability check IS the flavor gate. No `#if FULL`. STORE build + `scripts/check-store-seal.ps1` must stay green.
- **CONFIRM at build (live-verify, do NOT guess):** the exact Nexus GraphQL v2 filter to exclude adult mods (an `adultContent`/`containsAdultContent`-style filter on the mods search). Query the live schema/endpoint; the test asserts the query carries the verified filter.
- **Never bare `dotnet` at repo root.** Launcher tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (STORE adds `-p:Configuration=Store`). Plugin tests: target the plugin test csproj explicitly.
- **Release coupling (human-gated):** launcher release publishes Abstractions 0.12.0 → plugin nexus-v0.12.0 resolves it → feed delivers → catalog lights up. Dev: local-pack Abstractions 0.12.0 to build the plugin before it's on NuGet.

---

## File structure

**Launcher (`626-mod-launcher`):**

| Path | Responsibility |
|---|---|
| `src/ModManager.Plugins.Abstractions/Contract.cs` | +`IModCatalog` optional interface |
| `tests/ModManager.Tests/Plugins/ModCatalogContractTests.cs` | ABI: `IModCatalog` shape + IModTextSearch/DTOs unchanged |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | `CatalogAvailable`/`CatalogVisibility` + `SearchCatalogAsync` |
| `src/ModManager.App/NexusCatalogDialog.xaml(.cs)` | the browse dialog (search box + results + Get) |
| `src/ModManager.App/MainWindow.xaml(.cs)` | "Browse Nexus (in app)" menu item + open-dialog wiring |
| `docs/smoke-tests/pending.md` | catalog smoke entry |

**Plugin (`626-mod-plugins`):**

| Path | Responsibility |
|---|---|
| `src/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj` | Abstractions `PackageReference` → 0.12.0 |
| `src/ModManager.Plugin.Nexus/NexusModSource.cs` | implement `IModCatalog.SearchCatalogAsync` (adult-excluding query) |
| `.github/workflows/release.yml` | minBinaryVersion bump |
| `tests/ModManager.Plugin.Nexus.Tests/NexusCatalogTests.cs` | adult-filter query + hits; `SearchAsync` unchanged |

---

## Task 1: Abstractions 0.12.0 — `IModCatalog`

**Files:**
- Modify: `src/ModManager.Plugins.Abstractions/Contract.cs`
- Test: `tests/ModManager.Tests/Plugins/ModCatalogContractTests.cs`

**Interfaces:**
- Produces: `interface IModCatalog { Task<IReadOnlyList<SourceSearchHit>> SearchCatalogAsync(string gameDomain, string query); }`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Plugins/ModCatalogContractTests.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using ModManager.Plugins.Abstractions;
using Xunit;

public class ModCatalogContractTests
{
    [Fact]
    public void IModCatalog_has_expected_shape()
    {
        var m = typeof(IModCatalog).GetMethod("SearchCatalogAsync")!;
        Assert.Equal(typeof(Task<IReadOnlyList<SourceSearchHit>>), m.ReturnType);
        var p = m.GetParameters();
        Assert.Equal(typeof(string), p[0].ParameterType);   // gameDomain
        Assert.Equal(typeof(string), p[1].ParameterType);   // query
    }

    [Fact]
    public void IModTextSearch_and_SourceSearchHit_unchanged_abi_safe()
    {
        // IModCatalog must be ADDITIVE — the identify search + the shared DTO are untouched.
        Assert.NotNull(typeof(IModTextSearch).GetMethod("SearchAsync"));
        var hit = typeof(SourceSearchHit);
        foreach (var n in new[] { "GameDomain", "ModId", "Name", "Author", "Summary", "EndorsementCount", "Url" })
            Assert.NotNull(hit.GetProperty(n));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter ModCatalogContract`
Expected: FAIL — `IModCatalog` does not exist.

- [ ] **Step 3: Add the interface**

In `Contract.cs`, after `IModTextSearch` (near the `SourceSearchHit` record), add:

```csharp
/// <summary>
/// Optional catalog-browse capability: search a game's mods for in-app discovery, with adult/mature
/// content EXCLUDED server-side (so the launcher never surfaces it and needs no age-gating). Distinct
/// from <see cref="IModTextSearch.SearchAsync"/>, which stays unfiltered for identifying the user's own
/// files. The host feature-detects with `source is IModCatalog`; plugins without it simply don't offer
/// the catalog.
/// </summary>
public interface IModCatalog
{
    Task<IReadOnlyList<SourceSearchHit>> SearchCatalogAsync(string gameDomain, string query);
}
```

(`System.Collections.Generic`, `System.Threading.Tasks` resolve via ImplicitUsings, matching the sibling interfaces.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter ModCatalogContract`
Expected: PASS (2/2).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Plugins.Abstractions/Contract.cs tests/ModManager.Tests/Plugins/ModCatalogContractTests.cs
git commit -m "feat(plugins): IModCatalog optional capability (adult-excluded catalog search; ABI-additive)"
```

---

## Task 2: Plugin (`626-mod-plugins`) — `SearchCatalogAsync` with adult exclusion

**Files:**
- Modify: `src/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj` (Abstractions → 0.12.0)
- Modify: `src/ModManager.Plugin.Nexus/NexusModSource.cs` (implement `IModCatalog`)
- Modify: `.github/workflows/release.yml` (minBinaryVersion)
- Test: `tests/ModManager.Plugin.Nexus.Tests/NexusCatalogTests.cs`

**Dev bootstrap (Abstractions 0.12.0 not yet on nuget.org):** pack it locally from the launcher repo and restore from a local source (same as the OAuth plugin work):
```bash
dotnet pack ../626-mod-launcher/src/ModManager.Plugins.Abstractions/ModManager.Plugins.Abstractions.csproj \
  -p:Version=0.12.0 -o ./local-nuget
# nuget.config already lists ./local-nuget as a source (from the OAuth work); if not, add it. PackageReference stays 0.12.0.
```

**Interfaces:**
- Consumes: `IModCatalog`, `SourceSearchHit` (Abstractions 0.12.0).
- Produces: `NexusModSource : …, IModCatalog`.

- [ ] **Step 1: LIVE-VERIFY the adult-exclusion filter (do this before writing the query)**

Query the live Nexus GraphQL v2 endpoint (`https://api.nexusmods.com/v2/graphql`) with the existing mods-search query shape plus a candidate adult-exclusion filter, and confirm which field/argument the schema actually accepts (e.g. a `filter` argument with an `adultContent`/`containsAdultContent` boolean, or a top-level arg). Record the exact working query text (this is the same live-verify discipline the OAuth endpoints + the DS2 domain used — a guessed filter that the schema rejects would silently return unfiltered results). Save the verified query string; the test in Step 2 asserts the code carries it.

- [ ] **Step 2: Write the failing test**

```csharp
// tests/ModManager.Plugin.Nexus.Tests/NexusCatalogTests.cs
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ModManager.Plugins.Abstractions;
using ModManager.Plugin.Nexus;
using Xunit;

public class NexusCatalogTests
{
    // Captures the outbound GraphQL body so we can assert the adult filter is present.
    private sealed class CaptureHost : IPluginHostServices
    {
        public string? LastBody;
        public void AddModSource(IModSource s) { }
        #pragma warning disable CS0618
        public string? GetCredential(string key) => null;
        #pragma warning restore CS0618
        public HttpClient HttpClient { get; }
        public string AppVersion => "0.12.0";
        public CaptureHost() => HttpClient = new HttpClient(new Handler(this));
        private sealed class Handler(CaptureHost h) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            {
                if (r.Content is not null) h.LastBody = await r.Content.ReadAsStringAsync(c);
                return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent("{\"data\":{\"mods\":{\"nodes\":[]}}}") };
            }
        }
    }

    [Fact]
    public async Task SearchCatalog_query_excludes_adult_content()
    {
        var host = new CaptureHost();
        var src = new NexusModSource(host);
        _ = await ((IModCatalog)src).SearchCatalogAsync("eldenring", "grace");
        Assert.NotNull(host.LastBody);
        // Replace <VERIFIED_ADULT_FILTER_TOKEN> with the exact token from the Step-1 live-verified query
        // (e.g. "adultContent" / "containsAdultContent"). This asserts the catalog query carries it.
        Assert.Contains("<VERIFIED_ADULT_FILTER_TOKEN>", host.LastBody);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj --filter NexusCatalog`
Expected: FAIL — `SearchCatalogAsync` / `IModCatalog` not implemented (won't compile until Step 4).

- [ ] **Step 4: Implement**

`.csproj`: `<PackageReference Include="ModManager.Plugins.Abstractions" Version="0.12.0" />`.

`NexusModSource.cs`: add `IModCatalog` to the class declaration and implement `SearchCatalogAsync`. It mirrors `SearchAsync`'s transport (build the GraphQL POST, route through the existing `SendAsync`, parse via the existing hit-mapping helper) but uses a **catalog query constant that carries the verified adult-exclusion filter**:

```csharp
// The catalog query = the mods search + the adult-exclusion filter verified live in Step 1.
// SearchAsync (identify) is left EXACTLY as-is; this is a separate query + method.
private const string CatalogQuery = /* the Step-1 verified GraphQL doc, with the adult filter */;

public async Task<IReadOnlyList<SourceSearchHit>> SearchCatalogAsync(string gameDomain, string query)
{
    // Same request/transport/parse path as SearchAsync, using CatalogQuery. Reuse the existing
    // GraphQL POST builder + hit mapper; do not duplicate the mapping logic — factor a shared private
    // helper if SearchAsync's body isn't already reusable, WITHOUT changing SearchAsync's behavior.
    // Returns adult-free SourceSearchHit list; never throws (mirror SearchAsync's degrade-to-empty).
}
```

`release.yml`: bump `minBinaryVersion` to match the 0.12.0 host line (same edit shape as the nexus-v0.11.0 cut).

- [ ] **Step 5: Run tests to verify green + `SearchAsync` untouched**

```bash
dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj --filter NexusCatalog   # PASS
dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj                          # all green
git diff -- src/ModManager.Plugin.Nexus/NexusModSource.cs   # confirm SearchAsync body is unchanged
```

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Plugin.Nexus .github/workflows/release.yml tests/ModManager.Plugin.Nexus.Tests
git commit -m "feat(nexus): IModCatalog.SearchCatalogAsync — adult-excluded catalog search (Abstractions 0.12.0)"
```

---

## Task 3: Launcher VM — gating + `SearchCatalogAsync`

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `IModCatalog` (Task 1), `NexusSource` (existing `IModSource?`), `NexusDomains.Effective`, `NexusActionsAvailable`, `ActiveGameHasNexusDomain` (all existing).
- Produces: `bool CatalogAvailable`, `Visibility CatalogVisibility`, `Task<IReadOnlyList<SourceSearchHit>> SearchCatalogAsync(string query)`.

- [ ] **Step 1: Add the gating + search method**

Mirror `LooseIdentifyAvailable` / the loose-identify search-delegate self-timeout:

```csharp
/// <summary>Catalog browse is available on the FULL build when the loaded Nexus source supports
/// IModCatalog and the active game resolves a Nexus domain. On STORE / no-plugin / older plugin the
/// source isn't IModCatalog, so this is false and the menu item is absent. The capability check IS the
/// flavor gate — no #if FULL.</summary>
public bool CatalogAvailable =>
    NexusActionsAvailable && NexusSource is IModCatalog && ActiveGameHasNexusDomain;
public Visibility CatalogVisibility => CatalogAvailable ? Visibility.Visible : Visibility.Collapsed;

/// <summary>Adult-excluded Nexus catalog search for the active game. Self-timeouts (~10s) so a hung
/// request can't wedge the dialog; never throws (empty list on any failure). Adult exclusion is
/// server-side in the plugin — the launcher receives only clean hits.</summary>
public async Task<IReadOnlyList<SourceSearchHit>> SearchCatalogAsync(string query)
{
    if (_ctx is null || NexusSource is not IModCatalog catalog) return System.Array.Empty<SourceSearchHit>();
    var domain = NexusDomains.Effective(_ctx.Game);
    if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(query))
        return System.Array.Empty<SourceSearchHit>();
    try
    {
        var search = catalog.SearchCatalogAsync(domain, query);
        var done = await Task.WhenAny(search, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
        return done == search ? await search.ConfigureAwait(false) : System.Array.Empty<SourceSearchHit>();
    }
    catch { return System.Array.Empty<SourceSearchHit>(); }
}
```

Add `using ModManager.Plugins.Abstractions;` if not already present (it is — `NexusSource`/`SourceSearchHit` are used elsewhere).

- [ ] **Step 2: Build the app to verify it compiles**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded (the dialog in Task 4 isn't referenced yet; this VM code compiles standalone).

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(catalog): VM gating (CatalogAvailable) + self-timeout SearchCatalogAsync"
```

---

## Task 4: Launcher UI — browse dialog + wiring + full gate

**Files:**
- Create: `src/ModManager.App/NexusCatalogDialog.xaml`, `src/ModManager.App/NexusCatalogDialog.xaml.cs`
- Modify: `src/ModManager.App/MainWindow.xaml` (menu item), `src/ModManager.App/MainWindow.xaml.cs` (open-dialog handler)
- Modify: `docs/smoke-tests/pending.md`

**Interfaces:**
- Consumes: `MainViewModel.SearchCatalogAsync` / `CatalogVisibility` (Task 3), `SourceSearchHit`, the existing launcher/open-URL service used by `OnFindMods`.

- [ ] **Step 1: Create `NexusCatalogDialog`**

Mirror `LooseIdentifyDialog.xaml(.cs)` (read it first — it's the established Nexus dialog pattern for wiring a `ContentDialog` to the VM). The dialog:
- A search `TextBox` (+ a Search button; Enter submits) bound to a local query field.
- On submit → `await ViewModel.SearchCatalogAsync(query)` → bind the result `IReadOnlyList<SourceSearchHit>` to an `ItemsControl`.
- Each result row (DataTemplate over `SourceSearchHit`): `Name` (bold), `Author`, `EndorsementCount` (with a ♥), `Summary` (trimmed), and a **Get** button whose click opens `hit.Url` in the browser via the same launcher/open-URL call `OnFindMods` uses.
- Visual states via simple visibility flags on the dialog: `Initial` ("Search Nexus for {game} mods"), `Loading` ("searching…"), `Empty` ("No results for '{query}'"), `Error` ("Couldn't reach Nexus — try again"). `SearchCatalogAsync` never throws, so "error" is the empty-after-a-real-attempt case only if you distinguish it; otherwise Empty covers both — keep it simple: Loading → (results | Empty).

Give the dialog a ctor taking the `MainViewModel` (like `LooseIdentifyDialog`) and the active game's display name for the title.

- [ ] **Step 2: Add the "Browse Nexus (in app)" menu item**

In `MainWindow.xaml`, the "Find mods" `DropDownButton` flyout — add as the FIRST item, above the existing browser items:

```xml
<MenuFlyoutItem Text="Browse Nexus (in app)" Click="OnBrowseNexusInApp"
                Visibility="{x:Bind ViewModel.CatalogVisibility, Mode=OneWay}"
                ToolTipService.ToolTip="Search Nexus for this game's mods without leaving the launcher — Get opens the mod page to download" />
```

- [ ] **Step 3: Wire the open-dialog handler**

In `MainWindow.xaml.cs`, near `OnFindMods`:

```csharp
private async void OnBrowseNexusInApp(object sender, RoutedEventArgs e)
{
    var dlg = new NexusCatalogDialog(ViewModel) { XamlRoot = Content.XamlRoot };
    await dlg.ShowAsync();
}
```

(Match the exact `ContentDialog` show pattern `LooseIdentifyDialog` uses — `XamlRoot`, `ShowAsync`.)

- [ ] **Step 4: Full gate**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64                       # FULL: Build succeeded
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Configuration=Store # STORE: Build succeeded
pwsh scripts/check-store-seal.ps1                                                            # STORE seal OK
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj                                  # Core suite green (incl. ModCatalogContract + the existing LegacyPluginAbi test still passing = 0.11.0 plugin loads on 0.12.0 host)
```

- [ ] **Step 5: Smoke entry + commit**

Append to `docs/smoke-tests/pending.md`: with the nexus-v0.12.0 plugin loaded + Nexus connected + a game with a Nexus domain — "Find mods → Browse Nexus (in app)" opens the dialog; a search returns results (no adult listings); Get opens the mod page in the browser; the downloaded file drops into intake unchanged. Verify the item is ABSENT on the Store build / a game with no Nexus domain / disconnected / with the older 0.11.0 plugin.

```bash
git add src/ModManager.App/NexusCatalogDialog.xaml src/ModManager.App/NexusCatalogDialog.xaml.cs \
        src/ModManager.App/MainWindow.xaml src/ModManager.App/MainWindow.xaml.cs docs/smoke-tests/pending.md
git commit -m "feat(catalog): in-app Nexus browse dialog + Find-mods entry (gated on IModCatalog)"
```

---

## Release choreography (human-gated — not a task)

1. Merge the launcher feature → cut **v0.12.0** → CI publishes **Abstractions 0.12.0** to NuGet.
2. Merge the plugin PR → tag **nexus-v0.12.0** (resolves Abstractions 0.12.0; minBinaryVersion bumped).
3. The signed plugin feed delivers nexus-v0.12.0 → FULL installs gain `IModCatalog` → "Browse Nexus (in app)" appears.
4. Store SKU unaffected (catalog is gated out; seal green). The Store-Nexus swing (compile-in) remains its own later spec, after the UX Store version publishes.

---

## Self-review — spec coverage

- Browse/discover in-app, per-game, off "Find mods" → Tasks 3 (gating/search) + 4 (dialog/menu). ✔
- No adult content, server-side, catalog-only → Task 2 (`SearchCatalogAsync` adult filter, `SearchAsync` untouched) + Task 1 (separate `IModCatalog`). ✔
- No age-gating / no in-app note → nothing to build (adult never reaches the launcher). ✔
- Intake unaffected → untouched (no task changes intake). ✔
- Get = browser handoff → Task 4 Step 1 (Get opens `hit.Url`). ✔
- GitHub-only, seal untouched → Task 3 gating + Task 4 Step 4 (STORE + seal). ✔
- ABI-safe (0.11.0 plugin loads on 0.12.0 host) → Task 1 (additive) + Task 4 Step 4 (LegacyPluginAbi still passes). ✔
- Reuse SourceSearchHit → Tasks 1-4. ✔
- CONFIRM adult filter live → Task 2 Step 1. ✔
- Release coupling → choreography section. ✔
