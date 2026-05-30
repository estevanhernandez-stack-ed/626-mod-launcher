using ModManager.Core;

namespace ModManager.Tests;

internal static class FromSoftFixture
{
    /// <summary>Builds an on-disk FromSoft game and returns its GameEntry (engine fromsoft, GameRoot +
    /// pinned DataDir). metadata.json is keyed by the Seamless detection name so MergeMetadata hits.</summary>
    public static GameEntry Build()
    {
        var root = TestSupport.TempDir("fs-");
        var play = Path.Combine(root, "Game");
        Directory.CreateDirectory(Path.Combine(play, "SeamlessCoop"));
        File.WriteAllText(Path.Combine(play, "SeamlessCoop", "seamlesscoopsettings.ini"), "x");
        File.WriteAllText(Path.Combine(play, "dinput8.dll"), "x");
        Directory.CreateDirectory(Path.Combine(play, "mods"));
        File.WriteAllText(Path.Combine(play, "mods", "AdjustTheFov.dll"), "x");

        var dataDir = Path.Combine(root, "_626mods", "er");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "metadata.json"),
            "{\"Seamless Co-op\":{\"title\":\"Seamless Co-op (Elden Ring)\"," +
            "\"author\":\"Yui\",\"url\":\"https://www.nexusmods.com/eldenring/mods/510\"}}");

        return new GameEntry
        {
            Id = "er", GameName = "ELDEN RING", Engine = "fromsoft",
            GameRoot = root, DataDir = dataDir,
        };
    }

    /// <summary>Write a games.json registry containing this game and point McpConfig at it (for MCP tests).</summary>
    public static void SeedRegistry(GameEntry game)
    {
        var dir = TestSupport.TempDir("reg-");
        var gameRoot = game.GameRoot!.Replace("\\", "/");
        var dataDir = game.DataDir!.Replace("\\", "/");
        File.WriteAllText(Path.Combine(dir, "games.json"),
            "{\"version\":1,\"activeGameId\":\"er\",\"games\":[" +
            "{\"id\":\"er\",\"gameName\":\"ELDEN RING\",\"engine\":\"fromsoft\"," +
            "\"gameRoot\":\"" + gameRoot + "\",\"dataDir\":\"" + dataDir + "\"}]}");
        ModManager.Mcp.McpConfig.DataRoot = dir;
    }
}
