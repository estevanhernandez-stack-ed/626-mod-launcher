using ModManager.Core;

namespace ModManager.Tests;

// Per-location form: a game can mix pak-FILE locations (~mods) with FOLDER locations (UE4SS Lua
// mods), and a Managed location (Vortex/MO2) surfaces its mods read-only — flag, don't manage.
public class ScannerLocationFormTests
{
    private static GameContext Mixed()
    {
        var root = TestSupport.TempDir("locform-");
        var paks = Path.Combine(root, "Paks", "~mods");
        var scripts = Path.Combine(root, "ue4ss", "Mods");
        Directory.CreateDirectory(paks);
        Directory.CreateDirectory(Path.Combine(scripts, "PetBoarPlus"));
        Directory.CreateDirectory(Path.Combine(scripts, "SplitScreenMod"));
        File.WriteAllText(Path.Combine(paks, "Cool_P.pak"), "x");
        return Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[]
            {
                new ModLocation("mods", "~mods", "Paks/~mods"),
                new ModLocation("ue4ss", "UE4SS Scripts", "ue4ss/Mods") { Form = "folders", Managed = "vortex" },
            },
        });
    }

    [Fact]
    public async Task Mixed_locations_list_pak_files_and_script_folders()
    {
        var mods = await Scanner.BuildModListAsync(Mixed());
        Assert.Contains(mods, m => m.Name == "Cool" && !m.IsFolder && m.Location == "mods");
        Assert.Contains(mods, m => m.Name == "PetBoarPlus" && m.IsFolder && m.Location == "ue4ss");
        Assert.Contains(mods, m => m.Name == "SplitScreenMod" && m.IsFolder);
    }

    [Fact]
    public async Task Managed_location_marks_its_mods_managed()
    {
        var mods = await Scanner.BuildModListAsync(Mixed());
        Assert.Equal("vortex", mods.First(m => m.Name == "PetBoarPlus").Managed);
        Assert.Null(mods.First(m => m.Name == "Cool").Managed); // pak mod is ours, not managed
    }

    [Fact]
    public async Task Folder_form_mod_is_not_falsely_flagged_hasVortexFolder()
    {
        // ScanSubfolders defaults to "warn"; the vortex-stash heuristic must skip folder-form
        // locations (the subfolders ARE the mods there, not a Vortex stash beside a pak).
        var mods = await Scanner.BuildModListAsync(Mixed());
        Assert.False(mods.First(m => m.Name == "PetBoarPlus").HasVortexFolder);
    }

    [Fact]
    public async Task ByFolder_grouping_still_defaults_a_location_to_folders()
    {
        // Elden Ring shape: groupingRule by_folder, single location, folders are the mods.
        var root = TestSupport.TempDir("byfolder-");
        Directory.CreateDirectory(Path.Combine(root, "mod", "SeamlessCoop"));
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "er", GameName = "ER", GameRoot = root,
            GroupingRule = "by_folder",
            ModLocations = new[] { new ModLocation("mods", "mods", "mod") },
        });
        var mods = await Scanner.BuildModListAsync(c);
        Assert.Contains(mods, m => m.Name == "SeamlessCoop" && m.IsFolder);
    }
}
