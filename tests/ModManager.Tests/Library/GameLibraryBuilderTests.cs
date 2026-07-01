using ModManager.Core;
using ModManager.Core.Library;
using ModManager.Core.Recency;

namespace ModManager.Tests.Library;

public class GameLibraryBuilderTests
{
    private sealed class Src(Dictionary<string, DateTime> byId) : ILastPlayedSource
    { public string Name => "t";
      public LastPlayed? ForGame(GameRecencyKey k) => byId.TryGetValue(k.Id, out var d) ? new LastPlayed(d, null, "t") : null; }

    [Fact]
    public void Rows_are_ordered_most_recent_first_nulls_last_then_name()
    {
        var games = new[]
        {
            new GameEntry { Id = "a", GameName = "Alpha", GameRoot = "x" },
            new GameEntry { Id = "b", GameName = "Bravo", GameRoot = "x" },
            new GameEntry { Id = "c", GameName = "Charlie", GameRoot = "x" }, // no recency
        };
        var src = new Src(new() {
            ["a"] = new DateTime(2026,7,1,10,0,0,DateTimeKind.Utc),
            ["b"] = new DateTime(2026,7,1,12,0,0,DateTimeKind.Utc),
        });
        var rows = GameLibraryBuilder.Build(games, new ILastPlayedSource[]{src},
            _ => new GameModState(0,0,null), _ => EngineTier.Unknown, _ => null,
            _ => Array.Empty<string>(), _ => null);
        Assert.Equal(new[]{"b","a","c"}, rows.Select(r => r.Id).ToArray()); // b (12:00), a (10:00), c (null)
    }

    [Fact]
    public void Mod_state_and_tier_roll_up_onto_the_row()
    {
        var games = new[] { new GameEntry { Id = "a", GameName = "Alpha", GameRoot = "x" } };
        var rows = GameLibraryBuilder.Build(games, Array.Empty<ILastPlayedSource>(),
            _ => new GameModState(12, 8, "Ironman"), _ => EngineTier.EngineCurated, _ => "high",
            _ => new[]{"Mod Engine 2"}, _ => @"C:\cover.jpg");
        var r = Assert.Single(rows);
        Assert.Equal(12, r.ModCount); Assert.Equal(8, r.EnabledCount);
        Assert.Equal("Ironman", r.ActiveProfile); Assert.Equal(EngineTier.EngineCurated, r.Tier);
        Assert.Equal("high", r.BanRisk); Assert.Contains("Mod Engine 2", r.DetectedLoaders);
    }
}
