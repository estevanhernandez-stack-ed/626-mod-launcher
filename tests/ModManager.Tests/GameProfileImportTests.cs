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
}
