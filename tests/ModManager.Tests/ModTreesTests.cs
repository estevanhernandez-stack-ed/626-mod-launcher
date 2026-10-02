using System.Text.Json;
using ManifestMiner;
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests;

/// <summary>
/// B4, "see first, toggle later": a game's extra mod trees, carried by the manifest, and which of them a
/// mod also has files in. Nothing here toggles anything.
/// </summary>
public class ModTreesTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-modtrees-");
    private static readonly string[] Trees = { "r6/scripts", "r6/tweaks", "red4ext/plugins", "bin/x64/plugins/cyber_engine_tweaks/mods" };

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void Dir(string rel) => Directory.CreateDirectory(Path.Combine(_root, rel));
    private void File_(string rel) { Dir(Path.GetDirectoryName(rel)!); File.WriteAllText(Path.Combine(_root, rel), "x"); }

    [Fact]
    public void A_mod_lists_every_extra_tree_holding_an_entry_with_its_name_in_manifest_order()
    {
        File_("archive/pc/mod/CoolMod.archive");
        Dir("red4ext/plugins/CoolMod");
        Dir("r6/scripts/CoolMod");
        File_("r6/tweaks/CoolMod.yaml");
        Dir("r6/scripts/SomethingElse");

        var trees = ModTrees.Build(_root, Trees);

        Assert.Equal(new[] { "r6/scripts", "r6/tweaks", "red4ext/plugins" }, trees.For("CoolMod"));
        Assert.Equal(new[] { "r6/scripts" }, trees.For("SomethingElse"));
    }

    [Fact]
    public void Only_an_equal_name_counts_never_a_fuzzy_one()
    {
        // Telling the user a folder belongs to a mod it doesn't is worse than saying nothing.
        Dir("r6/scripts/CoolModExtras");
        Dir("r6/scripts/Cool");

        Assert.Empty(ModTrees.Build(_root, Trees).For("CoolMod"));
    }

    [Fact]
    public void Missing_trees_and_a_missing_game_root_say_nothing_rather_than_throw()
    {
        Assert.Empty(ModTrees.Build(_root, Trees).For("CoolMod"));
        Assert.Empty(ModTrees.Build(null, Trees).For("CoolMod"));
        Assert.Empty(ModTrees.Build(_root, null).For("CoolMod"));
        Assert.Empty(ModTrees.Empty.For("CoolMod"));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        Dir("r6/scripts/coolmod");
        Assert.Equal(new[] { "r6/scripts" }, ModTrees.Build(_root, Trees).For("CoolMod"));
    }

    // ---- the manifest field ----

    [Fact]
    public void ExtraModTrees_round_trips_as_camelCase()
    {
        var original = new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "cyberpunk-2077", Name = "Cyberpunk 2077", ModPath = "archive/pc/mod", ExtraModTrees = Trees } },
        };

        var json = JsonSerializer.Serialize(original, ManifestJson.Options);
        Assert.Contains("\"extraModTrees\"", json);
        Assert.DoesNotContain("\"ExtraModTrees\"", json);

        var back = JsonSerializer.Deserialize<GameManifest>(json, ManifestJson.Options)!;
        Assert.Equal(Trees, back.Games.Single().ExtraModTrees);
    }

    [Fact]
    public void An_entry_without_extra_trees_reads_as_none()
    {
        var back = JsonSerializer.Deserialize<GameManifest>(
            "{\"games\":[{\"id\":\"x\",\"name\":\"X\",\"modPath\":\"Data\"}]}", ManifestJson.Options)!;
        Assert.Null(back.Games.Single().ExtraModTrees);
    }

    [Theory]
    [InlineData("C:/Windows")]
    [InlineData("../escape")]
    [InlineData("r6/../../escape")]
    [InlineData("D:relative")]
    public void One_unsafe_extra_tree_rejects_the_entry(string bad)
    {
        var manifest = new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "bad", Name = "Bad", Engine = "custom", ExtraModTrees = new[] { "r6/scripts", bad } } },
        };

        var result = ManifestValidator.Validate(manifest, EnginePresets.Presets.Keys.ToHashSet());

        Assert.Empty(result.Manifest.Games);
        Assert.Contains("bad", result.RejectedEntries);
    }

    [Fact]
    public void Safe_extra_trees_pass_the_validator()
    {
        var manifest = new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "ok", Name = "Ok", Engine = "custom", ExtraModTrees = Trees } },
        };
        Assert.Single(ManifestValidator.Validate(manifest, EnginePresets.Presets.Keys.ToHashSet()).Manifest.Games);
    }

    [Fact]
    public void A_curated_override_carries_extra_trees_into_the_manifest()
    {
        var backbone = new GameManifest
        {
            Games = new List<GameManifestEntry>
            {
                new() { Id = "cyberpunk-2077", Name = "Cyberpunk 2077", Stores = new StoreIds { SteamAppId = "1091500" },
                        Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" }, Status = "auto" } },
            },
        };
        var merged = OverridesMerge.Apply(backbone, new[] { new OverrideEntry { SteamAppId = "1091500", ExtraModTrees = Trees } });

        Assert.Equal(Trees, merged.Games.Single().ExtraModTrees);
    }
}
