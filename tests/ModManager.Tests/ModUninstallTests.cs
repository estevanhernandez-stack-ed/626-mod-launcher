using ModManager.Core;

namespace ModManager.Tests;

/// <summary>E1, fifth slice: the uninstall rule and deletes the app's row and the agent share.</summary>
public class ModUninstallTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-uninstall-");
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    // The ME2 removal moved from the app's ModEngineService into Core (ModEngine2Writer.RemoveMod).
    [Fact]
    public void A_mod_engine_2_mod_loses_its_folder_and_its_config_line_and_keeps_the_others()
    {
        var gameRoot = Path.Combine(_root, "ELDEN RING");
        var me2 = Path.Combine(gameRoot, "ModEngine2");
        Directory.CreateDirectory(Path.Combine(me2, "mod", "ashes"));
        File.WriteAllText(Path.Combine(me2, "mod", "ashes", "a.dcx"), "x");
        Directory.CreateDirectory(Path.Combine(me2, "mod", "randomizer"));
        var cfg = Path.Combine(me2, "config_eldenring.toml");
        File.WriteAllText(cfg, """
[extension.mod_loader]
mods = [
    { enabled = true, name = "ashes", path = "mod/ashes" },
    { enabled = false, name = "rando", path = "mod/randomizer" }
]
""");
        var g = new GameEntry { Id = "er", GameName = "ER", Engine = "fromsoft", GameRoot = gameRoot, DataDir = Path.Combine(_root, "data"), ModEngineConfig = cfg };
        var ctx = Scanner.GameContext(g);
        var ashes = ModListing.Resolve(g).Single(m => m.Name == "ashes");

        Assert.Null(ModUninstall.Refusal(ctx, ashes));
        ModUninstall.Run(ctx, ashes);

        Assert.False(Directory.Exists(Path.Combine(me2, "mod", "ashes")));
        Assert.Equal(new[] { "rando" }, ModEngine2Config.ParseMods(File.ReadAllText(cfg)).Select(m => m.Name));
        Assert.True(Directory.Exists(Path.Combine(me2, "mod", "randomizer")));
    }

    // Review on #373: rows the listing appends are not installed mods; the scanner's uninstall can't find
    // them by name and the app reported "Uninstalled" having deleted nothing.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_proxy_loader_or_library_row_is_not_an_installed_mod(bool proxyLoader)
    {
        var row = proxyLoader
            ? new Mod { Name = "REFramework", Location = ProxyLoaderRows.LocationTag, IsLoader = true }
            : new Mod { Name = "UE4SS shared", Class = "library", Location = "mods" };

        Assert.Equal(UninstallBlock.NotAnInstalledMod, ModUninstall.Refusal(ListingMechanism.Scanner, row)!.Kind);
    }

    [Fact]
    public void A_family_with_one_refused_member_deletes_none_of_them()
    {
        var g = new GameEntry { Id = "g", GameName = "G", Engine = "ue-pak", GameRoot = _root, DataDir = Path.Combine(_root, "d"),
            ModLocations = new List<ModLocation> { new("mods", "Mods", "Mods") }, FileExtensions = new List<string> { "pak" } };
        Directory.CreateDirectory(Path.Combine(_root, "Mods"));
        var pak = Path.Combine(_root, "Mods", "Faster_5x_P.pak");
        File.WriteAllText(pak, "x");
        var ctx = Scanner.GameContext(g);
        var real = ModListing.Resolve(g).Single();
        var managed = new Mod { Name = "Faster_10x", ReadOnly = true, Files = new List<string> { "Faster_10x_P.pak" }, Location = "mods" };

        Assert.Throws<InvalidOperationException>(() => ModUninstall.RunAll(ctx, new[] { real, managed }));
        Assert.True(File.Exists(pak));
    }

    [Fact]
    public void A_mod_another_tool_manages_is_refused_and_running_it_throws()
    {
        var g = new GameEntry { Id = "g", GameName = "G", Engine = "ue-pak", GameRoot = _root, DataDir = Path.Combine(_root, "d"),
            ModLocations = new List<ModLocation> { new("mods", "Mods", "Mods") } };
        Directory.CreateDirectory(Path.Combine(_root, "Mods"));
        var ctx = Scanner.GameContext(g);
        var managed = new Mod { Name = "Managed", ReadOnly = true, Files = new List<string> { "Managed.pak" }, Location = "mods" };

        Assert.Equal(UninstallBlock.ManagedByAnotherTool, ModUninstall.Refusal(ctx, managed)!.Kind);
        Assert.Throws<InvalidOperationException>(() => ModUninstall.Run(ctx, managed));
    }
}
