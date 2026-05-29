using System.Text.Json;
using ModManager.Mcp;
using ModManager.Mcp.Tools;

namespace ModManager.Tests.Mcp;

// Validates the MCP read tools' marshaling + error shape against a seeded registry. The cores these
// project over (RegistryStore, Scanner) are unit-tested elsewhere; these lock the tool layer itself.
public class ReadToolsTests
{
    private static void SeedTwoGames()
    {
        var dir = TestSupport.TempDir("mcp-");
        var gamesJson =
            "{\"version\":1,\"activeGameId\":\"g1\",\"games\":[" +
            "{\"id\":\"g1\",\"gameName\":\"Game One\",\"engine\":\"fromsoft\",\"gameRoot\":\"C:/Games/G1\"}," +
            "{\"id\":\"g2\",\"gameName\":\"Game Two\",\"engine\":\"bepinex\",\"gameRoot\":\"C:/Games/G2\"}]}";
        File.WriteAllText(Path.Combine(dir, "games.json"), gamesJson);
        McpConfig.DataRoot = dir;
    }

    [Fact]
    public void ListGames_returns_registered_games_and_active_id()
    {
        SeedTwoGames();
        var json = JsonSerializer.Serialize(RegistryTools.ListGames());
        Assert.Contains("\"activeGameId\":\"g1\"", json);
        Assert.Contains("\"id\":\"g1\"", json);
        Assert.Contains("\"id\":\"g2\"", json);
        Assert.Contains("Game Two", json);
    }

    [Fact]
    public void GetActiveGame_returns_the_active_game()
    {
        SeedTwoGames();
        var json = JsonSerializer.Serialize(RegistryTools.GetActiveGame());
        Assert.Contains("\"id\":\"g1\"", json);
        Assert.Contains("Game One", json);
    }

    [Fact]
    public void GetModContext_unknown_game_returns_error_shape()
    {
        SeedTwoGames();
        var json = JsonSerializer.Serialize(ModTools.GetModContext("nope"));
        Assert.Contains("\"code\":\"unknown_game\"", json);
    }

    [Fact]
    public async Task ListMods_unknown_game_returns_error_shape()
    {
        SeedTwoGames();
        var json = JsonSerializer.Serialize(await ModTools.ListMods("nope"));
        Assert.Contains("\"code\":\"unknown_game\"", json);
    }

    [Fact]
    public async Task ListMods_fromsoft_returns_direct_inject_mods()
    {
        var game = FromSoftFixture.Build();
        FromSoftFixture.SeedRegistry(game);
        var json = JsonSerializer.Serialize(await ModTools.ListMods("er"));
        Assert.Contains("Seamless Co-op", json);
        Assert.Contains("Adjust The Fov", json);
        Assert.DoesNotContain("\"mods\":[]", json);
    }

    [Fact]
    public async Task ListMods_marshals_enrichment_fields()
    {
        var game = FromSoftFixture.Build();
        FromSoftFixture.SeedRegistry(game);
        var json = JsonSerializer.Serialize(await ModTools.ListMods("er"));
        Assert.Contains("\"displayTitle\":\"Seamless Co-op (Elden Ring)\"", json);
        Assert.Contains("\"author\":\"Yui\"", json);
        Assert.Contains("\"sourceUrl\":\"https://www.nexusmods.com/eldenring/mods/510\"", json);
    }
}
