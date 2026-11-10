using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Vanilla mode read as a broken install (AC8 + HomePlay smoke, 2026-11-09). Play vanilla moves the loaders
/// aside on purpose, and three surfaces then reported the launcher's own step-aside as a fault:
/// <list type="bullet">
/// <item>the strip's red FRAMEWORK chip: "Missing: UE4SS, runtime present, loader missing" with a "Get it"
/// download link, on ACE COMBAT 8, whose dwmapi.dll was sitting in the framework's holding folder;</item>
/// <item>the same chip naming Elden Mod Loader on Elden Ring, whose dinput8.dll was in vanilla-proxy;</item>
/// <item>the LAUNCH OPTION chip on Elden Ring, because Seamless reads as not installed while it is set aside.</item>
/// </list>
/// A loader the stash names and holds is not missing. A loader that was never there still is.
/// </summary>
public class VanillaModeStateTests
{
    private static GameEntry Game(string root, string engine, string modPath) => new()
    {
        Id = "g",
        GameName = "Test",
        Engine = engine,
        GameRoot = root,
        DataDir = Path.Combine(root, "_data"),
        ModLocations = new[] { new ModLocation("mods", "mods", modPath) },
        GroupingRule = "filename_no_ext",
        FileExtensions = new[] { "pak" },
    };

    private static void Put(string root, string rel)
    {
        var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
    }

    private static FrameworkPresence Ue4ss(GameContext ctx, VanillaStash? stash)
        => FrameworkDeps.Check(ctx, stash).Single(p => p.Dep.Name == "UE4SS");

    [Fact]
    public void A_UE4SS_loader_the_stash_holds_is_not_missing()
    {
        var root = TestSupport.TempDir("vmode-");
        Put(root, "Game/Binaries/Win64/ue4ss/UE4SS.dll");
        var ctx = Scanner.GameContext(Game(root, "ue-pak", "Game/Content/Paks/~mods"));
        Put(ctx.DataDir, "frameworks/ue4ss/disabled-proxy/dwmapi.dll");   // where Play vanilla put it

        Assert.False(Ue4ss(ctx, null).IsPresent);   // a live probe alone still says loader missing
        Assert.True(Ue4ss(ctx, new VanillaStash { Frameworks = { "ue4ss" } }).IsPresent);
    }

    [Fact]
    public void A_held_file_the_stash_does_not_name_does_not_count()
    {
        var root = TestSupport.TempDir("vmode-");
        Put(root, "Game/Binaries/Win64/ue4ss/UE4SS.dll");
        var ctx = Scanner.GameContext(Game(root, "ue-pak", "Game/Content/Paks/~mods"));
        Put(ctx.DataDir, "frameworks/ue4ss/disabled-proxy/dwmapi.dll");

        Assert.False(Ue4ss(ctx, new VanillaStash()).IsPresent);
        Assert.False(Ue4ss(ctx, new VanillaStash { Frameworks = { "other" } }).IsPresent);
    }

    [Fact]
    public void A_framework_that_was_never_installed_is_still_missing_in_vanilla_mode()
    {
        var root = TestSupport.TempDir("vmode-");
        var ctx = Scanner.GameContext(Game(root, "ue-pak", "Game/Content/Paks/~mods"));

        Assert.False(Ue4ss(ctx, new VanillaStash { Frameworks = { "ue4ss" } }).IsPresent);
    }

    [Fact]
    public void A_stepped_aside_Elden_Mod_Loader_is_not_missing_but_Mod_Engine_2_still_is()
    {
        var root = TestSupport.TempDir("vmode-");
        Put(root, "Game/eldenring.exe");
        Put(root, "Game/dinput8.dll");
        var game = Game(root, "fromsoft", "Game/mods");
        ModToggle.SetProxyLoaderEnabled(game, "dinput8.dll", enabled: false);   // the real step-aside
        var ctx = Scanner.GameContext(game);
        var stash = new VanillaStash { DirectInjectProxies = { "dinput8.dll" } };

        var missing = FrameworkDeps.Check(ctx, stash).Where(p => !p.IsPresent).Select(p => p.Dep.Name).ToList();

        Assert.DoesNotContain("Elden Mod Loader", missing);
        Assert.Contains("Mod Engine 2", missing);   // never installed: the one real gap stays visible
        Assert.Contains("Elden Mod Loader",
            FrameworkDeps.Check(ctx).Where(p => !p.IsPresent).Select(p => p.Dep.Name));
    }

    [Fact]
    public void An_Elden_Mod_Loader_held_as_a_direct_inject_ROW_is_not_missing()
    {
        // The live shape, which the proxy test above is not: on the real Elden Ring install the loader is the
        // "DLL mod loader" row, so Play vanilla moved it through the row lane into direct-disabled/<slug>/ and the
        // stash recorded it under modRows with directInjectProxies empty. The first cut of the fix looked only at
        // proxies and frameworks, passed every test, and left the chip up on the live retest.
        var root = TestSupport.TempDir("vmode-");
        Put(root, "Game/eldenring.exe");
        Put(root, "Game/dinput8.dll");
        var game = Game(root, "fromsoft", "Game/mods");
        var play = DirectInjectListing.PlayFolder(root)!;
        DirectInject.Disable(play, DirectInjectListing.Holding(game),
            new DirectInjectMod("DLL mod loader", "loader", "dinput8.dll", new[] { "dinput8.dll" }));
        var ctx = Scanner.GameContext(game);
        var stash = new VanillaStash { ModRows = { new StashedModRow { Name = "DLL mod loader", Location = "direct-inject" } } };

        static IEnumerable<string> Missing(IReadOnlyList<FrameworkPresence> p) => p.Where(x => !x.IsPresent).Select(x => x.Dep.Name);

        Assert.Contains("Elden Mod Loader", Missing(FrameworkDeps.Check(ctx)));
        Assert.DoesNotContain("Elden Mod Loader", Missing(FrameworkDeps.Check(ctx, stash)));
        // A row the stash does not name is somebody's turned-off mod, not a loader vanilla set aside.
        var other = new VanillaStash { ModRows = { new StashedModRow { Name = "ReShade", Location = "direct-inject" } } };
        Assert.Contains("Elden Mod Loader", Missing(FrameworkDeps.Check(ctx, other)));
    }

    [Fact]
    public void Stepped_aside_silences_the_launch_option_chip_and_nothing_else()
    {
        var c = new GameStateConditions { LaunchOptionsNeeded = true, BanRisk = true, SteppedAside = true };
        var ids = GameStateStrip.For(c).Select(x => x.Id).ToList();

        Assert.DoesNotContain("launch-options", ids);
        Assert.Contains("ban-risk", ids);
        Assert.Contains("launch-options",
            GameStateStrip.For(c with { SteppedAside = false }).Select(x => x.Id));
    }

    private static readonly LaunchTarget Seamless = new("Play (Seamless Co-op)", "exe", @"C:\g\Game\ersc_launcher.exe");
    private static readonly LaunchTarget Offline = new("Play modded (offline, no anti-cheat)", "exe", @"C:\g\AceCombat8.exe")
    { SkipsAntiCheat = true };
    private static readonly LaunchTarget Steam = new("Play vanilla (Steam)", "steam", "steam://rungameid/1");

    [Fact]
    public void The_vanilla_menu_offers_only_the_ways_in_vanilla_Play_would_use()
    {
        var all = new[] { Seamless, Offline, Steam };

        Assert.Equal(new[] { Steam }, LaunchTargetPicker.MenuTargets(all, LaunchMode.Vanilla));
        Assert.Equal(all, LaunchTargetPicker.MenuTargets(all, LaunchMode.Modded));
    }
}
