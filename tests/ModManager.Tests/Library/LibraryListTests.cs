using ModManager.Core;
using ModManager.Core.Library;
using ModManager.Core.Recency;

namespace ModManager.Tests.Library;

// B6: the library home is one list. Every game on the system 626 can see, managed or not, in a single
// order; managing is something a row has, not a lane it moves between.
public class LibraryListTests
{
    private static GameLibraryRow Managed(string id, string name, DateTime? played = null,
        string? store = "steam", EngineTier tier = EngineTier.Unknown, string? banRisk = null)
        => new(id, name, store, null, new LastPlayed(played, null, played is null ? "none" : "t"),
            0, 0, null, tier, banRisk, Array.Empty<string>(), null);

    private static InstalledGame Steam(string appId, string name, long? lastPlayedUnix = null)
        => new("steam", appId, name, $@"C:\Steam\common\{name}") { LastPlayed = lastPlayedUnix?.ToString() };

    private static InstalledGame Ea(string contentId, string name)
        => new("ea", contentId, name, $@"C:\EA Games\{name}");

    private static long Unix(int y, int m, int d) => new DateTimeOffset(y, m, d, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    [Fact]
    public void Managed_and_unmanaged_games_share_one_order_most_recent_first()
    {
        var list = LibraryList.Compose(
            new[]
            {
                Managed("elden", "Elden Ring", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)),
                Managed("windrose", "Windrose", new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc)),
            },
            new[] { Steam("1091500", "Cyberpunk 2077", Unix(2026, 9, 10)) });

        Assert.Equal(new[] { "Windrose", "Cyberpunk 2077", "Elden Ring" }, list.Select(e => e.Name));
        Assert.Equal(new[] { true, false, true }, list.Select(e => e.IsManaged));
    }

    [Fact]
    public void Never_played_games_go_last_by_name_whichever_kind_they_are()
    {
        var list = LibraryList.Compose(
            new[] { Managed("b", "Bravo"), Managed("z", "Zulu", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)) },
            new[] { Ea("Origin.OFR.1", "Alpha"), Steam("10", "Charlie", lastPlayedUnix: 0) });   // Steam stamps 0 for never

        Assert.Equal(new[] { "Zulu", "Alpha", "Bravo", "Charlie" }, list.Select(e => e.Name));
    }

    [Fact]
    public void An_unmanaged_game_carries_its_store_and_the_stores_own_last_played()
    {
        var e = Assert.Single(LibraryList.Compose(Array.Empty<GameLibraryRow>(), new[] { Steam("1091500", "Cyberpunk 2077", Unix(2026, 9, 10)) }));

        Assert.False(e.IsManaged);
        Assert.Null(e.Managed);
        Assert.Equal("steam", e.Store);
        Assert.Equal(new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc), e.LastPlayedUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a number")]
    [InlineData("-5")]
    public void An_unreadable_or_missing_store_time_is_unknown_not_epoch(string? raw)
        => Assert.Null(LibraryList.StoreLastPlayed(new InstalledGame("steam", "1", "X", "C:\\X") { LastPlayed = raw }));

    [Fact]
    public void Search_and_store_filter_apply_to_both_kinds()
    {
        var managed = LibraryEntry.Of(Managed("elden", "Elden Ring", store: "steam"));
        var unmanaged = LibraryEntry.Of(Ea("Origin.OFR.1", "Madden NFL 27"));

        Assert.True(LibraryList.Matches(unmanaged, "madden", null, null, false));
        Assert.False(LibraryList.Matches(managed, "madden", null, null, false));
        Assert.True(LibraryList.Matches(unmanaged, null, "EA", null, false));
        Assert.False(LibraryList.Matches(managed, null, "ea", null, false));
        Assert.True(LibraryList.Matches(managed, "  ", null, null, false));   // a blank search is no search
    }

    [Fact]
    public void Tier_and_ban_risk_filters_never_pass_a_game_626_has_not_assessed()
    {
        // These are facts 626 works out about games it manages. A "ban-risk games" view listing an
        // unmanaged game would claim an assessment that was never made.
        var unmanaged = LibraryEntry.Of(Steam("1", "Some Shooter"));
        var risky = LibraryEntry.Of(Managed("r", "Risky", banRisk: "High", tier: EngineTier.EngineCurated));

        Assert.False(LibraryList.Matches(unmanaged, null, null, EngineTier.Unknown, false));
        Assert.False(LibraryList.Matches(unmanaged, null, null, null, banRiskOnly: true));
        Assert.True(LibraryList.Matches(risky, null, null, EngineTier.EngineCurated, banRiskOnly: true));
    }

    [Fact]
    public void A_store_install_launches_through_its_own_store()
    {
        Assert.Equal("steam://rungameid/1091500", StoreLaunch.UrlFor(Steam("1091500", "Cyberpunk 2077")));
        Assert.Equal("origin2://game/launch/?offerIds=Origin.OFR.50.0005",
            StoreLaunch.UrlFor(Ea("Origin.OFR.50.0005", "Madden NFL 27")));
    }

    [Fact]
    public void A_store_we_cannot_launch_through_gives_no_url()
    {
        Assert.Null(StoreLaunch.UrlFor(new InstalledGame("gog", "123", "X", "C:\\X")));
        Assert.Null(StoreLaunch.UrlFor(new InstalledGame("steam", " ", "X", "C:\\X")));
    }
}
