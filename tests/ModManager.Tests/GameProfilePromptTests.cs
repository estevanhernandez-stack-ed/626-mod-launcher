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

    [Fact]
    public void BuildMany_pins_the_contract_for_a_list_of_games()
    {
        var p = GameProfilePrompt.BuildMany(new[] { "Cyberpunk 2077", "Phasmophobia" });
        Assert.Contains("Cyberpunk 2077", p);
        Assert.Contains("Phasmophobia", p);
        Assert.Contains("JSON array", p);
        Assert.Contains("same order", p);         // order pinned so the user can map rows back
        Assert.Contains("nexusGameDomain", p);    // inherits the single-game contract
        Assert.Contains("saveRoot", p);
        Assert.Contains("DocumentsMyGames", p);   // a save-root enum value is listed
        Assert.Contains("bethesda", p);           // an EnginePresets key is listed
        Assert.DoesNotContain("```", p);
    }

    [Fact]
    public void BuildMany_rejects_empty_list()
    {
        Assert.Throws<ArgumentException>(() => GameProfilePrompt.BuildMany(Array.Empty<string>()));
    }
}
