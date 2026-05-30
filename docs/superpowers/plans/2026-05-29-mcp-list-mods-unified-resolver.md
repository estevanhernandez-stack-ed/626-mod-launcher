# MCP `list_mods` — Unified Mod-Listing Resolver Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the agent-access MCP report the same mods the launcher's UI shows for every engine, by extracting one read-only Core resolver (`ModListing.Resolve`) that the App and the MCP both call — killing the second source of truth that let `list_mods` return `[]` for direct-inject (FromSoft) games.

**Architecture:** A new `ModListing.Resolve(GameEntry)` in `ModManager.Core` dispatches by engine (ME2 config → direct-inject → scanner), then merges `metadata.json`. It is strictly read-only; the two writes hidden in the scanner path (`SaveClassification`, `MigrateDataDir`) become explicit App-side steps. The App's `DirectInjectService` / `ModEngineService` listing glue moves into Core as `DirectInjectListing` / `ModEngine2Listing`; the App keeps its write ops and delegates folder/config resolution to the Core helpers.

**Tech Stack:** .NET 10, C# (nullable on, warnings-as-errors), xUnit. Headless Core tests; WinUI App built but not unit-tested.

**Spec:** [docs/superpowers/specs/2026-05-29-mcp-list-mods-unified-resolver-design.md](../specs/2026-05-29-mcp-list-mods-unified-resolver-design.md) · **Issue:** [#86](https://github.com/estevanhernandez-stack-ed/626-mod-launcher/issues/86)

**Conventions reminder:** Never run bare `dotnet test`/`dotnet build` at the repo root — always target `tests/ModManager.Tests/ModManager.Tests.csproj` or `src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. On-disk JSON is camelCase. No `File.Delete` in toggle/replace paths. New Core files must not reference WinUI/WinRT (`CorePurityTests` enforces).

---

## File Structure

**Create (Core):**
- `src/ModManager.Core/ModListing.cs` — the unified read-only resolver (dispatch + merge).
- `src/ModManager.Core/DirectInjectListing.cs` — direct-inject listing + shared folder helpers (moved from `DirectInjectService`).
- `src/ModManager.Core/ModEngine2Listing.cs` — ME2 config-backed listing + `ReadConfig` (moved from `ModEngineService`).

**Modify (Core):**
- `src/ModManager.Core/Scanner.cs` — extract `ClassifyInMemory` (private) + add public `ListClassified` and `PersistClassification`; refactor `ListWithClass` to reuse them (contract unchanged).

**Modify (App):**
- `src/ModManager.App/ViewModels/MainViewModel.cs` — list via `ModListing.Resolve`; the two writes become explicit, scanner-world-gated steps; delete the now-dead `ReloadFromScannerAsync`.
- `src/ModManager.App/Services/DirectInjectService.cs` — delete `List`/`Row`/`Names`; `Applies`/`PlayFolder`/`Holding`/`Enabled` delegate to `DirectInjectListing`.
- `src/ModManager.App/Services/ModEngineService.cs` — delete `ListMods`; `IsConfigBacked`/`ReadConfig` delegate to `ModEngine2Listing`.

**Modify (MCP):**
- `src/ModManager.Mcp/Tools/ModTools.cs` — `ListMods` calls `ModListing.Resolve` + marshals enrichment (`displayTitle`/`author`/`sourceUrl`).

**Create / Modify (Tests):**
- `tests/ModManager.Tests/FromSoftFixture.cs` — shared on-disk FromSoft fixture builder (new).
- `tests/ModManager.Tests/ModListingTests.cs` — resolver tests (new).
- `tests/ModManager.Tests/ScannerCoreTests.cs` — add read-only classify tests.
- `tests/ModManager.Tests/Mcp/ReadToolsTests.cs` — add fromsoft + enrichment tests.

**Modify (Docs):**
- `docs/smoke-tests/pending.md` — App parity smoke entry.

---

## Task 1: Extract read-only classify in Scanner

**Files:**
- Modify: `src/ModManager.Core/Scanner.cs` (the `ListWithClass` method, ~lines 604-619)
- Test: `tests/ModManager.Tests/ScannerCoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `tests/ModManager.Tests/ScannerCoreTests.cs` (the class already has the `Setup()` helper returning a scanner-world `GameContext` with a pinned `DataDir`):

```csharp
    [Fact]
    public void ListClassified_sets_class_without_writing_classification()
    {
        var (_, _, _, c) = Setup();
        var mods = Scanner.ListClassified(c);
        Assert.Equal("both", mods.First(m => m.Name == "Cool").Class);
        Assert.False(File.Exists(c.ClassificationPath)); // read-only: no write
    }

    [Fact]
    public void PersistClassification_writes_the_seeded_map()
    {
        var (_, _, _, c) = Setup();
        Scanner.PersistClassification(c, Scanner.ListClassified(c));
        Assert.True(File.Exists(c.ClassificationPath));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ScannerCoreTests"`
Expected: BUILD FAILS — `Scanner` has no `ListClassified` / `PersistClassification`.

- [ ] **Step 3: Refactor `ListWithClass` and add the new methods**

In `src/ModManager.Core/Scanner.cs`, replace the existing `ListWithClass` method:

```csharp
    private static IReadOnlyList<Mod> ListWithClass(GameContext c)
    {
        var mods = BuildModList(c);
        var map = Classification.Seed(LoadClassification(c), mods.Select(m => (m.Name, m.OnServer)));
        try { SaveClassification(c, map); } catch { /* best effort */ }
        foreach (var m in mods)
        {
            m.Class = map.TryGetValue(m.Name, out var cl) ? cl : "both";
            var v = Variant.ParseVariant(m.Name);
            m.Base = v.Base;
            m.Variant = v.Tag;
        }
        return Metadata.MergeMetadata(mods, LoadMetadata(c));
    }
```

with:

```csharp
    private static IReadOnlyList<Mod> ListWithClass(GameContext c)
    {
        var mods = BuildModList(c);
        var map = ClassifyInMemory(c, mods);
        try { SaveClassification(c, map); } catch { /* best effort */ }
        return Metadata.MergeMetadata(mods, LoadMetadata(c));
    }

    // Read-only: seed classification + set Class/Base/Variant in memory. No disk writes. Returns the seeded map.
    private static Dictionary<string, string> ClassifyInMemory(GameContext c, IReadOnlyList<Mod> mods)
    {
        var map = Classification.Seed(LoadClassification(c), mods.Select(m => (m.Name, m.OnServer)));
        foreach (var m in mods)
        {
            m.Class = map.TryGetValue(m.Name, out var cl) ? cl : "both";
            var v = Variant.ParseVariant(m.Name);
            m.Base = v.Base;
            m.Variant = v.Tag;
        }
        return map;
    }

    /// <summary>Read-only sibling of <see cref="ListWithClass"/>: scan + in-memory classify (Class/Base/Variant),
    /// no SaveClassification, no metadata merge. The shared mod-listing resolver uses this for the scanner world.</summary>
    public static IReadOnlyList<Mod> ListClassified(GameContext c)
    {
        var mods = BuildModList(c);
        ClassifyInMemory(c, mods);
        return mods;
    }

    /// <summary>Persist the auto-seeded classification exactly as <see cref="ListWithClass"/> does. Used by the
    /// App after the read-only resolver in scanner-world so per-reload persistence is byte-identical.</summary>
    public static void PersistClassification(GameContext c, IReadOnlyList<Mod> mods)
    {
        try { SaveClassification(c, Classification.Seed(LoadClassification(c), mods.Select(m => (m.Name, m.OnServer)))); }
        catch { /* best effort */ }
    }
```

- [ ] **Step 4: Run tests to verify they pass (incl. the existing `ListWithClass` test)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ScannerCoreTests"`
Expected: PASS — including the pre-existing `ListWithClass_auto_seeds_mirrored_both_clientonly_sp` (which asserts `File.Exists(c.ClassificationPath)`), proving `ListWithClass` still writes.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Scanner.cs tests/ModManager.Tests/ScannerCoreTests.cs
git commit -m "refactor(core): extract read-only ClassifyInMemory/ListClassified/PersistClassification from ListWithClass"
```

---

## Task 2: `ModEngine2Listing` Core type

**Files:**
- Create: `src/ModManager.Core/ModEngine2Listing.cs`
- Test: `tests/ModManager.Tests/ModListingTests.cs` (new file)

- [ ] **Step 1: Write the failing test**

Create `tests/ModManager.Tests/ModListingTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class ModListingTests
{
    [Fact]
    public void ModEngine2Listing_lists_config_mods_in_order_with_enabled_state()
    {
        var dir = TestSupport.TempDir("me2-");
        var configPath = Path.Combine(dir, "config_eldenring.toml");
        File.WriteAllText(configPath,
            "[extension.mod_loader]\n" +
            "mods = [\n" +
            "    { enabled = true, name = \"Alpha\", path = \"alpha\" },\n" +
            "    { enabled = false, name = \"Beta\", path = \"beta\" }\n" +
            "]\n");
        var game = new GameEntry { Id = "er", GameName = "ER", Engine = "fromsoft", ModEngineConfig = configPath };

        Assert.True(ModEngine2Listing.IsConfigBacked(game));
        var mods = ModEngine2Listing.List(game);
        Assert.Equal(new[] { "Alpha", "Beta" }, mods.Select(m => m.Name).ToArray());
        Assert.True(mods[0].Enabled);
        Assert.False(mods[1].Enabled);
        Assert.Equal("mod engine 2", mods[0].Location);
        Assert.Equal("both", mods[0].Class);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModListingTests"`
Expected: BUILD FAILS — `ModEngine2Listing` does not exist.

- [ ] **Step 3: Create the Core type**

Create `src/ModManager.Core/ModEngine2Listing.cs`:

```csharp
using System.IO;

namespace ModManager.Core;

/// <summary>
/// Read-only Mod Engine 2 listing. ME2's <c>mods[]</c> array is the source of truth for a
/// config-backed FromSoft game; this reads it as the launcher's mod list. Extracted from the
/// App's ModEngineService so the App and the headless agent-access MCP read ME2 mods through one
/// path. Parsing lives in <see cref="ModEngine2Config"/>; this resolves + maps.
/// </summary>
public static class ModEngine2Listing
{
    public static bool IsConfigBacked(GameEntry game)
        => game.Engine == "fromsoft"
           && !string.IsNullOrEmpty(game.ModEngineConfig)
           && File.Exists(game.ModEngineConfig);

    /// <summary>The config's mods as the normal mod list (priority order preserved).</summary>
    public static IReadOnlyList<Mod> List(GameEntry game)
    {
        var toml = ReadConfig(game);
        if (toml is null) return Array.Empty<Mod>();
        return ModEngine2Config.ParseMods(toml)
            .Select(m => new Mod
            {
                Name = m.Name,
                Base = m.Name,
                Class = "both",
                Enabled = m.Enabled,
                IsFolder = true,
                Location = "mod engine 2",
                Files = new List<string> { m.Path },
            })
            .ToList();
    }

    public static string? ReadConfig(GameEntry game)
    {
        try { return game.ModEngineConfig is null ? null : File.ReadAllText(game.ModEngineConfig); }
        catch { return null; }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModListingTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/ModEngine2Listing.cs tests/ModManager.Tests/ModListingTests.cs
git commit -m "feat(core): ModEngine2Listing — config-backed mod listing in Core"
```

---

## Task 3: `DirectInjectListing` Core type + shared fixture

**Files:**
- Create: `src/ModManager.Core/DirectInjectListing.cs`
- Create: `tests/ModManager.Tests/FromSoftFixture.cs`
- Test: `tests/ModManager.Tests/ModListingTests.cs` (add)

- [ ] **Step 1: Write the shared fixture + the failing test**

Create `tests/ModManager.Tests/FromSoftFixture.cs` — an on-disk FromSoft install: `Game\SeamlessCoop\` (triggers Seamless Co-op), `Game\dinput8.dll` (triggers the DLL loader), `Game\mods\AdjustTheFov.dll` (one loader-run mod), plus a pinned data dir with `metadata.json` keyed to the Seamless detection name:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

internal static class FromSoftFixture
{
    /// <summary>Builds an on-disk FromSoft game and returns its GameEntry (engine fromsoft, GameRoot +
    /// pinned DataDir). metadata.json is keyed by the Seamless detection name so MergeMetadata hits.</summary>
    public static GameEntry Build()
    {
        var root = TestSupport.TempDir("fs-");
        var play = Path.Combine(root, "Game");
        Directory.CreateDirectory(Path.Combine(play, "SeamlessCoop"));
        File.WriteAllText(Path.Combine(play, "SeamlessCoop", "seamlesscoopsettings.ini"), "x");
        File.WriteAllText(Path.Combine(play, "dinput8.dll"), "x");
        Directory.CreateDirectory(Path.Combine(play, "mods"));
        File.WriteAllText(Path.Combine(play, "mods", "AdjustTheFov.dll"), "x");

        var dataDir = Path.Combine(root, "_626mods", "er");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "metadata.json"),
            "{\"Seamless Co-op\":{\"title\":\"Seamless Co-op (Elden Ring)\"," +
            "\"author\":\"Yui\",\"url\":\"https://www.nexusmods.com/eldenring/mods/510\"}}");

        return new GameEntry
        {
            Id = "er", GameName = "ELDEN RING", Engine = "fromsoft",
            GameRoot = root, DataDir = dataDir,
        };
    }

    /// <summary>Write a games.json registry containing this game and point McpConfig at it (for MCP tests).</summary>
    public static void SeedRegistry(GameEntry game)
    {
        var dir = TestSupport.TempDir("reg-");
        var gameRoot = game.GameRoot!.Replace("\\", "/");
        var dataDir = game.DataDir!.Replace("\\", "/");
        File.WriteAllText(Path.Combine(dir, "games.json"),
            "{\"version\":1,\"activeGameId\":\"er\",\"games\":[" +
            "{\"id\":\"er\",\"gameName\":\"ELDEN RING\",\"engine\":\"fromsoft\"," +
            "\"gameRoot\":\"" + gameRoot + "\",\"dataDir\":\"" + dataDir + "\"}]}");
        ModManager.Mcp.McpConfig.DataRoot = dir;
    }
}
```

Add to `tests/ModManager.Tests/ModListingTests.cs`:

```csharp
    [Fact]
    public void DirectInjectListing_lists_seamless_and_loader_mods_and_drops_bare_loader()
    {
        var game = FromSoftFixture.Build();
        var mods = DirectInjectListing.List(game);
        var names = mods.Select(m => m.Name).ToList();

        Assert.Contains("Seamless Co-op", names);
        Assert.Contains("Adjust The Fov", names);            // loader-run DLL, prettified
        Assert.DoesNotContain("DLL mod loader", names);      // dropped: its mods\ has contents
        Assert.Equal("direct-inject", mods.First(m => m.Name == "Seamless Co-op").Location);
        Assert.Equal("co-op", mods.First(m => m.Name == "Seamless Co-op").Class);
    }

    [Fact]
    public void DirectInjectListing_keeps_bare_loader_when_mods_folder_empty()
    {
        var game = FromSoftFixture.Build();
        Directory.Delete(Path.Combine(game.GameRoot!, "Game", "mods"), recursive: true);
        var names = DirectInjectListing.List(game).Select(m => m.Name).ToList();
        Assert.Contains("DLL mod loader", names);            // no contents -> bare loader row stays
    }
```

> Note: `FromSoftFixture.SeedRegistry` references `ModManager.Mcp.McpConfig`. The test project already references `ModManager.Mcp` (see `ReadToolsTests.cs`), so this compiles.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModListingTests"`
Expected: BUILD FAILS — `DirectInjectListing` does not exist.

- [ ] **Step 3: Create the Core type**

Create `src/ModManager.Core/DirectInjectListing.cs`:

```csharp
using System.IO;

namespace ModManager.Core;

/// <summary>
/// Read-only direct-inject (FromSoft loose-file) listing. Lists enabled mods recognized in the
/// game's play folder alongside disabled ones held in the data dir. Extracted from the App's
/// DirectInjectService so the App and the headless agent-access MCP list direct-inject mods through
/// one path. Recognition + holding logic is pure/tested in <see cref="DirectInject"/>; this resolves
/// the folders and maps to <see cref="Mod"/>.
/// </summary>
public static class DirectInjectListing
{
    /// <summary>True for FromSoft games (the engine whose mods can be direct-inject).</summary>
    public static bool Applies(GameEntry game) => game.Engine == "fromsoft";

    public static IReadOnlyList<Mod> List(GameEntry game)
    {
        var folder = PlayFolder(game.GameRoot);
        return Enabled(folder).Select(d => Row(d, enabled: true))
            .Concat(DirectInject.ListDisabled(Holding(game)).Select(d => Row(d, enabled: false)))
            .ToList();
    }

    // All currently-enabled direct-inject mods: top-level signatures PLUS the individual mods a DLL
    // loader runs from its mods\ folder. When those are present the bare "DLL mod loader" row is
    // dropped — it's represented by its contents.
    public static IReadOnlyList<DirectInjectMod> Enabled(string? folder)
    {
        if (folder is null) return Array.Empty<DirectInjectMod>();
        var top = DirectInject.Detect(Names(folder, Directory.GetFiles), Names(folder, Directory.GetDirectories));

        var modsDir = Path.Combine(folder, "mods");
        var loaderMods = Directory.Exists(modsDir)
            ? DirectInject.DetectLoaderMods(Names(modsDir, Directory.GetFiles), Names(modsDir, Directory.GetDirectories))
            : Array.Empty<DirectInjectMod>();

        if (loaderMods.Count > 0) top = top.Where(m => m.Name != DirectInject.LoaderName).ToList();
        return top.Concat(loaderMods).ToList();
    }

    /// <summary>FromSoft games keep the exe + mods under a "Game" subfolder; fall back to the root.</summary>
    public static string? PlayFolder(string? gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return null;
        var game = Path.Combine(gameRoot, "Game");
        return Directory.Exists(game) ? game : gameRoot;
    }

    public static string Holding(GameEntry game) => Path.Combine(Scanner.DataDirForGame(game), "direct-disabled");

    private static Mod Row(DirectInjectMod d, bool enabled) => new()
    {
        Name = d.Name,
        Base = d.Name,
        Class = d.Kind,                 // chip: GRAPHICS / CO-OP / UPSCALER / DISPLAY / GAMEPLAY / DLL
        Location = "direct-inject",       // chip: loose-file mod, not Mod Engine 2
        Enabled = enabled,
        Description = "Detected: " + d.Evidence,
        Files = d.Entries.ToList(),
    };

    private static IReadOnlyList<string> Names(string folder, Func<string, string[]> list)
    {
        try { return list(folder).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList(); }
        catch { return Array.Empty<string>(); }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModListingTests"`
Expected: PASS (all three `ModListingTests` so far).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/DirectInjectListing.cs tests/ModManager.Tests/FromSoftFixture.cs tests/ModManager.Tests/ModListingTests.cs
git commit -m "feat(core): DirectInjectListing — direct-inject mod listing in Core"
```

---

## Task 4: `ModListing.Resolve` — the unified resolver

**Files:**
- Create: `src/ModManager.Core/ModListing.cs`
- Test: `tests/ModManager.Tests/ModListingTests.cs` (add)

- [ ] **Step 1: Write the failing tests**

Add to `tests/ModManager.Tests/ModListingTests.cs`:

```csharp
    [Fact]
    public void Resolve_fromsoft_returns_enriched_direct_inject_mods()
    {
        var game = FromSoftFixture.Build();
        var mods = ModListing.Resolve(game);
        var seamless = mods.First(m => m.Name == "Seamless Co-op");

        Assert.Equal("Seamless Co-op (Elden Ring)", seamless.DisplayName); // enriched from metadata.json
        Assert.Equal("Yui", seamless.Author);
        Assert.Equal("https://www.nexusmods.com/eldenring/mods/510", seamless.ModUrl);
        Assert.Contains(mods, m => m.Name == "Adjust The Fov");
        Assert.DoesNotContain(mods, m => m.Name == "DLL mod loader");
    }

    [Fact]
    public void Resolve_scanner_world_classifies_and_does_not_write()
    {
        var (_, _, _, c) = ScannerCoreTests.SetupPublic();
        var mods = ModListing.Resolve(c.Game);
        Assert.Equal("both", mods.First(m => m.Name == "Cool").Class); // classified
        Assert.False(File.Exists(c.ClassificationPath));               // read-only: no write
    }
```

This reuses the scanner-world fixture from `ScannerCoreTests`. Expose it: in `tests/ModManager.Tests/ScannerCoreTests.cs`, add a thin public wrapper next to `Setup()`:

```csharp
    internal static (string root, string primary, string mirror, GameContext c) SetupPublic() => Setup();
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModListingTests"`
Expected: BUILD FAILS — `ModListing` does not exist.

- [ ] **Step 3: Create the resolver**

Create `src/ModManager.Core/ModListing.cs`:

```csharp
namespace ModManager.Core;

/// <summary>
/// The single read-only mod-listing path. The App (MainViewModel) and the headless agent-access MCP
/// both call <see cref="Resolve"/> — no second source of truth. Dispatches by engine (Mod Engine 2
/// config → direct-inject → scanner), then merges per-game metadata.json. Performs NO disk writes:
/// the scanner world's classification persist + data-dir migration stay explicit App-side steps so a
/// read tool never mutates the user's install.
/// </summary>
public static class ModListing
{
    public static IReadOnlyList<Mod> Resolve(GameEntry game)
    {
        var ctx = Scanner.GameContext(game);
        // Order is load-bearing: ME2 config wins over loose direct-inject files (mirrors MainViewModel).
        IReadOnlyList<Mod> raw =
            ModEngine2Listing.IsConfigBacked(game) ? ModEngine2Listing.List(game)
            : DirectInjectListing.Applies(game)    ? DirectInjectListing.List(game)
            : Scanner.ListClassified(ctx);
        return Metadata.MergeMetadata(raw, Scanner.LoadMetadata(ctx));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModListingTests"`
Expected: PASS (all `ModListingTests`).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/ModListing.cs tests/ModManager.Tests/ModListingTests.cs tests/ModManager.Tests/ScannerCoreTests.cs
git commit -m "feat(core): ModListing.Resolve — unified read-only mod-listing resolver"
```

---

## Task 5: Point MCP `list_mods` at the resolver + enrichment

**Files:**
- Modify: `src/ModManager.Mcp/Tools/ModTools.cs` (the `ListMods` method, lines 31-46)
- Test: `tests/ModManager.Tests/Mcp/ReadToolsTests.cs` (add)

- [ ] **Step 1: Write the failing tests**

Add to `tests/ModManager.Tests/Mcp/ReadToolsTests.cs`:

```csharp
    [Fact]
    public async Task ListMods_fromsoft_returns_direct_inject_mods()
    {
        var game = FromSoftFixture.Build();
        FromSoftFixture.SeedRegistry(game);
        var json = JsonSerializer.Serialize(await ModTools.ListMods("er"));
        Assert.Contains("Seamless Co-op", json);
        Assert.Contains("Adjust The Fov", json);
        Assert.DoesNotContain("\"mods\":[]", json);
    }

    [Fact]
    public async Task ListMods_marshals_enrichment_fields()
    {
        var game = FromSoftFixture.Build();
        FromSoftFixture.SeedRegistry(game);
        var json = JsonSerializer.Serialize(await ModTools.ListMods("er"));
        Assert.Contains("\"displayTitle\":\"Seamless Co-op (Elden Ring)\"", json);
        Assert.Contains("\"author\":\"Yui\"", json);
        Assert.Contains("\"sourceUrl\":\"https://www.nexusmods.com/eldenring/mods/510\"", json);
    }
```

> Null-omission of `author`/`sourceUrl`/`loader` is the MCP SDK's runtime serializer behavior (verified live: `list_games` omitted windrose's null `engine`). It is NOT asserted here because these unit tests serialize the anonymous object with default `System.Text.Json`, which includes nulls. Assert presence + values only.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ReadToolsTests"`
Expected: FAIL — current `ListMods` calls `Scanner.BuildModListAsync`, which returns `[]` for the fixture (no matching `modLocations`); the enrichment keys are absent.

- [ ] **Step 3: Rewrite `ListMods`**

In `src/ModManager.Mcp/Tools/ModTools.cs`, replace the `ListMods` method:

```csharp
    [McpServerTool(Name = "list_mods")]
    [Description("Lists every detected mod for a game with its enabled state, class/chip, location, loader, and metadata (display title / author / source URL).")]
    public static Task<object> ListMods([Description("The game id, from list_games.")] string gameId)
    {
        var game = Find(gameId);
        if (game is null) return Task.FromResult(UnknownGame(gameId));
        var mods = ModListing.Resolve(game);
        return Task.FromResult<object>(new
        {
            gameId = game.Id,
            mods = mods.Select(m => new
            {
                name = m.Name,
                displayTitle = m.DisplayName,
                enabled = m.Enabled,
                @class = m.Class,
                location = m.Location,
                loader = m.Loader,
                author = m.Author,
                sourceUrl = m.ModUrl,
            }).ToArray(),
        });
    }
```

> The method drops `async` (the resolver is synchronous) but keeps the `Task<object>` signature via `Task.FromResult`, so the existing `await ModTools.ListMods("nope")` test still compiles. `using ModManager.Core;` is already present in this file.

- [ ] **Step 4: Run tests to verify they pass (incl. existing)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ReadToolsTests"`
Expected: PASS — including the existing `ListMods_unknown_game_returns_error_shape`.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Mcp/Tools/ModTools.cs tests/ModManager.Tests/Mcp/ReadToolsTests.cs
git commit -m "fix(mcp): list_mods uses ModListing.Resolve + metadata enrichment (fixes #86)"
```

---

## Task 6: App lists via the resolver; writes become explicit

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (lines 286-297, and delete `ReloadFromScannerAsync` at ~451-455)
- Modify: `docs/smoke-tests/pending.md`

- [ ] **Step 1: Replace the dispatch + merge block**

In `src/ModManager.App/ViewModels/MainViewModel.cs`, replace:

```csharp
            IReadOnlyList<Mod> list;
            var directInject = DirectInjectBacked;
            if (ConfigBacked) list = _me2.ListMods(_ctx.Game);
            else if (directInject) list = _direct.List(_ctx.Game);
            else list = await ReloadFromScannerAsync();

            // Always merge metadata.json onto the row list. The scanner branch did this internally too —
            // MergeMetadata is idempotent (same map → same fields), so the scanner branch's re-merge is a
            // no-op and the direct-inject + ME2 branches now pick up Nexus / CF identifies the same way
            // Windrose does. Without this, metadata.json entries written by Md5IdentifyArchivesAsync /
            // RefreshMetadataByNameAsync for fromsoft games never reach the displayed rows.
            list = Metadata.MergeMetadata(list, Scanner.LoadMetadata(_ctx));
```

with:

```csharp
            var directInject = DirectInjectBacked;
            // Scanner-world only: migrate the data dir, then list, then persist the auto-seeded
            // classification — exactly the two writes the old scanner branch did. The shared
            // read-only resolver (used by the agent-access MCP too) performs neither.
            if (!ConfigBacked && !directInject)
                await Scanner.MigrateDataDirAsync(_ctx);
            // One read-only listing path shared with the MCP: dispatch by engine (ME2 / direct-inject /
            // scanner) + merge metadata.json. See ModManager.Core.ModListing.Resolve.
            IReadOnlyList<Mod> list = ModListing.Resolve(_ctx.Game);
            if (!ConfigBacked && !directInject)
                Scanner.PersistClassification(_ctx, list);
```

- [ ] **Step 2: Delete the now-dead `ReloadFromScannerAsync`**

In the same file, delete this method (no remaining callers):

```csharp
    private async Task<IReadOnlyList<Mod>> ReloadFromScannerAsync()
    {
        await Scanner.MigrateDataDirAsync(_ctx!);
        return await Scanner.ListWithClassAsync(_ctx!);
    }
```

- [ ] **Step 3: Build the App to verify it compiles (warnings are errors)**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 warnings, 0 errors.

> If the build flags `_me2.ListMods` / `_direct.List` as unused — it won't (they're public methods; they're removed in Task 7). It also must not flag an unused `Metadata` using: `MainViewModel` still uses other `ModManager.Core` types.

- [ ] **Step 4: Add the App parity smoke entry**

Append to `docs/smoke-tests/pending.md`:

```markdown
## MCP list_mods unification — App parity (2026-05-29)

After unifying mod listing on `ModListing.Resolve`:
1. Open the App, switch to elden-ring (direct-inject: Seamless + EML installed).
   - Expected: the mod list is identical to before — Seamless Co-op + the EML-loaded DLL mods, same enabled states, same chips. No bare "DLL mod loader" row.
2. Switch to a bepinex game (e.g. R.E.P.O.) and a Mod Engine 2 game if available.
   - Expected: mod lists unchanged from before the refactor.
3. Toggle a direct-inject mod off/on.
   - Expected: still reversible (moves to holding, returns), no behavior change.
```

- [ ] **Step 5: Run the full Core suite (no regressions)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS (all tests).

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs docs/smoke-tests/pending.md
git commit -m "refactor(app): MainViewModel lists via ModListing.Resolve; writes explicit"
```

---

## Task 7: Slim the App services onto the Core helpers

**Files:**
- Modify: `src/ModManager.App/Services/DirectInjectService.cs`
- Modify: `src/ModManager.App/Services/ModEngineService.cs`

- [ ] **Step 1: Slim `DirectInjectService`**

In `src/ModManager.App/Services/DirectInjectService.cs`:

Change `Applies` to delegate:

```csharp
    /// <summary>True for FromSoft games (the engine whose mods can be direct-inject).</summary>
    public bool Applies(GameEntry game) => DirectInjectListing.Applies(game);
```

Delete the `List` method entirely:

```csharp
    public IReadOnlyList<Mod> List(GameEntry game)
    {
        var folder = PlayFolder(game.GameRoot);
        return Enabled(folder).Select(d => Row(d, enabled: true))
            .Concat(DirectInject.ListDisabled(Holding(game)).Select(d => Row(d, enabled: false)))
            .ToList();
    }
```

Replace the private `Enabled` method with a delegation:

```csharp
    private static IReadOnlyList<DirectInjectMod> Enabled(string? folder) => DirectInjectListing.Enabled(folder);
```

Replace `PlayFolder` with a delegation (keep it public — it has external callers):

```csharp
    /// <summary>FromSoft games keep the exe + mods under a "Game" subfolder; fall back to the root.</summary>
    public static string? PlayFolder(string? gameRoot) => DirectInjectListing.PlayFolder(gameRoot);
```

Replace `Holding` with a delegation:

```csharp
    private static string Holding(GameEntry game) => DirectInjectListing.Holding(game);
```

Delete the now-unused private `Row` method:

```csharp
    private static Mod Row(DirectInjectMod d, bool enabled) => new()
    {
        Name = d.Name,
        Base = d.Name,
        Class = d.Kind,
        Location = "direct-inject",
        Enabled = enabled,
        Description = "Detected: " + d.Evidence,
        Files = d.Entries.ToList(),
    };
```

Delete the now-unused private `Names` method:

```csharp
    private static IReadOnlyList<string> Names(string folder, Func<string, string[]> list)
    {
        try { return list(folder).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList(); }
        catch { return Array.Empty<string>(); }
    }
```

Leave everything else (`SeamlessNeedsLauncher`, `SeamlessFullyInstalled`, `IsSeamlessDllPresent`, `ProcessLoadProxies`, `AnyActiveProxyDll`, `Install`, `Plan`, `Execute`, `SetEnabled`) unchanged — they call the now-delegating `PlayFolder`/`Holding`/`Enabled`.

- [ ] **Step 2: Slim `ModEngineService`**

In `src/ModManager.App/Services/ModEngineService.cs`:

Change `IsConfigBacked` to delegate:

```csharp
    public bool IsConfigBacked(GameEntry game) => ModEngine2Listing.IsConfigBacked(game);
```

Delete the `ListMods` method entirely:

```csharp
    /// <summary>The config's mods as the normal mod list (priority order preserved).</summary>
    public IReadOnlyList<Mod> ListMods(GameEntry game)
    {
        var toml = ReadConfig(game);
        if (toml is null) return Array.Empty<Mod>();
        return ModEngine2Config.ParseMods(toml)
            .Select(m => new Mod
            {
                Name = m.Name,
                Base = m.Name,
                Class = "both",
                Enabled = m.Enabled,
                IsFolder = true,
                Location = "mod engine 2",
                Files = new List<string> { m.Path },
            })
            .ToList();
    }
```

Replace the private `ReadConfig` with a delegation (used by `Edit` + `Remove`):

```csharp
    private static string? ReadConfig(GameEntry game) => ModEngine2Listing.ReadConfig(game);
```

Leave `SetEnabled`, `SetAll`, `Reorder`, `Remove`, `Edit`, `Backup` unchanged.

- [ ] **Step 3: Build the App (warnings are errors)**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 4: Run the full Core suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS (all tests).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/Services/DirectInjectService.cs src/ModManager.App/Services/ModEngineService.cs
git commit -m "refactor(app): slim DirectInjectService/ModEngineService onto Core listing"
```

---

## Task 8: Final verification

**Files:** none (verification gate)

- [ ] **Step 1: Run the full Core test suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — all tests, including `ModListingTests`, `ReadToolsTests`, `ScannerCoreTests`, `DirectInjectToggleTests`, `DirectInjectLoaderTests`.

- [ ] **Step 2: Confirm Core purity held**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~CorePurityTests"`
Expected: PASS — the new Core files (`ModListing`, `DirectInjectListing`, `ModEngine2Listing`) pulled no WinUI/WinRT.

- [ ] **Step 3: Confirm the App builds clean**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 4: Manual smoke (the App parity entry from Task 6)**

Build + run the App per `docs/smoke-tests/pending.md` → confirm elden-ring's mod list is identical and toggles stay reversible. Record the result in the smoke checklist.

---

## Self-Review

**Spec coverage:**
- Whole-seam (3 worlds) → Tasks 2 (ME2), 3 (direct-inject), 4 (scanner via `ListClassified` in resolver). ✓
- Enrichment fields → Task 5 (`displayTitle`/`author`/`sourceUrl`). ✓
- Approach A dedicated `ModListing` → Task 4. ✓
- Read-only resolver; writes explicit App-side → Task 1 (`ListClassified`/`PersistClassification`) + Task 6 (gated `MigrateDataDir` + `PersistClassification`). ✓
- App services slim, single shared path → Task 7. ✓
- Acceptance: direct-inject regression (Task 4 + 5), bepinex no-regression (Task 4 `Resolve_scanner_world...` + Task 8 full suite), ME2 (Task 2), zero disk writes (Task 4 read-only test), CorePurity (Task 8), one shared path / services gone (Task 7). ✓
- Documented edge cases (missing folder, absent metadata) — covered by tolerant Core helpers; not separately tasked (no new code needed). Acceptable.

**Placeholder scan:** No TBD/TODO; every code step has full code. ✓

**Type consistency:** `ModListing.Resolve(GameEntry)`, `DirectInjectListing.{Applies,List,Enabled,PlayFolder,Holding}`, `ModEngine2Listing.{IsConfigBacked,List,ReadConfig}`, `Scanner.{ListClassified,PersistClassification}`, `Mod.{Name,DisplayName,Enabled,Class,Location,Loader,Author,ModUrl}` — names used identically across tasks. MCP keeps `Task<object>` signature. ✓
