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

    [Fact]
    public void Loads_an_override_that_has_no_Steam_app_id()
    {
        // A game bought from the EA app, Epic or GOG has no Steam id. Before this, the loader
        // dropped it here and the merge dropped it again - two silent gates, no report.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "some-ea-game.json"),
            "{ \"id\": \"some-ea-game\", \"name\": \"Some EA Game\", \"engine\": \"custom\" }");

        var loaded = OverridesLoader.Load(_dir);

        var entry = Assert.Single(loaded);
        Assert.Equal("some-ea-game", entry.Id);
        Assert.Null(entry.SteamAppId);
    }

    [Fact]
    public void An_override_with_neither_an_id_nor_a_Steam_id_is_still_dropped()
    {
        // There would be nothing to key it on. Task 3's gate reports this; the loader just
        // refuses to produce an entry that cannot be addressed.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "nameless.json"), "{ \"engine\": \"custom\" }");

        Assert.Empty(OverridesLoader.Load(_dir));
    }

    [Fact]
    public void A_loaded_override_remembers_its_file_so_a_problem_can_name_it()
    {
        // "Two overrides collide" is not actionable without both file names. The path is set by the
        // loader rather than parsed from JSON - it is not a curated field and must not be settable
        // from a file.
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "skyrim.json");
        File.WriteAllText(path, "{ \"steamAppId\": \"72850\", \"engine\": \"bethesda\" }");

        var entry = Assert.Single(OverridesLoader.Load(_dir));

        Assert.Equal(path, entry.SourcePath);
    }
}
