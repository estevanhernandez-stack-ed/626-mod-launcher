using ModManager.Core;
using ModManager.Core.Discovery;
using ModManager.Core.Persistence;

namespace ModManager.Tests;

// A17. Monster Hunter Wilds crashed on start with a REFramework dinput8.dll dated seventeen months before
// the game binary. These pin the check that would have said so: version-locked loaders only (Este,
// 2026-10-02), compared by file date against the game's largest executable.
public class StaleLoadersTests
{
    private static readonly DateTime LoaderDate = new(2025, 3, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime GameDate = new(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

    private static GameEntry Game(string root, string engine = "custom", string modPath = "mods") => new()
    {
        Id = "g",
        GameName = "Test",
        Engine = engine,
        GameRoot = root,
        ModLocations = new[] { new ModLocation("mods", "mods", modPath) },
        GroupingRule = "filename_no_ext",
        FileExtensions = new[] { "pak" },
        DataDir = Path.Combine(root, "_data"),
    };

    private static string Put(string root, string rel, DateTime utc, int bytes = 1)
    {
        var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[bytes]);
        File.SetLastWriteTimeUtc(p, utc);
        return p;
    }

    private static string WildsShape()
    {
        var root = TestSupport.TempDir("stale-");
        Put(root, "MonsterHunterWilds.exe", GameDate, bytes: 4096);
        Put(root, "dinput8.dll", LoaderDate);
        Directory.CreateDirectory(Path.Combine(root, "reframework", "autorun"));
        return root;
    }

    [Fact]
    public void A_REFramework_older_than_the_game_is_reported_with_both_dates()
    {
        var root = WildsShape();

        var report = StaleLoaders.Find(Scanner.GameContext(Game(root)));
        var stale = Assert.Single(report.Loaders);

        Assert.Equal("REFramework", stale.Name);
        Assert.Equal(LoaderDate, stale.LoaderUtc);
        Assert.Equal(GameDate, report.GameExeUtc);
        Assert.EndsWith("MonsterHunterWilds.exe", report.GameExePath);
        var sentence = Assert.Single(report.Sentences);
        Assert.Contains("2025-03-10", sentence);
        Assert.Contains("2026-08-17", sentence);
        Assert.Contains("github.com/praydog/REFramework", sentence);
        Assert.EndsWith(".", sentence);
    }

    [Fact]
    public void A_bare_dinput8_is_not_named_as_any_version_locked_loader()
    {
        // Half a dozen products ship as dinput8.dll; most (an ASI loader) do not care about the game build.
        var root = TestSupport.TempDir("stale-");
        Put(root, "Game.exe", GameDate, bytes: 4096);
        Put(root, "dinput8.dll", LoaderDate);

        Assert.Empty(StaleLoaders.Find(Scanner.GameContext(Game(root))).Loaders);
    }

    [Fact]
    public void A_build_agnostic_loader_is_never_flagged_however_old()
    {
        // ReShade: a known product, but not version-locked. Flagging it after every patch would teach
        // people to ignore the chip.
        var root = TestSupport.TempDir("stale-");
        Put(root, "Game.exe", GameDate, bytes: 4096);
        Put(root, "dxgi.dll", LoaderDate);
        Directory.CreateDirectory(Path.Combine(root, "reshade-shaders"));

        Assert.Empty(StaleLoaders.Find(Scanner.GameContext(Game(root))).Loaders);
    }

    [Fact]
    public void A_loader_newer_than_the_game_or_set_up_the_same_day_is_fine()
    {
        var root = WildsShape();
        File.SetLastWriteTimeUtc(Path.Combine(root, "dinput8.dll"), GameDate.AddDays(3));
        Assert.Empty(StaleLoaders.Find(Scanner.GameContext(Game(root))).Loaders);

        File.SetLastWriteTimeUtc(Path.Combine(root, "dinput8.dll"), GameDate.AddHours(-20));
        Assert.Empty(StaleLoaders.Find(Scanner.GameContext(Game(root))).Loaders);
    }

    [Fact]
    public void The_game_build_is_the_largest_executable_not_the_newest()
    {
        // A UE game's root stub and crash reporter are rewritten too, and are a fraction of the
        // -Shipping.exe. Here the stub is newer than UE4SS and the real binary is older, so taking the
        // stub would report UE4SS stale when it postdates the build it hooks.
        var root = TestSupport.TempDir("stale-");
        Put(root, "R5.exe", GameDate, bytes: 64);
        Put(root, "R5/Binaries/Win64/R5-Win64-Shipping.exe", LoaderDate.AddDays(-30), bytes: 8192);
        Put(root, "R5/Binaries/Win64/ue4ss/UE4SS.dll", LoaderDate);
        Put(root, "R5/Binaries/Win64/dwmapi.dll", LoaderDate);
        var ctx = Scanner.GameContext(Game(root, "ue-pak", "R5/Content/Paks/~mods"));

        Assert.EndsWith("R5-Win64-Shipping.exe", StaleLoaders.GameExecutable(ctx)!.Value.Path);
        Assert.Empty(StaleLoaders.Find(ctx).Loaders);
    }

    [Fact]
    public void UE4SS_under_the_project_folder_is_found_and_compared_to_the_shipping_binary()
    {
        var root = TestSupport.TempDir("stale-");
        Put(root, "R5/Binaries/Win64/R5-Win64-Shipping.exe", GameDate, bytes: 8192);
        Put(root, "R5/Binaries/Win64/ue4ss/UE4SS.dll", LoaderDate);
        Put(root, "R5/Binaries/Win64/dwmapi.dll", LoaderDate);

        var stale = Assert.Single(StaleLoaders.Find(Scanner.GameContext(Game(root, "ue-pak", "R5/Content/Paks/~mods"))).Loaders);

        Assert.Equal("UE4SS", stale.Name);
    }

    [Fact]
    public void A_UE4SS_runtime_without_the_proxy_that_loads_it_is_not_reported()
    {
        // A vanilla step-aside moves only dwmapi.dll; the runtime stays and injects nothing. Telling the user
        // to update a loader that is not running would be noise.
        var root = TestSupport.TempDir("stale-");
        Put(root, "R5/Binaries/Win64/R5-Win64-Shipping.exe", GameDate, bytes: 8192);
        Put(root, "R5/Binaries/Win64/ue4ss/UE4SS.dll", LoaderDate);

        Assert.Empty(StaleLoaders.Find(Scanner.GameContext(Game(root, "ue-pak", "R5/Content/Paks/~mods"))).Loaders);
    }

    [Fact]
    public void Mod_Engine_2_is_found_in_the_folder_its_registered_config_sits_in()
    {
        // The usual layout: the release extracted into a folder of its own, which LaunchScan finds and
        // records as ModEngineConfig. It is not in any probe root.
        var root = TestSupport.TempDir("stale-");
        Put(root, "Game/eldenring.exe", GameDate, bytes: 4096);
        Put(root, "ModEngine-2.1.0.0-win64/modengine2/bin/modengine2.dll", LoaderDate);
        var config = Put(root, "ModEngine-2.1.0.0-win64/config_eldenring.toml", LoaderDate);
        var game = Game(root, "fromsoft", "mod");
        game.ModEngineConfig = config;

        var stale = Assert.Single(StaleLoaders.Find(Scanner.GameContext(game)).Loaders);

        Assert.Equal("Mod Engine 2", stale.Name);
        Assert.EndsWith("modengine2.dll", stale.LoaderPath);
    }

    [Fact]
    public void A_same_size_copy_of_the_game_exe_with_an_old_date_is_not_the_build()
    {
        // The anti-cheat swap leaves the pre-patch eldenring.exe copied over start_protected_game.exe, with
        // the copy's old date. Taking it would hide a loader that predates the patch.
        var root = TestSupport.TempDir("stale-");
        Put(root, "Game/start_protected_game.exe", LoaderDate.AddDays(-30), bytes: 4096);
        Put(root, "Game/eldenring.exe", GameDate, bytes: 4000);
        Put(root, "Game/dinput8.dll", LoaderDate);
        Put(root, "Game/mod_loader_config.ini", LoaderDate);

        var report = StaleLoaders.Find(Scanner.GameContext(Game(root, "fromsoft", "mod")));

        Assert.EndsWith("eldenring.exe", report.GameExePath);
        Assert.Single(report.Loaders);
    }

    [Fact]
    public void Elden_Mod_Loader_in_the_Game_folder_is_found_by_its_config_beside_it()
    {
        var root = TestSupport.TempDir("stale-");
        Put(root, "Game/eldenring.exe", GameDate, bytes: 4096);
        Put(root, "Game/dinput8.dll", LoaderDate);
        Put(root, "Game/mod_loader_config.ini", LoaderDate);

        var stale = Assert.Single(StaleLoaders.Find(Scanner.GameContext(Game(root, "fromsoft", "mod"))).Loaders);

        Assert.Equal("Elden Mod Loader", stale.Name);
    }

    [Fact]
    public void Marking_checked_holds_until_the_executable_is_rewritten()
    {
        var report = StaleLoaders.Find(Scanner.GameContext(Game(WildsShape())));

        Assert.NotNull(report.Summary(null));
        Assert.Null(report.Summary(GameDate));
        // A patch rewrites the executable, so a check against the build before it no longer covers it.
        Assert.NotNull(report.Summary(GameDate.AddDays(-7)));
        Assert.Null(StaleLoaderReport.None.Summary(null));
    }

    [Fact]
    public void No_game_folder_or_no_executable_reports_nothing()
    {
        Assert.Empty(StaleLoaders.Find(Scanner.GameContext(Game(Path.Combine(TestSupport.TempDir("stale-"), "gone")))).Loaders);

        var root = TestSupport.TempDir("stale-");
        Put(root, "dinput8.dll", LoaderDate);
        Directory.CreateDirectory(Path.Combine(root, "reframework"));
        Assert.Empty(StaleLoaders.Find(Scanner.GameContext(Game(root))).Loaders);
    }

    [Fact]
    public void Get_game_shape_carries_the_same_sentence_and_says_when_it_was_checked()
    {
        var game = Game(WildsShape());

        var note = Assert.Single(GameShape.Of(game).Notes, n => n.StartsWith("REFramework"));
        Assert.Contains("2025-03-10", note);
        Assert.DoesNotContain("marked", note);

        game.LoaderCheckedExeUtc = GameDate;
        Assert.Contains("The user marked loaders as checked against this build.",
            Assert.Single(GameShape.Of(game).Notes, n => n.StartsWith("REFramework")));
    }

    [Fact]
    public void LoaderCheckedExeUtc_round_trips_as_camelCase()
    {
        var dir = TestSupport.TempDir("regstore-loaderchecked-");
        var reg = new GameRegistry { Games = { new GameEntry { Id = "g1", GameName = "G1", LoaderCheckedExeUtc = GameDate } } };

        RegistryStore.Save(dir, reg);

        var json = File.ReadAllText(RegistryStore.PathFor(dir));
        Assert.Contains("\"loaderCheckedExeUtc\"", json);
        Assert.DoesNotContain("\"LoaderCheckedExeUtc\"", json);
        Assert.Equal(GameDate, RegistryStore.Load(dir).Games[0].LoaderCheckedExeUtc);
    }
}
