using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class ManifestInvariantsTests
{
    private static IReadOnlyList<GameManifestEntry> Games => EmbeddedGameManifest.Current.Games;

    [Fact]
    public void Ids_are_unique()
    {
        var dupes = Games.GroupBy(g => g.Id).Where(grp => grp.Count() > 1).Select(grp => grp.Key).ToArray();
        Assert.Empty(dupes);
    }

    [Fact]
    public void Every_entry_has_id_and_name()
    {
        foreach (var g in Games)
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Id), $"empty id: {g.Name}");
            Assert.False(string.IsNullOrWhiteSpace(g.Name), $"empty name: {g.Id}");
        }
    }

    [Fact]
    public void A_present_steam_app_id_is_never_blank()
    {
        // Steam app id is optional (Minecraft has none — a game sold nowhere the launcher probes
        // still gets curated). When one IS present it must be a real value, never "".
        foreach (var g in Games.Where(g => g.Stores.SteamAppId is not null))
            Assert.False(string.IsNullOrWhiteSpace(g.Stores.SteamAppId), $"blank steam app id: {g.Id}");
    }

    [Fact]
    public void Every_non_null_engine_is_a_real_preset()
    {
        foreach (var g in Games.Where(g => g.Engine is not null))
            Assert.True(EnginePresets.Presets.ContainsKey(g.Engine!), $"{g.Id}: unknown engine '{g.Engine}'");
    }

    [Fact]
    public void Every_entry_has_a_provenance_source()
    {
        foreach (var g in Games)
            Assert.NotEmpty(g.Provenance.Sources);
    }

    [Fact]
    public void Popular_games_carry_the_fields_the_quick_pick_projection_needs()
    {
        foreach (var g in Games.Where(g => g.Provenance.Sources.Contains(ManifestSources.PopularGames)))
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Engine), $"{g.Id}: popular entry needs engine");
            Assert.False(string.IsNullOrWhiteSpace(g.ModPath), $"{g.Id}: popular entry needs modPath");
            Assert.NotNull(g.Featured);
        }
    }

    [Fact]
    public void No_field_carries_a_url_or_binary_path()
    {
        // honor-the-builders: layer-1a identity data never carries a binary or a download URL.
        foreach (var g in Games)
        {
            var values = new[] { g.Id, g.Name, g.Engine, g.NexusDomain, g.ModPath, g.GroupingRule }
                .Concat(g.FileExtensions ?? Array.Empty<string>())
                .Where(v => v is not null)!;
            foreach (var v in values)
            {
                Assert.DoesNotContain("http://", v, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("https://", v, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".dll", v, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".exe", v, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Mod_paths_are_safe_relative_paths()
    {
        foreach (var g in Games.Where(g => g.ModPath is not null))
        {
            Assert.False(Path.IsPathRooted(g.ModPath!), $"{g.Id}: rooted modPath");
            Assert.DoesNotContain("..", g.ModPath!.Split('/', '\\'));
        }
    }

    [Fact]
    public void No_two_entries_claim_one_Steam_app_id()
    {
        // An app id names the game; an entry id names a row. Two rows on one app id is one game
        // described twice, and every facade that keys by app id -- KnownEngines, NexusDomains,
        // KnownModPaths -- silently resolves it by iteration order. EffectiveManifest.Merge collapses
        // this across the snapshot/feed boundary, where it actually happened (Skyrim SE was skyrim-se
        // here and the-elder-scrolls-v-skyrim-special-edition in the feed). Nothing collapses it
        // WITHIN one file, so the snapshot has to be clean on its own.
        var dupes = EmbeddedGameManifest.Current.Games
            .Where(g => !string.IsNullOrEmpty(g.Stores.SteamAppId))
            .GroupBy(g => g.Stores.SteamAppId!, StringComparer.Ordinal)
            .Where(grp => grp.Count() > 1)
            .Select(grp => $"{grp.Key}: {string.Join(", ", grp.Select(g => g.Id))}")
            .ToList();

        Assert.Empty(dupes);
    }
}
