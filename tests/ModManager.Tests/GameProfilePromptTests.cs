using ModManager.Core;

namespace ModManager.Tests;

public class GameProfilePromptTests
{
    [Fact]
    public void Build_pins_the_contract_for_the_named_game()
    {
        var p = GameProfilePrompt.Build("Skyrim Special Edition");
        Assert.Contains("Skyrim Special Edition", p);
        Assert.Contains("engine", p);
        Assert.Contains("bethesda", p);          // an EnginePresets key is listed
        Assert.Contains("saveRoot", p);
        Assert.Contains("DocumentsMyGames", p);   // a save-root enum value is listed
        Assert.Contains("requiredLauncher", p);
        Assert.DoesNotContain("```", p);          // no markdown fences requested
    }

    [Fact]
    public void Build_asks_for_nexusGameDomain()
    {
        var p = GameProfilePrompt.Build("Cyberpunk 2077");
        Assert.Contains("nexusGameDomain", p);
        // The contract: a Nexus URL slug, not a numeric id.
        Assert.Contains("slug", p, StringComparison.OrdinalIgnoreCase);
    }
}
