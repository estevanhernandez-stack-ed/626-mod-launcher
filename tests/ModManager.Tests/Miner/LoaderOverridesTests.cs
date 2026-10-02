using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Miner;

// Curated loaders live in overrides/loaders/*.json, one per file. The game-override loader reads only
// the top level of overrides/, so the subfolder cannot be mistaken for a game.
public class LoaderOverridesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ovr-loaders-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private string LoadersDir => Path.Combine(_dir, "loaders");

    private void Write(string file, string json)
    {
        Directory.CreateDirectory(LoadersDir);
        File.WriteAllText(Path.Combine(LoadersDir, file), json);
    }

    private const string Seamless = """
        {
          "id": "seamless-coop",
          "displayName": "Seamless Co-op",
          "engine": "fromsoft",
          "steamAppId": "1245620",
          "launcherExeNames": ["launch_elden_ring_seamlesscoop.exe", "ersc_launcher.exe"],
          "getUrl": "https://www.nexusmods.com/eldenring/mods/510",
          "author": "LukeYui",
          "banSafe": true
        }
        """;

    [Fact]
    public void Loads_each_loader_file_in_the_loaders_subfolder()
    {
        Write("seamless-coop.json", Seamless);

        var loaded = LoaderOverrides.Load(_dir);

        var l = Assert.Single(loaded);
        Assert.Equal("seamless-coop", l.Id);
        Assert.Equal(new[] { "launch_elden_ring_seamlesscoop.exe", "ersc_launcher.exe" }, l.LauncherExeNames);
        Assert.True(l.BanSafe);
    }

    [Fact]
    public void No_loaders_subfolder_means_no_loaders()
        => Assert.Empty(LoaderOverrides.Load(_dir));

    [Fact]
    public void The_game_override_loader_does_not_read_the_loaders_subfolder()
    {
        Write("seamless-coop.json", Seamless);

        Assert.Empty(OverridesLoader.Load(_dir));
    }

    [Fact]
    public void A_malformed_loader_file_is_skipped_without_throwing()
    {
        Write("broken.json", "{ not json");
        Write("seamless-coop.json", Seamless);

        Assert.Single(LoaderOverrides.Load(_dir));
    }

    // Same stance as duplicate game keys: one curated file silently losing is a build failure, not a
    // resolved conflict.
    [Fact]
    public void Two_files_claiming_one_loader_id_are_a_problem()
    {
        var loaders = new[]
        {
            new LoaderManifestEntry { Id = "seamless-coop" },
            new LoaderManifestEntry { Id = "Seamless-Coop" },
            new LoaderManifestEntry { Id = "mod-engine-2" },
        };

        var problems = LoaderOverrides.Check(loaders);

        var p = Assert.Single(problems);
        Assert.Contains("seamless-coop", p.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Applying_loaders_replaces_the_drafts_loader_list()
    {
        var draft = new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "elden-ring", Name = "Elden Ring" } },
        };

        var applied = LoaderOverrides.Apply(draft, new[] { new LoaderManifestEntry { Id = "seamless-coop" } });

        Assert.Single(applied.Games);
        Assert.Equal("seamless-coop", Assert.Single(applied.Loaders).Id);
    }

    // PublishManifest reshapes games for the feed; it must carry the loaders through untouched.
    [Fact]
    public void Publishing_keeps_the_loaders()
    {
        var draft = new GameManifest { Loaders = new[] { new LoaderManifestEntry { Id = "seamless-coop" } } };

        Assert.Single(PublishManifest.ForPublish(draft).Loaders);
    }

    // Review on #356: the miner is built from the launcher's own source, so a field it does not know is
    // a typo, and the launcher would skip the loader for carrying it. That stops the run.
    [Fact]
    public void A_loader_file_with_an_unknown_field_is_a_problem()
    {
        Write("typo.json", """
            { "id": "typo", "displayName": "Typo", "engine": "frostbite", "gameId": ["madden-nfl-27"],
              "launcherExeNames": ["t.exe"], "getUrl": "https://example.com/t" }
            """);

        var p = Assert.Single(LoaderOverrides.Check(LoaderOverrides.Load(_dir)));
        Assert.Contains("gameId", p.Message);
    }

    // Review on #356: a pin to a game the feed does not carry pins the loader to nothing.
    [Fact]
    public void A_pin_to_a_game_the_feed_does_not_have_is_a_problem()
    {
        var loaders = new[] { new LoaderManifestEntry { Id = "pinned", GameIds = new[] { "madden-nfl-26" } } };
        var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "madden-nfl-27" };

        var p = Assert.Single(LoaderOverrides.Check(loaders, games));
        Assert.Contains("madden-nfl-26", p.Message);
        Assert.Empty(LoaderOverrides.Check(
            new[] { new LoaderManifestEntry { Id = "pinned", GameIds = new[] { "madden-nfl-27" } } }, games));
    }
}
