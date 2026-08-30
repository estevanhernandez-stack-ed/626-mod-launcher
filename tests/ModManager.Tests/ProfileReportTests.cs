using ModManager.Core.Transport;

namespace ModManager.Tests;

/// <summary>
/// Reading an archive against this machine, before anything is touched.
///
/// <para>Step two of the profile archive, and shippable on its own: it answers <i>"what is in this
/// thing?"</i> with no restore button near it. It is also the screen a restore will hang off, which is
/// why it comes first — the report is the part that has to be right.</para>
/// </summary>
public class ProfileReportTests
{
    private static ProfileGame G(string id, int saveFiles = 0, long saveBytes = 0,
                                int dataFiles = 0, params string[] mods)
        => new()
        {
            Game = new BundleGame(id, "1", id),
            SaveIncluded = saveFiles > 0,
            SaveFileCount = saveFiles,
            SaveBytes = saveBytes,
            DataFileCount = dataFiles,
            Mods = mods.Select(m => new BundleMod(m, "1.0", 7, true)).ToList(),
        };

    private static ProfileArchiveManifest M(params ProfileGame[] games)
        => new() { CreatedUtc = "2026-08-20T12:00:00.0000000Z", LauncherVersion = "0.19.0", Games = games };

    private static Dictionary<string, IReadOnlyCollection<string>> Here(
        params (string Id, string[] Mods)[] games)
        => games.ToDictionary(g => g.Id, g => (IReadOnlyCollection<string>)g.Mods);

    [Fact]
    public void A_game_this_machine_does_not_have_is_waiting_not_failing()
    {
        // The normal case on a fresh install, and the whole reason the feature exists. It must read as
        // "install the game and come back", never as an error.
        var report = ProfileInspector.Inspect(M(G("palworld"), G("cyberpunk-2077")),
                                              Here(("palworld", Array.Empty<string>())));

        Assert.Equal("palworld", Assert.Single(report.Here).Game.Game.Id);
        Assert.Equal("cyberpunk-2077", Assert.Single(report.NotHere).Game.Game.Id);
    }

    [Fact]
    public void Missing_mods_are_only_counted_for_games_that_are_actually_here()
    {
        // "You are missing 194 mods for a game you have not installed" is noise dressed as
        // information - it gives someone a number they cannot act on.
        var report = ProfileInspector.Inspect(
            M(G("cyberpunk-2077", mods: new[] { "A", "B", "C" })),
            Here(("palworld", Array.Empty<string>())));

        var g = Assert.Single(report.Games);
        Assert.False(g.RegisteredHere);
        Assert.Empty(g.MissingMods);
    }

    [Fact]
    public void Missing_mods_are_named_where_the_game_IS_here()
    {
        var report = ProfileInspector.Inspect(
            M(G("palworld", mods: new[] { "Kept", "Gone", "AlsoGone" })),
            Here(("palworld", new[] { "kept" })));       // case-insensitive, per SaveBundle.MissingMods

        var g = Assert.Single(report.Games);
        Assert.True(g.RegisteredHere);
        Assert.Equal(new[] { "Gone", "AlsoGone" }, g.MissingMods.Select(m => m.Name));
    }

    [Fact]
    public void Registered_with_no_mods_is_different_from_not_registered()
    {
        // One says "you are missing everything", the other says "install the game first". Collapsing
        // them would tell someone to go find mods for a game they do not own.
        var manifest = M(G("palworld", mods: new[] { "A" }));

        var installed = ProfileInspector.Inspect(manifest, Here(("palworld", Array.Empty<string>())));
        Assert.True(Assert.Single(installed.Games).RegisteredHere);
        Assert.Single(Assert.Single(installed.Games).MissingMods);

        var absent = ProfileInspector.Inspect(manifest, Here(("other", Array.Empty<string>())));
        Assert.False(Assert.Single(absent.Games).RegisteredHere);
        Assert.Empty(Assert.Single(absent.Games).MissingMods);
    }

    [Fact]
    public void The_headline_gives_the_count_a_denominator()
    {
        // A count with no denominator tells nobody anything. "12 games" is not the useful fact; "9 of
        // them are set up here" is.
        var three = M(G("a", 10, 1024), G("b", 5, 512), G("c"));

        Assert.Contains("3 games", ProfileInspector.Inspect(three, Here(("a", new string[0]))).Headline);
        Assert.Contains("1 of them is set up here", ProfileInspector.Inspect(three, Here(("a", new string[0]))).Headline);
        Assert.Contains("2 are waiting on the game", ProfileInspector.Inspect(three, Here(("a", new string[0]))).Headline);

        Assert.Contains("You already have all of them.",
            ProfileInspector.Inspect(three, Here(("a", new string[0]), ("b", new string[0]), ("c", new string[0]))).Headline);

        Assert.Contains("None of them are set up on this machine yet.",
            ProfileInspector.Inspect(three, Here()).Headline);
    }

    [Fact]
    public void An_empty_or_unreadable_archive_says_so_rather_than_rendering_a_blank_screen()
    {
        Assert.Equal("This archive has no games in it.", ProfileInspector.Inspect(M(), Here()).Headline);
        Assert.Equal("This archive has no games in it.", ProfileInspector.Inspect(null, Here()).Headline);
        Assert.Empty(ProfileInspector.Inspect(null, null).Games);
    }

    [Fact]
    public void Exclusions_are_grouped_by_reason_so_the_screen_can_say_what_kind()
    {
        var m = M(G("palworld")) with
        {
            Excluded = new[]
            {
                new BundleExclusion("games/cp/save/user.gls", "credential"),
                new BundleExclusion("games/pal/save/Players/x.sav", "character"),
                new BundleExclusion("games/w/save/Players/y.sav", "character"),
            },
        };

        var by = ProfileInspector.Inspect(m, Here()).ExcludedByReason;

        Assert.Equal(1, by["credential"]);
        Assert.Equal(2, by["character"]);
    }

    [Fact]
    public void A_games_detail_line_says_what_it_holds_and_what_is_missing()
    {
        var here = ProfileInspector.Inspect(
            M(G("palworld", 79, 32_753_386, 3, "Kept", "Gone")),
            Here(("palworld", new[] { "Kept" })));

        var line = ProfileReportText.DetailFor(Assert.Single(here.Games));
        Assert.Contains("79 save files", line);
        Assert.Contains("2 mods", line);
        Assert.Contains("settings", line);
        Assert.Contains("1 mod not installed here", line);
    }

    [Fact]
    public void A_game_that_is_not_here_says_that_instead_of_a_mod_count()
    {
        var line = ProfileReportText.DetailFor(Assert.Single(
            ProfileInspector.Inspect(M(G("cyberpunk-2077", 278, 1024, 5, "A", "B")), Here()).Games));

        Assert.Contains("not set up here yet", line);
        Assert.DoesNotContain("not installed here", line);
    }

    [Fact]
    public void A_game_carrying_nothing_still_says_so_rather_than_showing_an_empty_line()
    {
        Assert.Contains("nothing", ProfileReportText.DetailFor(
            new ProfileGameReport(G("empty"), true, Array.Empty<BundleMod>())));
    }

    [Fact]
    public void The_backup_date_is_round_trip_parsed_not_locale_guessed()
    {
        Assert.Equal("", ProfileReportText.WhenMade(null));
        Assert.Equal("", ProfileReportText.WhenMade("not a date"));
        Assert.NotEqual("", ProfileReportText.WhenMade("2026-08-20T12:00:00.0000000Z"));
    }
}
