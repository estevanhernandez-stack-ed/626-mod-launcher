using ManifestMiner;

namespace ModManager.Tests.Miner;

public class Mo2GameParserTests
{
    private const string ValheimPy = """
    from ..basic_game import BasicGame

    class ValheimGame(BasicGame):
        Name = "Valheim Support Plugin"
        GameName = "Valheim"
        GameShortName = "valheim"
        GameNexusId = 3667
        GameSteamId = [892970, 896660, 1223920]
        GameBinary = "valheim.exe"
        GameDataPath = ""
    """;

    private const string Witcher3Py = """
    class Witcher3Game(BasicGame):
        GameName = "The Witcher 3"
        GameNexusName = "witcher3"
        GameSteamId = [499450, 292030]
        GameDataPath = "Mods"
    """;

    [Fact]
    public void Parses_name_steam_id_list_and_data_path()
    {
        var g = Mo2GameParser.Parse(ValheimPy);
        Assert.NotNull(g);
        Assert.Equal("Valheim", g!.GameName);
        Assert.Equal(new[] { "892970", "896660", "1223920" }, g.SteamIds.ToArray());
        Assert.Equal("", g.DataPath);          // empty is preserved (means "no mod path")
        Assert.Null(g.NexusName);              // only GameNexusId (numeric) here -> no slug
    }

    [Fact]
    public void Parses_nexus_name_slug_and_multi_steam_ids()
    {
        var g = Mo2GameParser.Parse(Witcher3Py);
        Assert.NotNull(g);
        Assert.Equal("witcher3", g!.NexusName);
        Assert.Equal("Mods", g.DataPath);
        Assert.Contains("292030", g.SteamIds);
    }

    [Fact]
    public void Returns_null_when_no_steam_id_present()
    {
        var g = Mo2GameParser.Parse("class X(BasicGame):\n    GameName = \"X\"\n");
        Assert.Null(g); // without a Steam id we can't key it onto the backbone
    }
}
