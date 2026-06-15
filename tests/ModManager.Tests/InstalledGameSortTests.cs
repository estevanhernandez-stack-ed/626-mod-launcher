using ModManager.Core;

namespace ModManager.Tests;

public class InstalledGameSortTests
{
    private static InstalledGame G(string name, string? lastPlayed)
        => new("steam", name, name, $@"C:\{name}") { LastPlayed = lastPlayed };

    [Fact]
    public void Orders_most_recently_played_first()
    {
        var sorted = InstalledGameSort.RecentlyPlayedFirst(new[] { G("Old", "100"), G("New", "900"), G("Mid", "500") });
        Assert.Equal(new[] { "New", "Mid", "Old" }, sorted.Select(g => g.Name).ToArray());
    }

    [Fact]
    public void Never_played_or_unparseable_fall_last_then_alphabetical()
    {
        var sorted = InstalledGameSort.RecentlyPlayedFirst(new[] { G("Zeb", null), G("Played", "100"), G("Abe", "notanumber") });
        Assert.Equal(new[] { "Played", "Abe", "Zeb" }, sorted.Select(g => g.Name).ToArray());
    }
}
