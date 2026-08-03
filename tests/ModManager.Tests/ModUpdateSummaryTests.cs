using ModManager.Core;

namespace ModManager.Tests;

// ModUpdateSummary is a pure, never-throws reader over an already-persisted metadata.json: zero
// network, zero disk scan, one JSON read per game. It mirrors Mod.UpdateAvailable's pending rule
// (NexusLatestVersion non-blank and different from Version) and adds the "never checked" honesty
// distinction the badge/Updates-view need: Checked=false must never render as "0 updates".
public class ModUpdateSummaryTests
{
    private static GameContext Fixture(string prefix = "updatesummary-", string gameName = "T", string id = "t")
    {
        var root = TestSupport.TempDir(prefix);
        var gameRoot = Path.Combine(root, "game");
        Directory.CreateDirectory(gameRoot);
        return Scanner.GameContext(new GameEntry
        {
            Id = id, GameName = gameName, GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" },
            NexusGameDomain = "eldenring",
        });
    }

    [Fact]
    public void ForGame_counts_only_mods_with_a_different_latest_version()
    {
        var c = Fixture();
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            ["modA"] = new ModMeta { Title = "Mod A", Version = "1.0", NexusLatestVersion = "2.0", NexusModId = 11 },
            ["modB"] = new ModMeta { Title = "Mod B", Version = "3.0", NexusLatestVersion = "3.1", NexusModId = 22 },
            ["modC"] = new ModMeta { Title = "Mod C", Version = "1.0", NexusLatestVersion = "1.0" }, // up to date
            ["modD"] = new ModMeta { Title = "Mod D", Version = "1.0", NexusLatestVersion = null },   // never polled
            ["modE"] = new ModMeta { Title = "Mod E", Version = "1.0", NexusLatestVersion = "" },     // blank
        });

        var summary = ModUpdateSummary.ForGame(c.Game);

        Assert.True(summary.Checked);
        Assert.Equal(2, summary.Count);
        Assert.Equal(2, summary.Pending.Count);

        var a = Assert.Single(summary.Pending, p => p.ModKey == "modA");
        Assert.Equal("t", a.GameId);
        Assert.Equal("T", a.GameName);
        Assert.Equal("Mod A", a.ModName);
        Assert.Equal("1.0", a.InstalledVersion);
        Assert.Equal("2.0", a.LatestVersion);
        Assert.Equal(11, a.NexusModId);
        Assert.Equal("eldenring", a.NexusDomain);

        var b = Assert.Single(summary.Pending, p => p.ModKey == "modB");
        Assert.Equal("3.0", b.InstalledVersion);
        Assert.Equal("3.1", b.LatestVersion);
    }

    [Fact]
    public void ForGame_equal_versions_are_not_pending()
    {
        var c = Fixture();
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            ["modA"] = new ModMeta { Title = "Mod A", Version = "1.0", NexusLatestVersion = "1.0" },
        });

        var summary = ModUpdateSummary.ForGame(c.Game);

        Assert.True(summary.Checked); // it WAS polled — just nothing newer
        Assert.Equal(0, summary.Count);
        Assert.Empty(summary.Pending);
    }

    [Fact]
    public void ForGame_null_or_blank_latest_version_is_not_pending_and_does_not_mark_checked()
    {
        var c = Fixture();
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            ["modA"] = new ModMeta { Title = "Mod A", Version = "1.0", NexusLatestVersion = null },
            ["modB"] = new ModMeta { Title = "Mod B", Version = "1.0", NexusLatestVersion = "   " },
        });

        var summary = ModUpdateSummary.ForGame(c.Game);

        Assert.False(summary.Checked);
        Assert.Equal(0, summary.Count);
    }

    [Fact]
    public void ForGame_never_refreshed_game_is_unchecked_not_zero()
    {
        // THE HONESTY RULE: no entry anywhere has a NexusLatestVersion => Checked must be false,
        // not "true with Count 0" — those two states must be distinguishable by the caller.
        var c = Fixture();
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            ["modA"] = new ModMeta { Title = "Mod A", Version = "1.0" },
            ["modB"] = new ModMeta { Title = "Mod B", Version = "2.0" },
        });

        var summary = ModUpdateSummary.ForGame(c.Game);

        Assert.False(summary.Checked);
        Assert.Equal(0, summary.Count);
        Assert.Empty(summary.Pending);
    }

    [Fact]
    public void ForGame_missing_metadata_file_returns_unchecked_without_throwing()
    {
        var c = Fixture(); // SaveMetadata never called — metadata.json does not exist

        var summary = ModUpdateSummary.ForGame(c.Game);

        Assert.False(summary.Checked);
        Assert.Equal(0, summary.Count);
        Assert.Empty(summary.Pending);
    }

    [Fact]
    public void ForGame_empty_metadata_file_returns_unchecked_without_throwing()
    {
        var c = Fixture();
        TestSupport.Write(c.MetadataPath, "");

        var summary = ModUpdateSummary.ForGame(c.Game);

        Assert.False(summary.Checked);
        Assert.Equal(0, summary.Count);
    }

    [Fact]
    public void ForGame_malformed_json_returns_unchecked_without_throwing()
    {
        var c = Fixture();
        TestSupport.Write(c.MetadataPath, "{ this is not valid json ]]]");

        var summary = ModUpdateSummary.ForGame(c.Game);

        Assert.False(summary.Checked);
        Assert.Equal(0, summary.Count);
        Assert.Empty(summary.Pending);
    }

    [Fact]
    public void ForGames_aggregates_and_preserves_per_game_identity()
    {
        var c1 = Fixture("updatesummary-g1-", "Elden Ring", id: "eldenring");
        Scanner.SaveMetadata(c1, new Dictionary<string, ModMeta>
        {
            ["modA"] = new ModMeta { Title = "Mod A", Version = "1.0", NexusLatestVersion = "2.0" },
        });

        var c2 = Fixture("updatesummary-g2-", "Skyrim", id: "skyrim");
        // never refreshed — no NexusLatestVersion anywhere

        var results = ModUpdateSummary.ForGames(new[] { c1.Game, c2.Game });

        Assert.Equal(2, results.Count);

        var g1 = results.Single(r => r.GameId == "eldenring");
        Assert.Equal("Elden Ring", g1.GameName);
        Assert.True(g1.Checked);
        Assert.Equal(1, g1.Count);

        var g2 = results.Single(r => r.GameId == "skyrim");
        Assert.Equal("Skyrim", g2.GameName);
        Assert.False(g2.Checked);
        Assert.Equal(0, g2.Count);
    }
}
