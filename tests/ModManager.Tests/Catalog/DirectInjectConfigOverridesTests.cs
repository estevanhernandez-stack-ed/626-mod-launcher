using ModManager.Core.Catalog;

namespace ModManager.Tests.Catalog;

public class DirectInjectConfigOverridesTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "di-overrides-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void Load_returns_empty_when_file_missing()
    {
        var gameData = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(gameData);

        var result = DirectInjectConfigOverrides.Load(gameData);

        Assert.Empty(result.OverridesByModId);
    }

    [Fact]
    public void Save_then_Load_round_trips_camelCase()
    {
        var gameData = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(gameData);

        var pre = new DirectInjectConfigOverrides(new Dictionary<string, Dictionary<string, string>>
        {
            ["seamless-coop"] = new()
            {
                ["SeamlessCoop/seamlesscoopsettings.ini"] = "D:/elsewhere/seamless.ini",
            },
        });

        DirectInjectConfigOverrides.Save(gameData, pre);

        var path = Path.Combine(gameData, "direct-inject", "config-overrides.json");
        Assert.True(File.Exists(path));
        var json = File.ReadAllText(path);
        Assert.Contains("\"overridesByModId\":", json);

        var post = DirectInjectConfigOverrides.Load(gameData);
        Assert.Equal("D:/elsewhere/seamless.ini",
            post.OverridesByModId["seamless-coop"]["SeamlessCoop/seamlesscoopsettings.ini"]);
    }

    [Fact]
    public void Load_tolerates_unreadable_JSON_by_returning_empty()
    {
        var gameData = Path.Combine(_tmp, "GameData");
        var dir = Path.Combine(gameData, "direct-inject");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config-overrides.json"), "{ not valid json");

        var result = DirectInjectConfigOverrides.Load(gameData);

        Assert.Empty(result.OverridesByModId);
    }
}
