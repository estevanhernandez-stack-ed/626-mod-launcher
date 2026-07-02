using ModManager.Core.Recency;

namespace ModManager.Tests.Recency;

public class RecencyLadderTests
{
    private sealed class Fake(string name, LastPlayed? r) : ILastPlayedSource
    { public string Name => name; public LastPlayed? ForGame(GameRecencyKey k) => r; }

    private static readonly GameRecencyKey Key = new(SteamAppId: "1", GameRoot: @"C:\g", LaunchExe: "g.exe", Id: "g");
    private static readonly DateTime T1 = new(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void First_source_with_last_played_wins()
    {
        var r = RecencyLadder.Merge(Key, new ILastPlayedSource[]
        {
            new Fake("own", new LastPlayed(T2, TimeSpan.FromHours(3), "own")),
            new Fake("steam", new LastPlayed(T1, null, "steam")),
        });
        Assert.Equal(T2, r.LastPlayedUtc);
        Assert.Equal(TimeSpan.FromHours(3), r.Playtime);
        Assert.Equal("own", r.Source);
    }

    [Fact]
    public void Playtime_falls_through_independently_of_last_played()
    {
        // own has last-played but no playtime; gog (later) has playtime -> take gog's playtime
        var r = RecencyLadder.Merge(Key, new ILastPlayedSource[]
        {
            new Fake("own", new LastPlayed(T2, null, "own")),
            new Fake("gog", new LastPlayed(T1, TimeSpan.FromHours(5), "gog")),
        });
        Assert.Equal(T2, r.LastPlayedUtc);           // own wins last-played
        Assert.Equal(TimeSpan.FromHours(5), r.Playtime); // gog supplies playtime
    }

    [Fact]
    public void All_miss_returns_none()
    {
        var r = RecencyLadder.Merge(Key, new ILastPlayedSource[] { new Fake("a", null), new Fake("b", LastPlayed.None) });
        Assert.Null(r.LastPlayedUtc);
        Assert.Null(r.Playtime);
    }

    [Fact]
    public void A_throwing_source_is_skipped_not_fatal()
    {
        var throwing = new ThrowingSource();
        var r = RecencyLadder.Merge(Key, new ILastPlayedSource[] { throwing, new Fake("steam", new LastPlayed(T1, null, "steam")) });
        Assert.Equal(T1, r.LastPlayedUtc);
    }
    private sealed class ThrowingSource : ILastPlayedSource
    { public string Name => "boom"; public LastPlayed? ForGame(GameRecencyKey k) => throw new InvalidOperationException(); }
}
