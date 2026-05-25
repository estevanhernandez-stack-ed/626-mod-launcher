using ModManager.Core;

namespace ModManager.Tests;

// End-to-end: a folder with a Vortex marker yields read-only, vortex-tagged mods EVEN WHEN the
// profile never declared it managed; a plain folder stays ours (toggleable).
public class CoordinationScanTests
{
    [Fact]
    public async Task Detected_vortex_folder_makes_its_mods_readonly_even_if_undeclared()
    {
        var root = TestSupport.TempDir("coord-scan-");
        var scripts = Path.Combine(root, "ue4ss", "Mods");
        Directory.CreateDirectory(Path.Combine(scripts, "PetBoarPlus"));
        File.WriteAllText(Path.Combine(scripts, "vortex.deployment.x.json"), "{}"); // marker, but NOT declared
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[] { new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" } }, // no Managed
        });

        var mods = await Scanner.BuildModListAsync(c);
        var pet = mods.First(m => m.Name == "PetBoarPlus");
        Assert.True(pet.ReadOnly);
        Assert.Equal("vortex", pet.Managed);
    }

    [Fact]
    public async Task Plain_location_is_not_readonly()
    {
        var root = TestSupport.TempDir("coord-scan2-");
        var paks = Path.Combine(root, "Paks", "~mods");
        Directory.CreateDirectory(paks);
        File.WriteAllText(Path.Combine(paks, "Cool_P.pak"), "x");
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[] { new ModLocation("mods", "~mods", "Paks/~mods") },
        });

        var cool = (await Scanner.BuildModListAsync(c)).First(m => m.Name == "Cool");
        Assert.False(cool.ReadOnly);
        Assert.Null(cool.Managed);
    }
}
