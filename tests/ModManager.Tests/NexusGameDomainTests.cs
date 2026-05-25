using ModManager.Core;

namespace ModManager.Tests;

// Nexus keys games by a domain NAME ("windrose"), not a numeric id. It threads from the agent
// profile -> draft -> GameInput -> GameEntry so a game carries its Nexus domain for md5 lookup.
public class NexusGameDomainTests
{
    [Fact]
    public void BuildGameEntry_carries_the_nexus_game_domain()
    {
        var input = new GameInput { Name = "Windrose", Engine = "ue-pak", NexusGameDomain = "windrose" };
        var entry = EnginePresets.BuildGameEntry(input, existingIds: null);
        Assert.Equal("windrose", entry.NexusGameDomain);
    }

    [Fact]
    public void BuildGameEntry_leaves_nexus_domain_null_when_unset()
    {
        var entry = EnginePresets.BuildGameEntry(new GameInput { Name = "X", Engine = "ue-pak" }, null);
        Assert.Null(entry.NexusGameDomain);
    }

    [Fact]
    public void GameProfileImport_parses_nexus_game_domain()
    {
        var json = """
        {
          "name": "Windrose", "engine": "ue-pak",
          "saveRoot": "AppData", "saveSubPath": "Windrose/Saved",
          "nexusGameDomain": "windrose"
        }
        """;
        var result = GameProfileImport.Load(json);
        Assert.Empty(result.Errors);
        Assert.Equal("windrose", result.Draft!.NexusGameDomain);
    }
}
