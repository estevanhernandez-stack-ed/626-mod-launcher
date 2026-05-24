using ModManager.Core;

namespace ModManager.Tests;

// Pure save-location candidate generation: combine the OS folder roots with engine/game
// patterns. The App checks which candidate actually exists; this just builds the ranked list.
public class SaveLocationsTests
{
    private static SaveLocations.SaveRoots Roots() => new(
        Documents: @"C:\Users\mom\Documents",
        LocalAppData: @"C:\Users\mom\AppData\Local",
        AppData: @"C:\Users\mom\AppData\Roaming");

    [Fact]
    public void Bethesda_first_candidate_is_documents_my_games_saves()
    {
        var c = SaveLocations.Guess(Roots(), "Skyrim Special Edition", "bethesda");
        Assert.Equal(@"C:\Users\mom\Documents\My Games\Skyrim Special Edition\Saves", c[0]);
    }

    [Fact]
    public void Unreal_first_candidate_is_localappdata_saved_savegames()
    {
        var c = SaveLocations.Guess(Roots(), "Windrose", "ue-pak");
        Assert.Equal(@"C:\Users\mom\AppData\Local\Windrose\Saved\SaveGames", c[0]);
    }

    [Fact]
    public void Unknown_engine_falls_back_to_generic_candidates()
    {
        var c = SaveLocations.Guess(Roots(), "Some Game", null);
        Assert.Contains(@"C:\Users\mom\Documents\My Games\Some Game", c);
        Assert.Contains(@"C:\Users\mom\AppData\Roaming\Some Game", c);
    }

    [Fact]
    public void Candidates_are_distinct()
    {
        var c = SaveLocations.Guess(Roots(), "Dup", "bethesda");
        Assert.Equal(c.Count, c.Distinct().Count());
    }
}
