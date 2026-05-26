using ModManager.Core;

namespace ModManager.Tests;

public class GameProfileImportTests
{
    private const string Valid = """
    { "name":"Elden Ring","engine":"fromsoft","steamAppId":"1245620",
      "modPath":"Game/mod","saveRoot":"AppData","saveSubPath":"EldenRing",
      "requiredLauncher":"Game/ersc_launcher.exe" }
    """;

    [Fact]
    public void Valid_profile_loads_with_no_errors()
    {
        var r = GameProfileImport.Load(Valid);
        Assert.Empty(r.Errors);
        Assert.NotNull(r.Draft);
        Assert.Equal("Elden Ring", r.Draft!.Name);
        Assert.Equal("fromsoft", r.Draft.Engine);
        Assert.Equal("AppData", r.Draft.SaveRoot);
        Assert.Equal("Game/ersc_launcher.exe", r.Draft.RequiredLauncher);
    }

    [Fact]
    public void Bad_json_is_rejected_with_a_reason()
    {
        var r = GameProfileImport.Load("{ not json ");
        Assert.Null(r.Draft);
        Assert.NotEmpty(r.Errors);
    }

    [Fact]
    public void Unknown_engine_is_rejected_listing_allowed_keys()
    {
        var r = GameProfileImport.Load("""{ "name":"X","engine":"frostbite","saveRoot":"AppData","saveSubPath":"X" }""");
        Assert.Contains(r.Errors, e => e.Contains("engine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SaveRoot_outside_the_enum_is_rejected()
    {
        var r = GameProfileImport.Load("""{ "name":"X","engine":"bethesda","saveRoot":"Desktop","saveSubPath":"X" }""");
        Assert.Contains(r.Errors, e => e.Contains("saveRoot", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("C:/abs/mod")]   // absolute
    [InlineData("../escape")]    // traversal
    [InlineData("/rooted")]      // drive-rooted
    public void Absolute_or_traversal_paths_are_rejected(string modPath)
    {
        var json = $$"""{ "name":"X","engine":"bethesda","saveRoot":"AppData","saveSubPath":"X","modPath":"{{modPath}}" }""";
        var r = GameProfileImport.Load(json);
        Assert.Contains(r.Errors, e => e.Contains("path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_required_fields_are_rejected()
    {
        var r = GameProfileImport.Load("""{ "engine":"bethesda" }"""); // no name/saveRoot/saveSubPath
        Assert.Contains(r.Errors, e => e.Contains("name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(r.Errors, e => e.Contains("saveRoot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Non_numeric_steamAppId_is_rejected_but_absent_is_ok()
    {
        Assert.Contains(GameProfileImport.Load("""{ "name":"X","engine":"bethesda","saveRoot":"AppData","saveSubPath":"X","steamAppId":"abc" }""").Errors,
            e => e.Contains("steamAppId", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(GameProfileImport.Load("""{ "name":"X","engine":"bethesda","saveRoot":"AppData","saveSubPath":"X" }""").Errors);
    }

    [Fact]
    public void BuildGameEntry_carries_the_required_launcher()
    {
        var input = new GameInput { Name = "Elden Ring", Engine = "fromsoft", GameRoot = @"C:\game",
            RequiredLauncher = "Game/ersc_launcher.exe" };
        var entry = EnginePresets.BuildGameEntry(input, existingIds: null);
        Assert.Equal("Game/ersc_launcher.exe", entry.RequiredLauncher);
        Assert.Equal("fromsoft", entry.Engine);
    }

    [Fact]
    public void LoadMany_parses_each_element_with_its_own_result()
    {
        var json = """
        [
          { "name":"A","engine":"bethesda","saveRoot":"AppData","saveSubPath":"A" },
          { "name":"B","engine":"frostbite","saveRoot":"AppData","saveSubPath":"B" }
        ]
        """;
        var results = GameProfileImport.LoadMany(json);
        Assert.Equal(2, results.Count);
        Assert.Empty(results[0].Errors);
        Assert.Equal("A", results[0].Draft!.Name);
        Assert.NotEmpty(results[1].Errors); // frostbite is not an engine preset
        Assert.Null(results[1].Draft);
    }

    [Fact]
    public void LoadMany_rejects_non_array_root_with_one_error()
    {
        var results = GameProfileImport.LoadMany("""{ "name":"X" }""");
        Assert.Single(results);
        Assert.Null(results[0].Draft);
        Assert.Contains(results[0].Errors, e => e.Contains("array", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadMany_rejects_bad_json_with_one_error()
    {
        var results = GameProfileImport.LoadMany("[ not json");
        Assert.Single(results);
        Assert.Null(results[0].Draft);
        Assert.NotEmpty(results[0].Errors);
    }

    [Fact]
    public void LoadMany_returns_empty_for_an_empty_array()
    {
        var results = GameProfileImport.LoadMany("[]");
        Assert.Empty(results);
    }

    [Fact]
    public void Load_carries_nexusGameDomain_when_present()
    {
        var json = """{ "name":"Cyberpunk 2077","engine":"custom","saveRoot":"DocumentsMyGames","saveSubPath":"CD Projekt Red/Cyberpunk 2077","nexusGameDomain":"cyberpunk2077" }""";
        var r = GameProfileImport.Load(json);
        Assert.Empty(r.Errors);
        Assert.Equal("cyberpunk2077", r.Draft!.NexusGameDomain);
    }

    [Fact]
    public void BuildGameEntry_carries_the_full_agent_profile_fields()
    {
        var input = new GameInput
        {
            Name = "Custom Game",
            Engine = "ue-pak",
            GameRoot = @"C:\game",
            WindowTitle = "W",
            FileExtensions = new[] { "pak" },
            GroupingRule = "by_folder",
            CurseforgeGameId = 12345,
        };
        var entry = EnginePresets.BuildGameEntry(input, existingIds: null);
        Assert.Equal(12345, entry.CurseforgeGameId);
        Assert.Equal("W", entry.WindowTitle);
        Assert.Equal(new[] { "pak" }, entry.FileExtensions);
        Assert.Equal("by_folder", entry.GroupingRule);
    }
}
