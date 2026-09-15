# EA app games, slice one — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** College Football 27 and Madden NFL 27 are found in the EA app, offered in the discovery lane, added in one click with the right id and a writable data folder, and Play hands the launch to the EA app.

**Architecture:** Core does all parsing and every decision: the installerdata reader, the registry-entry scan, the curated match, the import plan, discovery filtering, and the no-mod-lane state. The App adds a read-only `EaLibrary` over the registry, a composite store library for discovery, and three call-site changes. The manifest gains `StoreIds.EaContentId`, and the miner learns to write it.

**Tech Stack:** .NET 10, C#, xUnit, WinUI 3, System.Xml.Linq, Microsoft.Win32.Registry.

**Spec:** `docs/superpowers/specs/2026-09-14-ea-app-games-slice-one-design.md` (approved 2026-09-14). Read it before starting any task, especially section 6a.

## Global Constraints

- Every Core behaviour change starts with a failing xUnit test in `tests/ModManager.Tests/`.
- Never run bare `dotnet test` or `dotnet build` at the repo root. Use `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` and `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.22.0`. Close the running app before building it.
- Warnings are errors in Core and tests. No WinUI or WinRT types in `src/ModManager.Core/` (`CorePurityTests`).
- camelCase JSON on disk. The new persisted field is `eaContentId`.
- Store kind string: `"ea"`. Steam stays `"steam"`.
- Launch link, exactly: `origin2://game/launch/?offerIds=<contentID>`.
- Data folder for an EA import, exactly: `<LocalApplicationData>\626mods\<manifest id>`.
- Engine for an EA import, exactly: `"frostbite"`. That preset declares **no** mod locations.
- An EA import **never** sets `SteamAppId`.
- EA installs are offered **only** when a manifest entry's `Stores.EaContentId` equals the install's content id. No name matching.
- Store facts, never policy (spec 6a). Nothing checks for `"frostbite"` or `"ea"` to decide mod behaviour; the no-mod-lane state keys on *the resolved game has no mod locations*.
- No-mod-lane sentence, verbatim: `The launcher doesn't turn mods on or off for this game yet. It tracks the game and warns about its anti-cheat. Mod tools for this game replace EA's anti-cheat launcher, which the launcher won't do.`
- Nothing reads or writes a game file, a save, or anything under `C:\Program Files\EA Games` except reading `__Installer\installerdata.xml`. Nothing starts, queries or signals the EA app or the game.
- XML parsing: `DtdProcessing = DtdProcessing.Prohibit` and `XmlResolver = null`.
- EA's real `installerdata.xml` files are never committed; tests use synthetic fixtures.
- Conventional commits ending with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

### Task 1: The manifest carries an EA content id, and the miner writes it

**Files:**
- Modify: `src/ModManager.Core/Manifest/GameManifest.cs` (`StoreIds`)
- Modify: `src/ModManager.Core/Manifest/EffectiveManifest.cs` (`MergeStores`)
- Modify: `tools/ManifestMiner/OverrideEntry.cs`, `tools/ManifestMiner/OverridesMerge.cs` (`ApplyTo`, `NewFrom`)
- Test: `tests/ModManager.Tests/Manifest/EffectiveManifestTests.cs` (append to `ManifestMergeCompletenessTests`), `tests/ModManager.Tests/Miner/OverridesMergeTests.cs`

**Interfaces:**
- Produces: `StoreIds.EaContentId` (`string?`), `OverrideEntry.EaContentId` (`string?`).

- [ ] **Step 1: Write the failing tests.** Append to `ManifestMergeCompletenessTests`:

```csharp
    [Fact]
    public void Every_store_id_survives_the_merge_in_both_directions()
    {
        // The walk above skips Stores, so a new store field was not covered. This one walks StoreIds
        // itself: the next store id cannot be dropped silently either.
        var full = new StoreIds();
        foreach (var p in typeof(StoreIds).GetProperties()) p.SetValue(full, "sample-" + p.Name);

        GameManifestEntry Merged(StoreIds embedded, StoreIds remote) => EffectiveManifest
            .Merge(new GameManifest { Games = new[] { new GameManifestEntry { Id = "g", Stores = embedded } } },
                   new GameManifest { Games = new[] { new GameManifestEntry { Id = "g", Stores = remote } } })
            .Games.Single(g => g.Id == "g");

        var fromRemote = Merged(new StoreIds(), full).Stores;
        var fromEmbedded = Merged(full, new StoreIds()).Stores;

        foreach (var p in typeof(StoreIds).GetProperties())
        {
            Assert.Equal("sample-" + p.Name, p.GetValue(fromRemote));
            Assert.Equal("sample-" + p.Name, p.GetValue(fromEmbedded));
        }
        Assert.Contains(typeof(StoreIds).GetProperties(), p => p.Name == "EaContentId");
    }
```

Append to `OverridesMergeTests`:

```csharp
    [Fact]
    public void An_override_sets_the_ea_content_id_on_a_matched_entry()
    {
        var backbone = Backbone(("madden-nfl-27", "3940610", null));
        var overrides = new[] { new OverrideEntry { SteamAppId = "3940610", EaContentId = "16425895" } };

        var e = OverridesMerge.Apply(backbone, overrides).Games.Single(g => g.Id == "madden-nfl-27");
        Assert.Equal("16425895", e.Stores.EaContentId);
        Assert.Equal("3940610", e.Stores.SteamAppId);   // the Steam id is kept, not replaced
    }

    [Fact]
    public void An_added_override_carries_its_ea_content_id()
    {
        var overrides = new[] { new OverrideEntry { Id = "some-ea-only-game", Name = "Some EA Game", EaContentId = "123" } };

        var e = OverridesMerge.Apply(Backbone(), overrides).Games.Single(g => g.Id == "some-ea-only-game");
        Assert.Equal("123", e.Stores.EaContentId);
    }
```

- [ ] **Step 2: Run and watch them fail.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestMergeCompletenessTests|FullyQualifiedName~OverridesMergeTests"`
Expected: build error — `EaContentId` does not exist on `OverrideEntry` / `StoreIds`.

- [ ] **Step 3: Implement.**

`StoreIds`:

```csharp
    /// <summary>The EA app's content id, from the game's <c>__Installer\installerdata.xml</c>
    /// <c>contentIDs/contentID</c>. The key an EA install is matched on.</summary>
    public string? EaContentId { get; init; }
```

`EffectiveManifest.MergeStores` gains `EaContentId = remote.EaContentId ?? embedded.EaContentId,`.

`OverrideEntry` gains, after `SteamAppId`:

```csharp
    /// <summary>The EA app content id, for games the EA app installs. Written into <c>Stores</c>.</summary>
    public string? EaContentId { get; init; }
```

`OverridesMerge.ApplyTo` gains `Stores = e.Stores with { EaContentId = ov.EaContentId ?? e.Stores.EaContentId },`.
`OverridesMerge.NewFrom` changes its Stores line to `Stores = new StoreIds { SteamAppId = ov.SteamAppId, EaContentId = ov.EaContentId },`.

- [ ] **Step 4: Run the suite.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/ModManager.Core/Manifest/GameManifest.cs src/ModManager.Core/Manifest/EffectiveManifest.cs tools/ManifestMiner/OverrideEntry.cs tools/ManifestMiner/OverridesMerge.cs tests/ModManager.Tests/Manifest/EffectiveManifestTests.cs tests/ModManager.Tests/Miner/OverridesMergeTests.cs
git commit -m "feat(manifest): games carry an EA app content id, and the miner writes it

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Reading EA installs — installerdata and registry entries

**Files:**
- Create: `src/ModManager.Core/Stores/EaInstallerData.cs`
- Create: `src/ModManager.Core/Stores/EaInstallScan.cs`
- Test: `tests/ModManager.Tests/Stores/EaInstallerDataTests.cs`, `tests/ModManager.Tests/Stores/EaInstallScanTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record EaInstall(IReadOnlyList<string> ContentIds, string? Title, string? GameVersion)`
  - `public static EaInstall? EaInstallerData.Parse(string? xml)`
  - `public sealed record EaRegistryInstall(string? DisplayName, string? InstallDir)`
  - `public static IReadOnlyList<InstalledGame> EaInstallScan.Scan(IEnumerable<EaRegistryInstall> entries, Func<string, string?> readText)` — `readText` returns the file's text, or null when it is missing or unreadable.
  - `public const string EaInstallScan.StoreKind = "ea"`
- Namespace for both: `ModManager.Core.Stores`.

- [ ] **Step 1: Write the failing tests.** `tests/ModManager.Tests/Stores/EaInstallerDataTests.cs`:

```csharp
using ModManager.Core.Stores;

namespace ModManager.Tests.Stores;

public class EaInstallerDataTests
{
    // Synthetic, shaped like the real DiPManifest 4.0 files. EA's own files are never committed.
    internal const string Sample = """
<?xml version="1.0" encoding="utf-8"?>
<DiPManifest version="4.0">
  <buildMetaData>
    <gameVersion version="1.0.140.17622" />
  </buildMetaData>
  <contentIDs>
    <contentID>16425899</contentID>
  </contentIDs>
  <gameTitles>
    <gameTitle locale="de_DE">EA SPORTS College Football 27 (DE)</gameTitle>
    <gameTitle locale="en_US">EA SPORTS College Football 27</gameTitle>
  </gameTitles>
  <runtime>
    <launcher uid="2-2"><name locale="en_US">EA SPORTS College Football 27</name><filePath>[HKEY_LOCAL_MACHINE\SOFTWARE\EA Sports\EA SPORTS College Football 27\Install Dir]EAAntiCheat.GameServiceLauncher.exe</filePath></launcher>
  </runtime>
</DiPManifest>
""";

    [Fact]
    public void Reads_content_ids_title_and_version()
    {
        var i = EaInstallerData.Parse(Sample)!;

        Assert.Equal(new[] { "16425899" }, i.ContentIds);
        Assert.Equal("EA SPORTS College Football 27", i.Title);       // en_US preferred over document order
        Assert.Equal("1.0.140.17622", i.GameVersion);
    }

    [Fact]
    public void Falls_back_to_the_first_title_when_there_is_no_en_US()
    {
        var xml = Sample.Replace("locale=\"en_US\"", "locale=\"fr_FR\"");
        Assert.Equal("EA SPORTS College Football 27 (DE)", EaInstallerData.Parse(xml)!.Title);
    }

    [Fact]
    public void Keeps_every_content_id_in_order_and_skips_blanks()
    {
        var xml = Sample.Replace("<contentID>16425899</contentID>",
            "<contentID>111</contentID><contentID>  </contentID><contentID>deadspace_na</contentID>");
        Assert.Equal(new[] { "111", "deadspace_na" }, EaInstallerData.Parse(xml)!.ContentIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not xml at all")]
    [InlineData("<SomethingElse><contentIDs><contentID>1</contentID></contentIDs></SomethingElse>")]
    [InlineData("<DiPManifest version=\"4.0\"><contentIDs></contentIDs></DiPManifest>")]
    [InlineData("<DiPManifest version=\"4.0\"><contentIDs><contentID>1</contentID>")]
    public void Anything_that_is_not_an_ea_install_is_null_not_an_exception(string? xml)
        => Assert.Null(EaInstallerData.Parse(xml));

    [Fact]
    public void A_doctype_with_an_external_entity_is_refused_without_resolving_it()
    {
        // Any installer can write this file. An XXE that reads a local file must not work.
        var secret = Path.Combine(Path.GetTempPath(), "ea-xxe-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(secret, "SECRET");
        try
        {
            var xml = $"""
<?xml version="1.0"?>
<!DOCTYPE DiPManifest [ <!ENTITY x SYSTEM "file:///{secret.Replace('\\', '/')}"> ]>
<DiPManifest version="4.0"><contentIDs><contentID>&x;</contentID></contentIDs></DiPManifest>
""";
            var i = EaInstallerData.Parse(xml);
            Assert.True(i is null || !i.ContentIds.Contains("SECRET"));
        }
        finally { File.Delete(secret); }
    }
}
```

`tests/ModManager.Tests/Stores/EaInstallScanTests.cs`:

```csharp
using ModManager.Core.Stores;

namespace ModManager.Tests.Stores;

public class EaInstallScanTests
{
    private static string Xml(string contentId, string title, string version = "1.0.0.1") =>
        EaInstallerDataTests.Sample
            .Replace("16425899", contentId)
            .Replace(">EA SPORTS College Football 27<", ">" + title + "<")
            .Replace("1.0.140.17622", version);

    private static Func<string, string?> Files(Dictionary<string, string> byPath)
        => p => byPath.TryGetValue(p, out var t) ? t : null;

    [Fact]
    public void An_install_with_readable_installerdata_becomes_an_installed_game()
    {
        var dir = Path.Combine("C:", "EA Games", "Madden NFL 27");
        var games = EaInstallScan.Scan(
            new[] { new EaRegistryInstall("Madden NFL 27", dir + Path.DirectorySeparatorChar) },
            Files(new() { [Path.Combine(dir, "__Installer", "installerdata.xml")] = Xml("16425895", "Madden NFL 27", "1.0.139.61898") }));

        var g = Assert.Single(games);
        Assert.Equal("ea", g.StoreKind);
        Assert.Equal("16425895", g.AppId);
        Assert.Equal("Madden NFL 27", g.Name);
        Assert.Equal(dir, g.InstallDir);                 // trailing separator trimmed
        Assert.Equal("1.0.139.61898", g.BuildId);
    }

    [Fact]
    public void A_staged_or_foreign_install_is_skipped()
    {
        var games = EaInstallScan.Scan(
            new[]
            {
                new EaRegistryInstall("No folder", null),
                new EaRegistryInstall("No installerdata", Path.Combine("C:", "EA Games", "Half")),
                new EaRegistryInstall("Garbage", Path.Combine("C:", "EA Games", "Bad")),
            },
            Files(new() { [Path.Combine("C:", "EA Games", "Bad", "__Installer", "installerdata.xml")] = "<nope/>" }));

        Assert.Empty(games);
    }

    [Fact]
    public void The_same_folder_seen_twice_is_listed_once()
    {
        // The 64- and 32-bit registry views can both carry the same game.
        var dir = Path.Combine("C:", "EA Games", "X");
        var entries = new[] { new EaRegistryInstall("X", dir), new EaRegistryInstall("X", dir.ToUpperInvariant()) };
        var games = EaInstallScan.Scan(entries,
            p => p.EndsWith("installerdata.xml", StringComparison.OrdinalIgnoreCase) ? Xml("1", "X") : null);

        Assert.Single(games);
    }

    [Fact]
    public void A_missing_title_falls_back_to_the_registry_display_name()
    {
        var dir = Path.Combine("C:", "EA Games", "Y");
        var noTitle = "<DiPManifest version=\"4.0\"><contentIDs><contentID>7</contentID></contentIDs></DiPManifest>";
        var g = Assert.Single(EaInstallScan.Scan(new[] { new EaRegistryInstall("Display Y", dir) }, _ => noTitle));
        Assert.Equal("Display Y", g.Name);
    }
}
```

- [ ] **Step 2: Run and watch them fail.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModManager.Tests.Stores"`
Expected: build error — `ModManager.Core.Stores` does not exist.

- [ ] **Step 3: Implement.** `src/ModManager.Core/Stores/EaInstallerData.cs`:

```csharp
using System.Xml;
using System.Xml.Linq;

namespace ModManager.Core.Stores;

/// <summary>What the EA app records about one install, read from its <c>__Installer\installerdata.xml</c>.</summary>
public sealed record EaInstall(IReadOnlyList<string> ContentIds, string? Title, string? GameVersion);

/// <summary>
/// Reads the EA app's per-install manifest (<c>DiPManifest</c>). Pure: it takes the file's text.
/// Any installer can write that file, so DTDs are prohibited and no external resource is resolved.
/// Anything that is not a DiPManifest with at least one content id is "not an EA install" — null,
/// never an exception.
/// </summary>
public static class EaInstallerData
{
    public static EaInstall? Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            doc = XDocument.Load(reader);
        }
        catch { return null; }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "DiPManifest") return null;

        var ids = root.Elements("contentIDs").Elements("contentID")
            .Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();
        if (ids.Count == 0) return null;

        var titles = root.Elements("gameTitles").Elements("gameTitle").ToList();
        var title = (titles.FirstOrDefault(t => (string?)t.Attribute("locale") == "en_US") ?? titles.FirstOrDefault())
            ?.Value.Trim();
        var version = (string?)root.Element("buildMetaData")?.Element("gameVersion")?.Attribute("version");

        return new EaInstall(ids, string.IsNullOrEmpty(title) ? null : title, string.IsNullOrWhiteSpace(version) ? null : version);
    }
}
```

`src/ModManager.Core/Stores/EaInstallScan.cs`:

```csharp
namespace ModManager.Core.Stores;

/// <summary>One EA install as the registry names it: <c>HKLM\SOFTWARE\&lt;publisher&gt;\&lt;title&gt;</c>,
/// values <c>DisplayName</c> and <c>Install Dir</c>.</summary>
public sealed record EaRegistryInstall(string? DisplayName, string? InstallDir);

/// <summary>
/// Turns registry entries into installed games. Pure: the App reads the registry and passes a file
/// reader. An entry counts only when its installerdata can be read and parses — a staged or partial
/// install is skipped. The same folder seen under both registry views is listed once.
/// </summary>
public static class EaInstallScan
{
    public const string StoreKind = "ea";

    public static IReadOnlyList<InstalledGame> Scan(IEnumerable<EaRegistryInstall> entries, Func<string, string?> readText)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<InstalledGame>();
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.InstallDir)) continue;
            var dir = e.InstallDir.Trim().TrimEnd('\\', '/');
            if (dir.Length == 0 || !seen.Add(dir)) continue;

            string? text;
            try { text = readText(Path.Combine(dir, "__Installer", "installerdata.xml")); }
            catch { continue; }
            if (EaInstallerData.Parse(text) is not { } install) continue;

            var name = install.Title ?? e.DisplayName ?? Path.GetFileName(dir);
            result.Add(new InstalledGame(StoreKind, install.ContentIds[0], name, dir) { BuildId = install.GameVersion });
        }
        return result;
    }
}
```

- [ ] **Step 4: Run the tests and the suite.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/ModManager.Core/Stores tests/ModManager.Tests/Stores
git commit -m "feat(stores): read EA app installs from installerdata and registry entries

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: A registration can carry an EA id, a launch link, a data folder, and no mod locations

**Files:**
- Modify: `src/ModManager.Core/GameEntry.cs` (`GameEntry`, `GameInput`)
- Modify: `src/ModManager.Core/EnginePresets.cs` (`Presets`, `BuildGameEntry`)
- Test: `tests/ModManager.Tests/GameEntryCloneTests.cs` (add `EaContentId` to `Populated()`), `tests/ModManager.Tests/Stores/EaRegistrationTests.cs` (new)

**Interfaces:**
- Produces: `GameEntry.EaContentId` (`string?`); `GameInput.EaContentId`, `GameInput.LaunchUrl`, `GameInput.DataDir` (all `string?`); preset key `"frostbite"`.

- [ ] **Step 1: Write the failing tests.** In `GameEntryCloneTests.Populated()`, add `EaContentId = "16425895",` beside `SteamAppId`. Create `tests/ModManager.Tests/Stores/EaRegistrationTests.cs`:

```csharp
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Persistence;

namespace ModManager.Tests.Stores;

public class EaRegistrationTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-ea-reg-");
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static GameInput Input(string root) => new()
    {
        Id = "madden-nfl-27", Name = "Madden NFL 27", Engine = "frostbite", GameRoot = root,
        EaContentId = "16425895",
        LaunchUrl = "origin2://game/launch/?offerIds=16425895",
        DataDir = Path.Combine("L", "626mods", "madden-nfl-27"),
    };

    [Fact]
    public void The_input_lands_on_the_entry_with_no_steam_id_and_no_mod_locations()
    {
        var e = EnginePresets.BuildGameEntry(Input(_root), existingIds: null);

        Assert.Equal("madden-nfl-27", e.Id);
        Assert.Equal("frostbite", e.Engine);
        Assert.Equal("16425895", e.EaContentId);
        Assert.Equal("origin2://game/launch/?offerIds=16425895", e.LaunchUrl);
        Assert.Equal(Path.Combine("L", "626mods", "madden-nfl-27"), e.DataDir);
        Assert.Null(e.SteamAppId);
        Assert.Empty(e.ModLocations);
    }

    [Fact]
    public void A_steam_input_still_derives_its_steam_link_and_one_location()
    {
        var e = EnginePresets.BuildGameEntry(new GameInput { Name = "Palworld", Engine = "custom", GameRoot = _root, SteamAppId = "1623730" }, null);
        Assert.Equal("steam://rungameid/1623730", e.LaunchUrl);
        Assert.Single(e.ModLocations);
        Assert.Null(e.EaContentId);
    }

    [Fact]
    public void The_ea_id_round_trips_through_the_registry_as_camelCase()
    {
        var entry = EnginePresets.BuildGameEntry(Input(_root), null);
        RegistryStore.Save(_root, Registry.UpsertGame(Registry.EmptyRegistry(), entry));

        var json = File.ReadAllText(RegistryStore.PathFor(_root));
        Assert.Contains("\"eaContentId\"", json);
        Assert.DoesNotContain("\"EaContentId\"", json);
        var back = RegistryStore.Load(_root).Games.Single();
        Assert.Equal("16425895", back.EaContentId);
        Assert.Equal(entry.DataDir, back.DataDir);
    }

    [Fact]
    public void A_game_with_no_mod_locations_lists_no_mods_and_does_not_throw()
    {
        var root = Path.Combine(_root, "Madden NFL 27");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Madden27.exe"), "game");
        var entry = EnginePresets.BuildGameEntry(Input(root), null);
        entry.DataDir = Path.Combine(_root, "data");

        Assert.Empty(ModListing.Resolve(entry));
        Assert.Empty(Scanner.GameContext(entry).Locations);
    }
}
```

- [ ] **Step 2: Run and watch them fail.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EaRegistrationTests|FullyQualifiedName~GameEntryCloneTests"`
Expected: build error — `EaContentId`, `LaunchUrl` and `DataDir` do not exist on `GameInput`, and `EaContentId` does not exist on `GameEntry`.

- [ ] **Step 3: Implement.**

`GameEntry`, directly after `SteamAppId`:

```csharp
    /// <summary>The EA app content id, for a game added from the EA app. Its store key, as SteamAppId is
    /// Steam's. Never set together with a Steam launch.</summary>
    public string? EaContentId { get; set; }
```

`GameInput` gains:

```csharp
    public string? EaContentId { get; init; }
    /// <summary>An explicit launch link. Wins over the one derived from SteamAppId.</summary>
    public string? LaunchUrl { get; init; }
    /// <summary>Where the launcher keeps this game's data, when the default beside the game root is not
    /// writable (for example under Program Files).</summary>
    public string? DataDir { get; init; }
```

`EnginePresets.Presets` gains:

```csharp
        ["frostbite"] = new("Frostbite (EA)", Array.Empty<string>(), "filename_no_ext", "",
            "EA SPORTS College Football, Madden. No mod locations yet: the launcher tracks these games and hands Play to the EA app."),
```

In `BuildGameEntry`, replace the `ModLocations = new[] { modLocation },` initialiser line with `ModLocations = string.IsNullOrEmpty(input.ModPath) && string.IsNullOrEmpty(preset.ModPath) ? Array.Empty<ModLocation>() : new[] { modLocation },` and, after the existing SteamAppId block, add:

```csharp
        if (!string.IsNullOrEmpty(input.EaContentId)) entry.EaContentId = input.EaContentId;
        if (!string.IsNullOrEmpty(input.LaunchUrl)) entry.LaunchUrl = input.LaunchUrl;
        if (!string.IsNullOrEmpty(input.DataDir)) entry.DataDir = input.DataDir;
```

If `Every_preset_has_the_required_shape` (or a similar preset-shape test) rejects an empty `ModPath`, extend that test's rule to allow an empty path **only** when the preset's note says there are no mod locations, rather than giving the preset a fake path, and say so in the report.

- [ ] **Step 4: Run the suite.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS. If `ModListing.Resolve` or `Scanner.GameContext` throws for zero locations, fix the throwing site with a null-safe check (`Locations.FirstOrDefault()` and an early empty return), keep the fix minimal, and name the site in the report.

- [ ] **Step 5: Commit.**

```bash
git add src/ModManager.Core/GameEntry.cs src/ModManager.Core/EnginePresets.cs tests/ModManager.Tests/GameEntryCloneTests.cs tests/ModManager.Tests/Stores/EaRegistrationTests.cs
git commit -m "feat(registry): a game can carry an EA id, an explicit launch link and data folder, and no mod locations

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: The curated match, the import plan, and discovery

**Files:**
- Create: `src/ModManager.Core/Stores/EaGameImport.cs`
- Create: `src/ModManager.Core/Stores/StoreDiscovery.cs`
- Test: `tests/ModManager.Tests/Stores/EaGameImportTests.cs`, `tests/ModManager.Tests/Stores/StoreDiscoveryTests.cs`

**Interfaces:**
- Consumes: `StoreIds.EaContentId` (Task 1), `EaInstallScan.StoreKind` (Task 2), `GameInput.EaContentId/LaunchUrl/DataDir`, preset `"frostbite"`, `GameEntry.EaContentId` (Task 3), `BanRiskCatalog.Effective(GameEntry)`.
- Produces:
  - `public static string EaGameImport.LaunchUrlFor(string contentId)`
  - `public static GameManifestEntry? EaGameImport.Match(InstalledGame install, IEnumerable<GameManifestEntry> manifestGames)`
  - `public static GameInput? EaGameImport.Plan(InstalledGame install, IEnumerable<GameManifestEntry> manifestGames, string localDataRoot)`
  - `public static IReadOnlyList<InstalledGame> StoreDiscovery.Offerable(IEnumerable<InstalledGame> installed, IEnumerable<GameEntry> registered, IEnumerable<GameManifestEntry> manifestGames)`

- [ ] **Step 1: Write the failing tests.** `tests/ModManager.Tests/Stores/EaGameImportTests.cs`:

```csharp
using ModManager.Core;
using ModManager.Core.Manifest;
using ModManager.Core.Stores;

namespace ModManager.Tests.Stores;

[Collection("ManifestState")]
public class EaGameImportTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    private static readonly GameManifestEntry[] Manifest =
    {
        new() { Id = "ea-sports-college-football-27", Name = "EA SPORTS College Football 27", Stores = new StoreIds { SteamAppId = "4032350", EaContentId = "16425899" }, BanRisk = "high" },
        new() { Id = "madden-nfl-27", Name = "Madden NFL 27", Stores = new StoreIds { SteamAppId = "3940610", EaContentId = "16425895" }, BanRisk = "high" },
    };

    private static InstalledGame Ea(string id, string name = "Whatever") =>
        new("ea", id, name, Path.Combine("C:", "Program Files", "EA Games", name)) { BuildId = "1.0.0.1" };

    [Fact]
    public void A_matching_content_id_plans_the_manifest_game()
    {
        var input = EaGameImport.Plan(Ea("16425895", "Madden NFL 27"), Manifest, Path.Combine("L"))!;

        Assert.Equal("madden-nfl-27", input.Id);
        Assert.Equal("Madden NFL 27", input.Name);
        Assert.Equal("frostbite", input.Engine);
        Assert.Equal(Path.Combine("C:", "Program Files", "EA Games", "Madden NFL 27"), input.GameRoot);
        Assert.Equal("16425895", input.EaContentId);
        Assert.Equal("origin2://game/launch/?offerIds=16425895", input.LaunchUrl);
        Assert.Equal(Path.Combine("L", "626mods", "madden-nfl-27"), input.DataDir);
        Assert.Null(input.SteamAppId);
    }

    [Fact]
    public void No_content_id_match_means_no_plan_even_when_the_name_matches()
        => Assert.Null(EaGameImport.Plan(Ea("99999999", "Madden NFL 27"), Manifest, "L"));

    [Fact]
    public void A_steam_install_is_never_planned_as_an_ea_game()
        => Assert.Null(EaGameImport.Plan(new InstalledGame("steam", "16425895", "Madden NFL 27", "C:"), Manifest, "L"));

    [Theory]
    [InlineData("16425899")]
    [InlineData("16425895")]
    public void The_entry_an_ea_import_builds_resolves_high_ban_risk_with_or_without_a_feed(string contentId)
    {
        var entry = EnginePresets.BuildGameEntry(EaGameImport.Plan(Ea(contentId), Manifest, "L")!, null);

        EffectiveManifest.SetRemote(null);
        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(entry));
        EffectiveManifest.SetRemote(new GameManifest { Games = Manifest });
        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(entry));
    }
}
```

`tests/ModManager.Tests/Stores/StoreDiscoveryTests.cs`:

```csharp
using ModManager.Core;
using ModManager.Core.Manifest;
using ModManager.Core.Stores;

namespace ModManager.Tests.Stores;

public class StoreDiscoveryTests
{
    private static readonly GameManifestEntry[] Manifest =
    {
        new() { Id = "madden-nfl-27", Name = "Madden NFL 27", Stores = new StoreIds { EaContentId = "16425895" } },
    };

    private static InstalledGame Steam(string id) => new("steam", id, "S" + id, "C:");
    private static InstalledGame Ea(string id) => new("ea", id, "E" + id, "C:");

    [Fact]
    public void Steam_games_behave_as_they_did()
    {
        var registered = new[] { new GameEntry { Id = "a", SteamAppId = "1" } };
        var offered = StoreDiscovery.Offerable(new[] { Steam("1"), Steam("2") }, registered, Manifest);
        Assert.Equal(new[] { "2" }, offered.Select(g => g.AppId));
    }

    [Fact]
    public void A_curated_ea_game_is_offered_until_it_is_registered()
    {
        Assert.Single(StoreDiscovery.Offerable(new[] { Ea("16425895") }, Array.Empty<GameEntry>(), Manifest));

        var registered = new[] { new GameEntry { Id = "madden-nfl-27", EaContentId = "16425895" } };
        Assert.Empty(StoreDiscovery.Offerable(new[] { Ea("16425895") }, registered, Manifest));
    }

    [Fact]
    public void An_uncurated_ea_game_is_not_offered()
        => Assert.Empty(StoreDiscovery.Offerable(new[] { Ea("555") }, Array.Empty<GameEntry>(), Manifest));

    [Fact]
    public void The_store_kind_is_part_of_the_key()
    {
        // An EA content id and a Steam app id can be the same digits. Neither may hide the other.
        var registeredEa = new[] { new GameEntry { Id = "madden-nfl-27", EaContentId = "16425895" } };
        Assert.Single(StoreDiscovery.Offerable(new[] { Steam("16425895") }, registeredEa, Manifest));

        var registeredSteam = new[] { new GameEntry { Id = "x", SteamAppId = "16425895" } };
        Assert.Single(StoreDiscovery.Offerable(new[] { Ea("16425895") }, registeredSteam, Manifest));
    }
}
```

- [ ] **Step 2: Run and watch them fail.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EaGameImportTests|FullyQualifiedName~StoreDiscoveryTests"`
Expected: build error — `EaGameImport` and `StoreDiscovery` do not exist.

- [ ] **Step 3: Implement.** `src/ModManager.Core/Stores/EaGameImport.cs`:

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Core.Stores;

/// <summary>
/// Turns an EA app install into a registration — only when the manifest knows the game.
///
/// <para><b>Curated only, on purpose.</b> Ban risk resolves by manifest id (and a compiled floor for the
/// two EA football titles). An EA install registered under a slug of its display name would read no risk,
/// and the EA catalogue is full of anti-cheat games. So the match is on <c>Stores.EaContentId</c> and
/// nothing else — never the name. Growing EA coverage is a data PR.</para>
///
/// <para><b>Facts, not policy.</b> The plan records what the game is (engine <c>frostbite</c>, its content
/// id, where it lives) and never that it has "no mods": a later mod lane is a preset or manifest change
/// that reaches the registered game without a migration.</para>
/// </summary>
public static class EaGameImport
{
    public static string LaunchUrlFor(string contentId) => "origin2://game/launch/?offerIds=" + contentId;

    public static GameManifestEntry? Match(InstalledGame install, IEnumerable<GameManifestEntry> manifestGames)
        => install.StoreKind == EaInstallScan.StoreKind && !string.IsNullOrWhiteSpace(install.AppId)
            ? manifestGames.FirstOrDefault(g => string.Equals(g.Stores.EaContentId, install.AppId, StringComparison.Ordinal))
            : null;

    public static GameInput? Plan(InstalledGame install, IEnumerable<GameManifestEntry> manifestGames, string localDataRoot)
    {
        if (Match(install, manifestGames) is not { } game) return null;
        return new GameInput
        {
            Id = game.Id,
            Name = game.Name,
            Engine = "frostbite",
            GameRoot = install.InstallDir,
            EaContentId = install.AppId,
            LaunchUrl = LaunchUrlFor(install.AppId),
            // Beside the game root is C:\Program Files\EA Games\_626mods, which an unelevated launcher
            // cannot write. The explicit DataDir override is honoured everywhere.
            DataDir = Path.Combine(localDataRoot, "626mods", game.Id),
            // SteamAppId deliberately unset: it would route Play through steam://.
        };
    }
}
```

Note: `LaunchUrlFor` uses the id as-is, because content ids are digits or plain slugs such as `deadspace_na`. If a reviewer asks for escaping, use `Uri.EscapeDataString` and update the test's expected string to match.

`src/ModManager.Core/Stores/StoreDiscovery.cs`:

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Core.Stores;

/// <summary>Which installed games the discovery lane offers: not already registered, keyed by store, and
/// for EA only the ones the manifest knows (see <see cref="EaGameImport"/>).</summary>
public static class StoreDiscovery
{
    public static IReadOnlyList<InstalledGame> Offerable(
        IEnumerable<InstalledGame> installed, IEnumerable<GameEntry> registered, IEnumerable<GameManifestEntry> manifestGames)
    {
        var reg = registered.ToList();
        var steamIds = reg.Where(g => !string.IsNullOrEmpty(g.SteamAppId)).Select(g => g.SteamAppId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eaIds = reg.Where(g => !string.IsNullOrEmpty(g.EaContentId)).Select(g => g.EaContentId!)
            .ToHashSet(StringComparer.Ordinal);
        var games = manifestGames.ToList();

        return installed.Where(ig => ig.StoreKind == EaInstallScan.StoreKind
                ? !eaIds.Contains(ig.AppId) && EaGameImport.Match(ig, games) is not null
                : !steamIds.Contains(ig.AppId))
            .ToList();
    }
}
```

- [ ] **Step 4: Run the suite.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/ModManager.Core/Stores/EaGameImport.cs src/ModManager.Core/Stores/StoreDiscovery.cs tests/ModManager.Tests/Stores/EaGameImportTests.cs tests/ModManager.Tests/Stores/StoreDiscoveryTests.cs
git commit -m "feat(stores): curated EA games plan into a registration and show in discovery

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: A game with no mod locations says so, refuses drops, and refuses toggles

**Files:**
- Modify: `src/ModManager.Core/ModListing.cs` (add `HasNoModLane`)
- Modify: `src/ModManager.Core/SayWhatYouCanDo.cs` (`ModListEmptyState`)
- Modify: `src/ModManager.Core/ModToggle.cs` (refuse at the top of `SetEnabledAsync`)
- Test: `tests/ModManager.Tests/SayWhatYouCanDoTests.cs` (append to `ModListEmptyStateTests`), `tests/ModManager.Tests/Stores/NoModLaneTests.cs` (new)

**Interfaces:**
- Consumes: preset `"frostbite"` with no locations (Task 3).
- Produces:
  - `public static bool ModListing.HasNoModLane(GameContext ctx)`
  - `public const string ModListEmptyState.NoModLane` (the verbatim sentence)
  - `ModListEmptyState.MessageFor(bool hasGame, int totalRows, int visibleRows, string? search, string? mode, bool noModLane = false)`

- [ ] **Step 1: Write the failing tests.** Append to `ModListEmptyStateTests`:

```csharp
    [Fact]
    public void A_game_with_no_mod_lane_says_so_and_offers_no_drop()
    {
        var m = ModListEmptyState.MessageFor(true, 0, 0, null, null, noModLane: true)!;

        Assert.Equal(ModListEmptyState.NoModLane, m);
        Assert.DoesNotContain("Drop", m);
        Assert.DoesNotContain("+ Add mods", m);
    }

    [Fact]
    public void The_no_mod_lane_sentence_is_the_one_the_spec_fixed()
        => Assert.Equal(
            "The launcher doesn't turn mods on or off for this game yet. It tracks the game and warns about its anti-cheat. Mod tools for this game replace EA's anti-cheat launcher, which the launcher won't do.",
            ModListEmptyState.NoModLane);
```

`tests/ModManager.Tests/Stores/NoModLaneTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests.Stores;

public class NoModLaneTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-nolane-");
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private GameEntry Frostbite()
    {
        var root = Path.Combine(_root, "Madden NFL 27");
        Directory.CreateDirectory(root);
        var e = EnginePresets.BuildGameEntry(new GameInput { Id = "madden-nfl-27", Name = "Madden NFL 27", Engine = "frostbite", GameRoot = root }, null);
        e.DataDir = Path.Combine(_root, "data");
        return e;
    }

    [Fact]
    public void A_game_with_no_mod_locations_has_no_mod_lane()
        => Assert.True(ModListing.HasNoModLane(Scanner.GameContext(Frostbite())));

    [Fact]
    public void The_same_game_with_a_location_behaves_like_any_other_game()
    {
        // Facts, not policy: nothing keys on the engine name or the store.
        var e = Frostbite();
        e.ModLocations = new[] { new ModLocation("mods", "mods", "mods") };
        Assert.False(ModListing.HasNoModLane(Scanner.GameContext(e)));
    }

    [Fact]
    public void Lanes_that_do_not_use_locations_are_not_mistaken_for_none()
    {
        var fromsoft = new GameEntry { Id = "er", Engine = "fromsoft", GameRoot = _root, DataDir = Path.Combine(_root, "d2") };
        Assert.False(ModListing.HasNoModLane(Scanner.GameContext(fromsoft)));
    }

    [Fact]
    public async Task A_toggle_on_a_game_with_no_mod_lane_is_refused_with_the_sentence()
    {
        var ctx = Scanner.GameContext(Frostbite());
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ModToggle.SetEnabledAsync(ctx, new Mod { Name = "anything", Location = "mods" }, false));
        Assert.Equal(ModListEmptyState.NoModLane, e.Message);
    }
}
```

If `Mod` has required members that the initialiser above does not set, set them to the simplest valid values and say so in the report.

- [ ] **Step 2: Run and watch them fail.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModListEmptyStateTests|FullyQualifiedName~NoModLaneTests"`
Expected: build error — `NoModLane`, `HasNoModLane` and the `noModLane` parameter do not exist.

- [ ] **Step 3: Implement.**

`ModListing`:

```csharp
    /// <summary>True when the resolved game has nowhere mods could live: the folder lane, with no mod
    /// locations. A STATE, not a store or engine check (spec 6a) — the moment a location exists this is
    /// false, so a later mod lane needs no migration. Direct-inject, Mod Engine 2 and loose-root games
    /// do not use locations this way and are never "no lane".</summary>
    public static bool HasNoModLane(GameContext ctx)
        => ctx.Locations.Count == 0 && MechanismFor(ctx.Game, ctx) == ListingMechanism.Scanner;
```

`ModListEmptyState`: add the constant, add the optional parameter, and return the sentence first when it applies:

```csharp
    /// <summary>Said where the list would otherwise invite a drop: this game has no mod lane.</summary>
    public const string NoModLane =
        "The launcher doesn't turn mods on or off for this game yet. It tracks the game and warns about its anti-cheat. "
        + "Mod tools for this game replace EA's anti-cheat launcher, which the launcher won't do.";

    public static string? MessageFor(bool hasGame, int totalRows, int visibleRows, string? search, string? mode, bool noModLane = false)
    {
        if (!hasGame) return "No game registered yet. Add one with + Game.";
        if (visibleRows > 0) return null;
        if (noModLane && totalRows == 0) return NoModLane;
        // ... the existing body continues unchanged from `var query = ...`
```

`ModToggle.SetEnabledAsync`, as its first statements after `var game = ctx.Game;`:

```csharp
        // A game with nowhere for mods to live has no lane to route to. Refuse in words, before any
        // lane could index an empty location list.
        if (ModListing.HasNoModLane(ctx))
            throw new InvalidOperationException(ModListEmptyState.NoModLane);
```

- [ ] **Step 4: Run the suite.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/ModManager.Core/ModListing.cs src/ModManager.Core/SayWhatYouCanDo.cs src/ModManager.Core/ModToggle.cs tests/ModManager.Tests/SayWhatYouCanDoTests.cs tests/ModManager.Tests/Stores/NoModLaneTests.cs
git commit -m "feat(mod-list): a game with no mod lane says so and refuses toggles

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: The App finds EA games, offers them, adds them, and refuses drops

**Files:**
- Create: `src/ModManager.App/Services/EaLibrary.cs`
- Create: `src/ModManager.App/Services/CompositeStoreLibrary.cs`
- Modify: `src/ModManager.App/App.xaml.cs` (DI, ~line 41-42)
- Modify: `src/ModManager.App/ViewModels/LibraryViewModel.cs` (`RebuildDiscovery`, ~518-535)
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (`AddDiscoveredGameAsync`, ~414-430)
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (`RefreshEmptyState` ~1277; `AddModsAsync` ~3695)

App layer, headless-untestable. Policy is covered in Core by Tasks 1-5. Verification is the App build, the full suite, and the live checks in Task 7.

**Interfaces:**
- Consumes: `EaInstallScan.Scan`, `EaRegistryInstall`, `EaInstallScan.StoreKind`, `EaGameImport.Plan`, `StoreDiscovery.Offerable`, `ModListing.HasNoModLane`, `ModListEmptyState.NoModLane` and the `noModLane` parameter, `EffectiveManifest.Current.Games`.
- Produces: `EaLibrary : IStoreLibrary`, `CompositeStoreLibrary : IStoreLibrary`.

- [ ] **Step 1: `EaLibrary`.**

```csharp
using System.IO;
using Microsoft.Win32;
using ModManager.Core;
using ModManager.Core.Stores;

namespace ModManager.App.Services;

/// <summary>
/// EA app installs, read from the registry and each install's installerdata. Read-only: registry reads
/// and one file read per install. It never writes a key, never starts or queries the EA app, and returns
/// an empty list on any failure. Parsing and the install rule live in Core (<see cref="EaInstallScan"/>).
/// </summary>
public sealed class EaLibrary : IStoreLibrary
{
    // The registry parent is the publisher, and varies by title.
    private static readonly string[] Publishers = { "EA Sports", "EA Games", "Electronic Arts" };

    public string StoreKind => EaInstallScan.StoreKind;

    public IReadOnlyList<InstalledGame> InstalledGames()
    {
        try { return EaInstallScan.Scan(RegistryInstalls(), ReadText); }
        catch { return Array.Empty<InstalledGame>(); }
    }

    // No local EA art cache was found.
    public string? ResolveCoverArtPath(string appId) => null;
    public string? ResolveCoverArtPath(string appId, CoverShape shape) => null;

    private static IEnumerable<EaRegistryInstall> RegistryInstalls()
    {
        var found = new List<EaRegistryInstall>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                foreach (var publisher in Publishers)
                {
                    using var parent = hklm.OpenSubKey(@"SOFTWARE\" + publisher);
                    if (parent is null) continue;
                    foreach (var title in parent.GetSubKeyNames())
                    {
                        try
                        {
                            using var k = parent.OpenSubKey(title);
                            found.Add(new EaRegistryInstall(k?.GetValue("DisplayName") as string, k?.GetValue("Install Dir") as string));
                        }
                        catch { /* one unreadable key does not hide the rest */ }
                    }
                }
            }
            catch { /* view unavailable */ }
        }
        return found;
    }

    private static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }
}
```

- [ ] **Step 2: `CompositeStoreLibrary`.**

```csharp
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>Every store library as one, for the surfaces that are store-agnostic (the discovery lane,
/// covers, last-played). Code that is genuinely about Steam keeps taking <see cref="SteamService"/>.</summary>
public sealed class CompositeStoreLibrary : IStoreLibrary
{
    private readonly IReadOnlyList<IStoreLibrary> _libraries;
    public CompositeStoreLibrary(IEnumerable<IStoreLibrary> libraries) => _libraries = libraries.ToList();

    public string StoreKind => "all";

    public IReadOnlyList<InstalledGame> InstalledGames()
    {
        var all = new List<InstalledGame>();
        foreach (var lib in _libraries)
        {
            try { all.AddRange(lib.InstalledGames()); }
            catch { /* one failing store never hides another */ }
        }
        return all;
    }

    public string? ResolveCoverArtPath(string appId)
        => _libraries.Select(l => l.ResolveCoverArtPath(appId)).FirstOrDefault(p => p is not null);

    public string? ResolveCoverArtPath(string appId, CoverShape shape)
        => _libraries.Select(l => l.ResolveCoverArtPath(appId, shape)).FirstOrDefault(p => p is not null);
}
```

- [ ] **Step 3: DI.** In `App.xaml.cs`, replace `services.AddSingleton<IStoreLibrary>(sp => sp.GetRequiredService<SteamService>());` with:

```csharp
                services.AddSingleton<EaLibrary>();
                // Store-agnostic surfaces (discovery, covers, last-played) see every store. Steam-specific
                // code keeps resolving SteamService directly.
                services.AddSingleton<IStoreLibrary>(sp => new CompositeStoreLibrary(new IStoreLibrary[]
                {
                    sp.GetRequiredService<SteamService>(),
                    sp.GetRequiredService<EaLibrary>(),
                }));
```

- [ ] **Step 4: Discovery.** Replace the body of `LibraryViewModel.RebuildDiscovery` with:

```csharp
    private void RebuildDiscovery(IReadOnlyList<GameEntry> registered)
    {
        DiscoveryRows.Clear();
        IReadOnlyList<InstalledGame> installed;
        try { installed = _store.InstalledGames(); }
        catch { installed = Array.Empty<InstalledGame>(); }

        // Keyed by store, and EA games only when the manifest knows them (Core decides both).
        foreach (var ig in ModManager.Core.Stores.StoreDiscovery.Offerable(
                     installed, registered, ModManager.Core.Manifest.EffectiveManifest.Current.Games))
            DiscoveryRows.Add(new DiscoveredGameViewModel(ig, id => _covers.LocalPortrait(id)));
    }
```

- [ ] **Step 5: The add click.** At the top of `MainWindow.AddDiscoveredGameAsync`, before the Steam plan:

```csharp
        if (game.StoreKind == ModManager.Core.Stores.EaInstallScan.StoreKind)
        {
            var eaInput = ModManager.Core.Stores.EaGameImport.Plan(
                game, ModManager.Core.Manifest.EffectiveManifest.Current.Games,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            // Discovery only offers curated EA games, so a null here means the feed changed underneath
            // the lane. Adding it under a guessed id would lose its ban risk; do nothing instead.
            if (eaInput is not null) await ViewModel.AddGameAsync(eaInput);
            return;
        }
```

- [ ] **Step 6: The mod list and drops.** In `MainViewModel.RefreshEmptyState`, pass the state:

```csharp
        var msg = ModListEmptyState.MessageFor(HasGame, totalRows, visibleRows, ModFilterText, ActiveMode,
            noModLane: _ctx is not null && ModListing.HasNoModLane(_ctx));
```

In `MainViewModel.AddModsAsync`, directly after `if (_ctx is null || paths.Count == 0) return;`:

```csharp
        // Nowhere for a mod to go: say why, before any gate or extraction. Nothing is written.
        if (ModListing.HasNoModLane(_ctx))
        {
            StatusText = ModListEmptyState.NoModLane;
            return;
        }
```

- [ ] **Step 7: Build and run the suite.**

Close the app if it is running. Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.22.0`
Expected: Build succeeded, 0 errors; the warning count does not rise from its baseline.
Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS.

- [ ] **Step 8: Commit.**

```bash
git add src/ModManager.App/Services/EaLibrary.cs src/ModManager.App/Services/CompositeStoreLibrary.cs src/ModManager.App/App.xaml.cs src/ModManager.App/ViewModels/LibraryViewModel.cs src/ModManager.App/MainWindow.xaml.cs src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(app): find EA app games, offer the curated ones, add them in one click

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Smoke entry

**Files:**
- Modify: `docs/smoke-tests/pending.md` (append a section)

- [ ] **Step 1: Append** with the Edit tool, never a shell heredoc:

```markdown
## College Football 27 and Madden NFL 27 from the EA app

Shipped: EA app installs are detected, curated ones are offered in the discovery lane, and adding one
registers it with its manifest id, the EA content id, a data folder under `%LOCALAPPDATA%\626mods`, and
Play set to the EA app's launch link (spec `docs/superpowers/specs/2026-09-14-ea-app-games-slice-one-design.md`).

Needs the `eaContentId` data PR merged in `626-game-manifest` and the feed refreshed first.

1. **Offered.** With both games installed through the EA app, the library home's discovery lane shows
   both. A game already added does not show again.
2. **Added right.** Add each. `list_games` shows ids `ea-sports-college-football-27` and `madden-nfl-27`.
   `get_game_shape` shows no mod locations. The row shows the BAN RISK chip and no SETUP warning.
3. **Nothing written under Program Files.** Compare a listing of `C:\Program Files\EA Games` before and
   after both adds: identical. `%LOCALAPPDATA%\626mods\madden-nfl-27` is where the game's data goes.
4. **The mod list says so.** Open either game: the list reads *The launcher doesn't turn mods on or off
   for this game yet.* Dropping a zip on the window changes nothing and says the same sentence.
5. **Play (owner).** Press Play once on each. The EA app should start the game. If it does not, note it
   here: the row action switches to Open the EA app.

Why it matters: the first registration of a game from a store other than Steam, and the first where the
launcher's default data folder would have been unwritable.
```

- [ ] **Step 2: Check.**

Run: `python -c "b=open('docs/smoke-tests/pending.md','rb').read(); print(sum(1 for c in b if c<32 and c not in (9,10,13)))"`
Expected: `0`
Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SmokeCatalogueTests"`
Expected: PASS.

- [ ] **Step 3: Commit.**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): College Football 27 and Madden NFL 27 from the EA app

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## After the tasks (controller, not a task)

These happen outside the worktree, in order, and each is an outward action:

1. Merge the launcher PR, so the manifest repo's CI can build with the miner that knows `eaContentId`.
2. Open the data PR in `626-game-manifest`, signed off by the owner 2026-09-14. It adds `"eaContentId": "16425899"` to `overrides/ea-sports-college-football-27.json` and `"eaContentId": "16425895"` to `overrides/madden-nfl-27.json`. Merge it, and confirm the regenerated feed carries both. Verify the committed blob, not the CRLF checkout.
3. Run the live smoke in Task 7 on this machine, with `list_mods` / `list_games` snapshots before and after.
