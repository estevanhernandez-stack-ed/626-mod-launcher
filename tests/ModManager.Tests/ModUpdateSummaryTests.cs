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

    // ---- An unknown installed version is not an update ----
    // Found by live smoke on a 98-mod Cyberpunk library: every row wore an UPDATE chip. A
    // name-search identify deliberately never writes Version (a name match says WHICH mod, never
    // which FILE is installed), then the by-id enrichment pass writes NexusLatestVersion — so the
    // comparison ran between a real upstream version and nothing, which always differs.
    //
    // The failure mode is what makes it dangerous: "everything needs updating" is PLAUSIBLE to a
    // user returning to an old library, so a false positive here reads as truth and never gets
    // reported. Acting on it re-downloads mods that were already current.

    [Fact]
    public void A_mod_with_no_installed_version_is_never_pending()
    {
        var c = Fixture("updatesummary-noinstalled-");
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            // Exactly the shape the identify-then-enrich path produces.
            ["NameSearched"] = new() { NexusModId = 1, NexusLatestVersion = "2.1", Version = null },
            ["BlankInstalled"] = new() { NexusModId = 2, NexusLatestVersion = "2.1", Version = "   " },
            ["Genuine"] = new() { NexusModId = 3, NexusLatestVersion = "2.1", Version = "1.0" },
        });

        var g = ModUpdateSummary.ForGame(c.Game);

        // Only the row whose installed version we actually know can be pending.
        Assert.Equal(1, g.Count);
        Assert.Equal("Genuine", g.Pending.Single().ModKey);
    }

    // We DID poll those rows, so the game still counts as checked — "unknown installed" is a gap in
    // what we know about the mod, not a gap in whether we looked.
    [Fact]
    public void Polled_rows_still_count_as_checked_even_when_none_can_be_pending()
    {
        var c = Fixture("updatesummary-checkednopending-");
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            ["A"] = new() { NexusModId = 1, NexusLatestVersion = "2.1", Version = null },
        });

        var g = ModUpdateSummary.ForGame(c.Game);

        Assert.True(g.Checked);
        Assert.Equal(0, g.Count);
    }

    // The chip and the badge read the same persisted fields and must never disagree — Mod.UpdateAvailable
    // says so in its own doc comment. Pin them together so a fix to one can't drift from the other.
    [Theory]
    [InlineData(null, "2.1", false)]   // installed unknown -> cannot claim an update
    [InlineData("", "2.1", false)]
    [InlineData("   ", "2.1", false)]
    [InlineData("1.0", "2.1", true)]   // both known and different -> genuine update
    [InlineData("2.1", "2.1", false)]  // both known and equal -> current
    [InlineData("1.0", null, false)]   // never polled
    [InlineData("1.0", "   ", false)]  // blank upstream is the same as never polled
    public void Chip_and_badge_agree_on_every_version_pairing(string? installed, string? latest, bool expected)
    {
        var mod = new Mod { Version = installed, NexusLatestVersion = latest };

        Assert.Equal(expected, mod.UpdateAvailable);

        var c = Fixture("updatesummary-parity-" + Guid.NewGuid().ToString("N")[..8] + "-");
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            ["K"] = new() { NexusModId = 1, Version = installed, NexusLatestVersion = latest },
        });

        Assert.Equal(expected, ModUpdateSummary.ForGame(c.Game).Count == 1);
    }
}
