using ModManager.Core;

namespace ModManager.Tests;

public class PaksRootPresetTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "paksroot-preset-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    // A UE game with <Project>/Content/Paks on disk; optionally a ~mods subfolder (loader present).
    private string MakeUeGame(string project, bool withModsSubfolder)
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot-" + Guid.NewGuid().ToString("n"));
        var paks = Path.Combine(gameRoot, project, "Content", "Paks");
        Directory.CreateDirectory(paks);
        if (withModsSubfolder) Directory.CreateDirectory(Path.Combine(paks, "~mods"));
        return gameRoot;
    }

    private static GameInput UeInput(string gameRoot) => new()
    {
        Name = "TestUE", Engine = "ue-pak", GameRoot = gameRoot,
    };

    [Fact]
    public void Loaderless_game_gets_a_paks_root_location_at_Content_Paks()
    {
        var gameRoot = MakeUeGame("Witchfire", withModsSubfolder: false);
        var entry = EnginePresets.BuildGameEntry(UeInput(gameRoot), existingIds: null);
        var loc = entry.ModLocations.Single();
        Assert.Equal("paks-root", loc.Form);
        Assert.Equal("Witchfire/Content/Paks", loc.Path.Replace('\\', '/'));
    }

    [Fact]
    public void Loader_game_with_a_mods_subfolder_keeps_the_mods_location()
    {
        var gameRoot = MakeUeGame("R5", withModsSubfolder: true);
        var entry = EnginePresets.BuildGameEntry(UeInput(gameRoot), existingIds: null);
        var loc = entry.ModLocations.Single();
        Assert.Null(loc.Form);   // not paks-root — the loader ~mods convention
        Assert.EndsWith("Content/Paks/~mods", loc.Path.Replace('\\', '/'));
    }

    [Fact]
    public void Explicit_user_modpath_is_respected_not_overridden_by_detection()
    {
        var gameRoot = MakeUeGame("Witchfire", withModsSubfolder: false);
        var input = new GameInput
        {
            Name = "TestUE", Engine = "ue-pak", GameRoot = gameRoot, ModPath = "Custom/Mods",
        };
        var entry = EnginePresets.BuildGameEntry(input, existingIds: null);
        var loc = entry.ModLocations.Single();
        Assert.Equal("Custom/Mods", loc.Path);
        Assert.Null(loc.Form);   // user override → no paks-root auto-detection
    }

    [Fact]
    public void Non_ue_pak_engine_is_unaffected()
    {
        var input = new GameInput { Name = "Sky", Engine = "bethesda", GameRoot = _tmp };
        var entry = EnginePresets.BuildGameEntry(input, existingIds: null);
        Assert.Equal("Data", entry.ModLocations.Single().Path);
        Assert.Null(entry.ModLocations.Single().Form);
    }

    [Fact]
    public void No_gameroot_falls_back_to_the_static_preset_path()
    {
        var input = new GameInput { Name = "UE", Engine = "ue-pak", GameRoot = null };
        var entry = EnginePresets.BuildGameEntry(input, existingIds: null);
        // No disk to detect against → static preset path, no paks-root.
        Assert.EndsWith("Content/Paks/~mods", entry.ModLocations.Single().Path.Replace('\\', '/'));
        Assert.Null(entry.ModLocations.Single().Form);
    }
}
