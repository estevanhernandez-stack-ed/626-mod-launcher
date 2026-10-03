using System.Text.Json;
using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

/// <summary>
/// <c>modPathModOnly</c>: a descriptive manifest field saying the game's modPath holds nothing but mods.
/// Safe Clear may sweep only such a folder (and the declared extra trees, and the per-engine shapes in
/// <c>ModOnlyFolders</c>). Carried like extraModTrees: camelCase, merged remote-wins, curated through the
/// miner's overrides, and set in the embedded snapshot for Cyberpunk 2077.
/// </summary>
public class ModPathModOnlyTests
{
    [Fact]
    public void Round_trips_as_camelCase()
    {
        var original = new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "cyberpunk-2077", Name = "Cyberpunk 2077", ModPath = "archive/pc/mod", ModPathModOnly = true } },
        };

        var json = JsonSerializer.Serialize(original, ManifestJson.Options);
        Assert.Contains("\"modPathModOnly\"", json);
        Assert.DoesNotContain("\"ModPathModOnly\"", json);

        var back = JsonSerializer.Deserialize<GameManifest>(json, ManifestJson.Options)!;
        Assert.True(back.Games.Single().ModPathModOnly);
    }

    [Fact]
    public void An_entry_that_does_not_say_reads_as_null_never_as_mod_only()
    {
        var back = JsonSerializer.Deserialize<GameManifest>(
            "{\"games\":[{\"id\":\"x\",\"name\":\"X\",\"modPath\":\"Data\"}]}", ManifestJson.Options)!;
        Assert.Null(back.Games.Single().ModPathModOnly);
    }

    [Fact]
    public void The_embedded_Cyberpunk_entry_marks_archive_pc_mod_mod_only_and_the_validator_keeps_it()
    {
        var cp = EmbeddedGameManifest.Current.Games.Single(g => g.Id == "cyberpunk-2077");
        Assert.Equal("archive/pc/mod", cp.ModPath);
        Assert.True(cp.ModPathModOnly);

        var result = ManifestValidator.Validate(new GameManifest { Games = new[] { cp } }, new HashSet<string> { "custom" });
        Assert.True(result.Manifest.Games.Single().ModPathModOnly);
    }

    [Fact]
    public void No_embedded_Bethesda_entry_is_marked_mod_only()
        => Assert.DoesNotContain(EmbeddedGameManifest.Current.Games,
            g => g.Engine == "bethesda" && g.ModPathModOnly == true);

    [Fact]
    public void A_remote_value_wins_the_merge_and_an_absent_one_keeps_the_embedded()
    {
        var embedded = new GameManifest { Games = new[] { new GameManifestEntry { Id = "g", ModPath = "m", ModPathModOnly = true } } };
        // The flag travels with the path: a remote that names a path brings its own flag.
        Assert.False(EffectiveManifest.Merge(embedded,
            new GameManifest { Games = new[] { new GameManifestEntry { Id = "g", ModPath = "m", ModPathModOnly = false } } }).Games.Single().ModPathModOnly);
        Assert.True(EffectiveManifest.Merge(embedded,
            new GameManifest { Games = new[] { new GameManifestEntry { Id = "g" } } }).Games.Single().ModPathModOnly);
    }

    [Fact]
    public void An_override_sets_it_on_a_matched_entry_and_carries_it_on_an_added_one()
    {
        var backbone = new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry { Id = "a", Name = "a", Stores = new StoreIds { SteamAppId = "1" },
                    Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" }, Status = "auto" } },
            },
        };
        var result = OverridesMerge.Apply(backbone, new[]
        {
            new OverrideEntry { SteamAppId = "1", ModPath = "mods", ModPathModOnly = true },
            new OverrideEntry { SteamAppId = "2", Id = "b", Name = "B", Engine = "custom", ModPath = "x/mods", ModPathModOnly = true },
        });
        Assert.True(result.Games.Single(g => g.Stores.SteamAppId == "1").ModPathModOnly);
        Assert.True(result.Games.Single(g => g.Stores.SteamAppId == "2").ModPathModOnly);
    }

    [Fact]
    public void The_overrides_gate_refuses_mod_only_without_a_mod_path()
    {
        var problems = OverridesValidate.Check(new[]
        {
            new OverrideEntry { SteamAppId = "1", Id = "a", Name = "A", Engine = "custom", ModPathModOnly = true },
        });

        Assert.Contains(problems, p => p.Message.Contains("modPathModOnly"));
    }
}
