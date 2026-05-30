using ModManager.Core;
using ModManager.Core.Persistence;

namespace ModManager.Tests.Persistence;

public class RegistryStoreTests
{
    [Fact]
    public void Save_then_Load_round_trips_as_camelCase()
    {
        var dir = TestSupport.TempDir("regstore-");
        var reg = new GameRegistry
        {
            Version = 1,
            ActiveGameId = "elden-ring",
            Games = new List<GameEntry> { new() { Id = "elden-ring", GameName = "ELDEN RING" } },
        };

        RegistryStore.Save(dir, reg);

        var json = File.ReadAllText(Path.Combine(dir, "games.json"));
        Assert.Contains("\"activeGameId\"", json);     // camelCase on disk (the launcher's convention)
        Assert.DoesNotContain("\"ActiveGameId\"", json);

        var loaded = RegistryStore.Load(dir);
        Assert.Equal("elden-ring", loaded.ActiveGameId);
        Assert.Single(loaded.Games);
        Assert.Equal("ELDEN RING", loaded.Games[0].GameName);
    }

    [Fact]
    public void Load_missing_file_returns_empty_registry()
    {
        var dir = TestSupport.TempDir("regstore-");
        var reg = RegistryStore.Load(dir);
        Assert.NotNull(reg);
        Assert.Empty(reg.Games);
    }
}
