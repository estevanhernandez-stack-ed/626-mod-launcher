using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Vanilla step-aside on a direct-inject game (FromSoft, no Mod Engine 2). Found on the 2026-11-07 Patch Day
/// smoke: the app sent every stepped-aside row to Scanner.DisableModAsync, which only knows the scanner's own
/// mod list. Direct-inject rows live in DirectInjectListing, so each disable found nothing and returned, and a
/// "vanilla" Elden Ring kept ERSS2 and ReShade in the play folder. Only dinput8.dll moved, through the proxy
/// lane. Rows now go through ModToggle's lane chooser by name.
/// </summary>
public class VanillaStepAsideLaneTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-vanilla-lane-");
    private string GameRoot => Path.Combine(_root, "ELDEN RING");
    private string Play => Path.Combine(GameRoot, "Game");

    public VanillaStepAsideLaneTests()
    {
        Directory.CreateDirectory(Path.Combine(Play, "reshade-shaders"));
        File.WriteAllText(Path.Combine(Play, "reshade-shaders", "shader.fx"), "fx");
        File.WriteAllText(Path.Combine(Play, "ReShadePreset.ini"), "preset");
        Directory.CreateDirectory(Path.Combine(Play, "ERSS2"));
        File.WriteAllText(Path.Combine(Play, "ERSS2", "sl.interposer.dll"), "sl");
        File.WriteAllText(Path.Combine(Play, "ERSS-FG.dll"), "fg");
        File.WriteAllText(Path.Combine(Play, "d3d12.dll"), "erss2-loader"); // ERSS2's proxy, a companion file
        File.WriteAllText(Path.Combine(Play, "eldenring.exe"), "game");
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private GameEntry Game() => new()
    {
        Id = "er-vanilla", GameName = "ELDEN RING", Engine = "fromsoft", GameRoot = GameRoot,
        DataDir = Path.Combine(_root, "data"),
    };

    private static VanillaLaunchOps Ops(GameContext c) => new()
    {
        ActiveModRows = () => ModListing.Resolve(c.Game).Where(m => m.Enabled && !m.ReadOnly && !m.IsBase)
            .Select(m => new StashedModRow { Name = m.Name, Location = m.Location }).ToList(),
        ActiveFrameworks = () => Array.Empty<string>(),
        ActiveDirectInjectProxies = () => Array.Empty<string>(),
        DisableModRow = (name, location) => ModToggle.SetEnabledByNameAsync(c, name, location, false),
        EnableModRow = (name, location) => ModToggle.SetEnabledByNameAsync(c, name, location, true),
        DisableFramework = _ => { },
        EnableFramework = _ => { },
        DisableDirectInjectProxy = _ => { },
        EnableDirectInjectProxy = _ => { },
    };

    private Dictionary<string, string> Snapshot() => Directory.GetFiles(Play, "*", SearchOption.AllDirectories)
        .Where(p => !Path.GetRelativePath(Play, p).StartsWith("_626", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(p => Path.GetRelativePath(Play, p), File.ReadAllText, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task Step_aside_moves_direct_inject_rows_out_of_the_play_folder_and_restore_brings_them_back()
    {
        var game = Game();
        var c = Scanner.GameContext(game);
        var before = Snapshot();
        Assert.Contains(ModListing.Resolve(game), m => m.Name == "ReShade" && m.Enabled);

        var aside = await VanillaLaunch.StepAsideAsync(game.DataDir!, Ops(c));
        Assert.True(aside.Success, aside.Error);
        Assert.False(File.Exists(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.False(File.Exists(Path.Combine(Play, "ERSS-FG.dll")));
        Assert.False(Directory.Exists(Path.Combine(Play, "ERSS2")));
        Assert.False(File.Exists(Path.Combine(Play, "d3d12.dll"))); // the loader goes with its mod
        Assert.True(File.Exists(Path.Combine(Play, "eldenring.exe"))); // the game's own file stays
        Assert.Equal(LaunchMode.Vanilla, VanillaLaunch.CurrentMode(game.DataDir!));

        var back = await VanillaLaunch.RestoreAsync(game.DataDir!, Ops(c));
        Assert.True(back.Success, back.Error);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Loader_row_and_proxy_lane_together_round_trip_byte_identical()
    {
        // The DLL loader is both a row (dll-mod-loader) and a proxy (dinput8.dll); the App runs both lanes.
        File.WriteAllText(Path.Combine(Play, "dinput8.dll"), "loader");
        Directory.CreateDirectory(Path.Combine(Play, "mods"));
        File.WriteAllText(Path.Combine(Play, "mods", "SkipTheIntro.dll"), "skip");
        var game = Game();
        var c = Scanner.GameContext(game);
        var ops = Ops(c);
        var withProxies = new VanillaLaunchOps
        {
            ActiveModRows = ops.ActiveModRows,
            ActiveFrameworks = ops.ActiveFrameworks,
            ActiveDirectInjectProxies = () => DirectInject.ProcessLoadProxiesIn(Directory.GetFiles(Play)),
            DisableModRow = ops.DisableModRow,
            EnableModRow = ops.EnableModRow,
            DisableFramework = ops.DisableFramework,
            EnableFramework = ops.EnableFramework,
            DisableDirectInjectProxy = p => ModToggle.SetProxyLoaderEnabled(game, p, enabled: false),
            EnableDirectInjectProxy = p => ModToggle.SetProxyLoaderEnabled(game, p, enabled: true),
        };
        var before = Snapshot();

        Assert.True((await VanillaLaunch.StepAsideAsync(game.DataDir!, withProxies)).Success);
        Assert.False(File.Exists(Path.Combine(Play, "dinput8.dll")));
        Assert.False(File.Exists(Path.Combine(Play, "ERSS-FG.dll")));

        Assert.True((await VanillaLaunch.RestoreAsync(game.DataDir!, withProxies)).Success);
        Assert.Equal(before, Snapshot());
    }

    private VanillaLaunchOps FullOps(GameEntry game, GameContext c, Func<IReadOnlyList<StashedModRow>>? rows = null)
    {
        var ops = Ops(c);
        return new VanillaLaunchOps
        {
            ActiveModRows = rows ?? ops.ActiveModRows,
            ActiveFrameworks = ops.ActiveFrameworks,
            ActiveDirectInjectProxies = () => DirectInject.ProcessLoadProxiesIn(Directory.GetFiles(Play)),
            DisableModRow = ops.DisableModRow,
            EnableModRow = ops.EnableModRow,
            DisableFramework = ops.DisableFramework,
            EnableFramework = ops.EnableFramework,
            DisableDirectInjectProxy = p => ModToggle.SetProxyLoaderEnabled(game, p, enabled: false),
            EnableDirectInjectProxy = p => ModToggle.SetProxyLoaderEnabled(game, p, enabled: true),
        };
    }

    [Fact]
    public async Task A_stale_copy_in_the_proxy_holding_never_comes_back_live()
    {
        // Audit I1: the loader is a row AND a proxy. Recording it under both lanes let Restore pull a leftover
        // from _626/vanilla-proxy into the play folder before the row's own restore ran.
        File.WriteAllText(Path.Combine(Play, "dinput8.dll"), "loader-current");
        var proxyHolding = DirectInject.VanillaProxyHolding(Play);
        Directory.CreateDirectory(proxyHolding);
        File.WriteAllText(Path.Combine(proxyHolding, "dinput8.dll"), "loader-stale");
        var game = Game();
        var ops = FullOps(game, Scanner.GameContext(game));

        Assert.True((await VanillaLaunch.StepAsideAsync(game.DataDir!, ops)).Success);
        Assert.Empty(VanillaStashStore.Load(game.DataDir!)!.DirectInjectProxies); // the row lane took it
        var back = await VanillaLaunch.RestoreAsync(game.DataDir!, ops);

        Assert.True(back.Success, back.Error);
        Assert.Equal("loader-current", File.ReadAllText(Path.Combine(Play, "dinput8.dll")));
        Assert.Equal("loader-stale", File.ReadAllText(Path.Combine(proxyHolding, "dinput8.dll"))); // untouched
    }

    [Fact]
    public async Task A_row_already_off_on_disk_is_not_stashed_and_stays_off_after_restore()
    {
        // Audit I2a: the App's row list can lag the disk (an agent turned ReShade off). The step-aside skips it;
        // recording it anyway made Restore turn on a mod that was off before vanilla.
        var game = Game();
        var c = Scanner.GameContext(game);
        await ModToggle.SetEnabledAsync(c, ModListing.Resolve(game).Single(m => m.Name == "ReShade"), false);
        var stale = new[] { new StashedModRow { Name = "ReShade", Location = "" } };
        var ops = FullOps(game, c, () => stale.Concat(Ops(c).ActiveModRows()).ToList());

        Assert.True((await VanillaLaunch.StepAsideAsync(game.DataDir!, ops)).Success);
        Assert.DoesNotContain(VanillaStashStore.Load(game.DataDir!)!.ModRows, r => r.Name == "ReShade");
        Assert.True((await VanillaLaunch.RestoreAsync(game.DataDir!, ops)).Success);

        Assert.False(File.Exists(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.False(ModListing.Resolve(game).Single(m => m.Name == "ReShade").Enabled);
    }

    [Fact]
    public async Task By_name_disable_of_an_unknown_row_is_a_no_op()
    {
        var game = Game();
        var before = Snapshot();
        await ModToggle.SetEnabledByNameAsync(Scanner.GameContext(game), "No Such Mod", "", false);
        Assert.Equal(before, Snapshot());
    }
}
