using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// A27, the half that was missed. A10 fixed what the row SAYS and never touched whether the row
/// EXISTS.
///
/// <para>Its own test file names the two motivating examples — <c>1.1 → 1</c> and
/// <c>1.0.1 → 1.0.0</c>, "two apparent DOWNGRADES the launcher was inviting the user to act on". The
/// arrow went away. The rows did not: both were still in the Updates view when the 0.19 store
/// screenshots were taken, reading <i>"1.0.1 installed · Nexus lists 1.0.0"</i>. Carefully worded,
/// still an invitation to update to an older release.</para>
///
/// <para>The decision was string inequality: <c>latest != installed</c>. It never asked which was
/// newer, although <see cref="PendingUpdate.Compare"/> had been able to answer since A10.</para>
///
/// <para><b>What this deliberately does not change.</b> Nexus's own per-user flag still outranks
/// everything — Nexus knows which FILE was downloaded and we often do not. And an unorderable pair
/// (<c>1.0.0.1</c> vs <c>1.0.0.1-hotfix</c>) still lists, because refusing to guess is the point: the
/// hotfix probably IS newer and the row says so without implying a direction. Only the case we can
/// PROVE is backwards gets dropped.</para>
/// </summary>
public class UpdateIsNotJustADifferenceTests
{
    private static Mod Installed(string? version, string? latest, bool? flag = null) => new()
    {
        Name = "m",
        Version = version,
        NexusLatestVersion = latest,
        NexusUpdateAvailable = flag,
    };

    // ---- the row-level chip -------------------------------------------------------------------

    [Theory]
    [InlineData("1.1", "1")]        // A10's first named example
    [InlineData("1.0.1", "1.0.0")]  // A10's second
    [InlineData("2.0", "1.9.9")]
    [InlineData("1.0.0.2", "1.0.0.1")]
    public void A_version_we_can_prove_is_older_is_not_an_update(string installed, string latest)
    {
        // The whole bug in one assertion: Nexus listing something OLDER than what is on disk is not
        // an update, and saying so carefully does not make it one.
        Assert.False(Installed(installed, latest).UpdateAvailable);
    }

    [Theory]
    [InlineData("1.0", "2.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1", "1.1")]
    public void A_version_we_can_prove_is_newer_still_is_one(string installed, string latest)
        => Assert.True(Installed(installed, latest).UpdateAvailable);

    [Theory]
    [InlineData("1.0.0.1", "1.0.0.1-hotfix")]
    [InlineData("1.0", "1.0b")]
    [InlineData("2024.1", "final")]
    public void A_pair_we_cannot_order_still_lists_because_refusing_to_guess_cuts_both_ways(
        string installed, string latest)
    {
        // Dropping these would hide real updates to tidy away a display problem. The row names both
        // versions and implies no direction — that wording already exists and is correct.
        Assert.True(Installed(installed, latest).UpdateAvailable);
    }

    [Fact]
    public void Nexus_own_flag_outranks_the_compare_in_both_directions()
    {
        // Nexus knows which FILE the user downloaded; the version strings often cannot. A flag saying
        // "behind" wins even when the numbers look backwards, and a flag saying "not behind" wins even
        // when they look forwards.
        Assert.True(Installed("1.0.1", "1.0.0", flag: true).UpdateAvailable);
        Assert.False(Installed("1.0", "2.0", flag: false).UpdateAvailable);
    }

    [Fact]
    public void Equivalent_versions_are_still_not_an_update()
    {
        // Unchanged behaviour, pinned because the new clause sits right next to it: "1" and "1.0" are
        // the same release, and were already excluded by the plain string compare.
        Assert.False(Installed("1.0", "1.0").UpdateAvailable);
    }

    // ---- the badge and the Updates view, which must agree with the chip ------------------------

    private static GameContext Fixture()
    {
        var root = TestSupport.TempDir("update-direction-");
        var gameRoot = Path.Combine(root, "game");
        Directory.CreateDirectory(gameRoot);
        return Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" },
            NexusGameDomain = "eldenring",
        });
    }

    [Fact]
    public void The_badge_and_the_view_drop_the_backwards_rows_too()
    {
        // ModUpdateSummary mirrors Mod.UpdateAvailable by design and says so in a comment: a
        // divergence shows an UPDATE chip on a row and then an empty Updates view. Both had the same
        // gap, so both get the same clause, and this is what keeps them honest about it.
        var c = Fixture();
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            // The two from the store screenshot.
            ["fastcraft"] = new ModMeta { Title = "Fast Craft", Version = "1.1", NexusLatestVersion = "1", NexusModId = 1 },
            ["ultrawide"] = new ModMeta { Title = "Ultrawide Fix", Version = "1.0.1", NexusLatestVersion = "1.0.0", NexusModId = 2 },
            // Genuinely behind.
            ["real"] = new ModMeta { Title = "Real", Version = "1.0", NexusLatestVersion = "1.2", NexusModId = 3 },
            // Unorderable — stays.
            ["hotfix"] = new ModMeta { Title = "Hotfix", Version = "1.0.0.1", NexusLatestVersion = "1.0.0.1-hotfix", NexusModId = 4 },
        });

        var summary = ModUpdateSummary.ForGame(c.Game);

        Assert.True(summary.Checked);
        Assert.DoesNotContain(summary.Pending, p => p.ModKey == "fastcraft");
        Assert.DoesNotContain(summary.Pending, p => p.ModKey == "ultrawide");
        Assert.Contains(summary.Pending, p => p.ModKey == "real");
        Assert.Contains(summary.Pending, p => p.ModKey == "hotfix");
        Assert.Equal(2, summary.Count);
    }

    [Fact]
    public void A_game_whose_only_differences_are_backwards_reads_as_checked_with_nothing_pending()
    {
        // The distinction ModUpdateSummary exists to protect: Checked=true with Count=0 means "looked,
        // nothing to do". It must not collapse into "never checked" just because every difference
        // turned out to point the wrong way.
        var c = Fixture();
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            ["a"] = new ModMeta { Title = "A", Version = "1.1", NexusLatestVersion = "1", NexusModId = 1 },
        });

        var summary = ModUpdateSummary.ForGame(c.Game);

        Assert.True(summary.Checked);
        Assert.Equal(0, summary.Count);
    }
}
