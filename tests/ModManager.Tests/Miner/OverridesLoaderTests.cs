using ManifestMiner;

namespace ModManager.Tests.Miner;

public class OverridesLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ovr-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [Fact]
    public void Loads_all_json_overrides_in_the_directory()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "skyrim.json"),
            "{ \"steamAppId\": \"72850\", \"engine\": \"bethesda\", \"modPath\": \"Data\" }");
        File.WriteAllText(Path.Combine(_dir, "oblivion.json"),
            "{ \"steamAppId\": \"22330\", \"engine\": \"bethesda\", \"modPath\": \"Data\" }");

        var loaded = OverridesLoader.Load(_dir);

        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, o => o.SteamAppId == "72850" && o.Engine == "bethesda");
    }

    [Fact]
    public void Missing_directory_returns_empty()
        => Assert.Empty(OverridesLoader.Load(Path.Combine(_dir, "nope")));

    [Fact]
    public void Skips_malformed_files_without_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "good.json"), "{ \"steamAppId\": \"1\", \"engine\": \"smapi\" }");
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{ not valid json");

        var loaded = OverridesLoader.Load(_dir);
        Assert.Single(loaded);
        Assert.Equal("1", loaded[0].SteamAppId);
    }
}
