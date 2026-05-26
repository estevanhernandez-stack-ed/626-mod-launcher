using ModManager.Core;

namespace ModManager.Tests;

public class Ue4ssLoadOrderTests
{
    [Fact]
    public async Task ApplyLoadOrder_writes_ue4ss_manifest_order_for_folder_mods()
    {
        var root = TestSupport.TempDir("ue4ss-lo-");
        var mods = Path.Combine(root, "ue4ss", "Mods");
        Directory.CreateDirectory(Path.Combine(mods, "Aaa"));
        Directory.CreateDirectory(Path.Combine(mods, "Bbb"));
        File.WriteAllText(Path.Combine(mods, "mods.txt"), "Aaa : 1\nBbb : 1\n");
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[] { new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" } },
        });

        await Scanner.ApplyLoadOrderAsync(c, new[] { "Bbb", "Aaa" });

        var first = File.ReadAllLines(Path.Combine(mods, "mods.txt")).First(l => l.Contains(" : ")).Trim();
        Assert.Equal("Bbb : 1", first);   // manifest reordered to match the requested order
    }

    [Fact]
    public async Task ApplyLoadOrder_ignores_ue4ss_mods_not_in_the_location()
    {
        // A pak mod + a UE4SS mod; the ue4ss SetOrder only sees the ue4ss folder's mods.
        var root = TestSupport.TempDir("ue4ss-lo2-");
        var paks = Path.Combine(root, "Paks", "~mods"); Directory.CreateDirectory(paks);
        File.WriteAllText(Path.Combine(paks, "Cool_P.pak"), "x");
        var mods = Path.Combine(root, "ue4ss", "Mods"); Directory.CreateDirectory(Path.Combine(mods, "Lua1"));
        File.WriteAllText(Path.Combine(mods, "mods.txt"), "Lua1 : 1\n");
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[]
            {
                new ModLocation("mods", "~mods", "Paks/~mods"),
                new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" },
            },
        });
        // Should not throw mixing pak + ue4ss keys.
        await Scanner.ApplyLoadOrderAsync(c, new[] { "Cool", "Lua1" });
        Assert.Contains("Lua1 : 1", File.ReadAllText(Path.Combine(mods, "mods.txt")));
    }

    [Fact]
    public async Task ApplyLoadOrder_does_not_reorder_an_owned_ue4ss_folder()
    {
        // A vortex.deployment.*.json in the folder makes ToolOwnership.Detect return Vortex.
        // SetOrder must NOT be called — the manifest must remain in its original order.
        var root = TestSupport.TempDir("ue4ss-lo3-");
        var mods = Path.Combine(root, "ue4ss", "Mods");
        Directory.CreateDirectory(Path.Combine(mods, "Aaa"));
        Directory.CreateDirectory(Path.Combine(mods, "Bbb"));
        File.WriteAllText(Path.Combine(mods, "mods.txt"), "Aaa : 1\nBbb : 1\n");
        // Plant a Vortex ownership marker so ToolOwnership.Detect returns Vortex.
        File.WriteAllText(Path.Combine(mods, "vortex.deployment.1.json"), "{}");

        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[] { new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" } },
        });

        await Scanner.ApplyLoadOrderAsync(c, new[] { "Bbb", "Aaa" });

        // Manifest must be unchanged — first entry is still Aaa.
        var first = File.ReadAllLines(Path.Combine(mods, "mods.txt")).First(l => l.Contains(" : ")).Trim();
        Assert.Equal("Aaa : 1", first);
    }
}
