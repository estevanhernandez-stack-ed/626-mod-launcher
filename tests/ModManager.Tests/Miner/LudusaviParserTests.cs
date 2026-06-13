using ManifestMiner;

namespace ModManager.Tests.Miner;

public class LudusaviParserTests
{
    // A trimmed sample of the real Ludusavi manifest shape: top-level key = game name.
    private const string Yaml = """
    An Example Game:
      files:
        <home>/Example/saves:
          tags:
            - save
      installDir:
        ExampleGame: {}
      steam:
        id: 12345
    Another Game:
      files:
        <home>/Another:
          tags:
            - save
      steam:
        id: 67890
    No Steam Game:
      files:
        <home>/NoSteam: {}
    """;

    [Fact]
    public void Parses_name_steam_id_and_save_paths()
    {
        var games = LudusaviParser.Parse(Yaml);

        var example = games.Single(g => g.Name == "An Example Game");
        Assert.Equal("12345", example.SteamAppId);
        Assert.Contains("ExampleGame", example.InstallDirs);
        Assert.Contains(example.SavePaths, p => p.Contains("Example/saves"));

        var noSteam = games.Single(g => g.Name == "No Steam Game");
        Assert.Null(noSteam.SteamAppId); // absent steam block -> null (normalize will drop it)
    }
}
