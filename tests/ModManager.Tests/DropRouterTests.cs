using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// E1, fourth slice: the drop decided in one place. The app's drop acts on this plan and the agent's
/// dry_run_intake returns it, so these pin the app's own order and conditions.
/// </summary>
public class DropRouterTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-drop-");
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private GameEntry Game(string engine = "ue-pak", bool withModFolder = true, string? me2Config = null)
    {
        var gameRoot = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameRoot);
        if (withModFolder) Directory.CreateDirectory(Path.Combine(gameRoot, "Mods"));
        return new GameEntry
        {
            Id = "g", GameName = "G", Engine = engine, GameRoot = gameRoot, DataDir = Path.Combine(_root, "data"),
            ModLocations = withModFolder ? new List<ModLocation> { new("mods", "Mods", "Mods") } : new List<ModLocation>(),
            FileExtensions = new List<string> { "pak" },
            ModEngineConfig = me2Config,
        };
    }

    private string Zip(string name, params (string, string)[] entries)
    {
        var p = Path.Combine(_root, name);
        TestSupport.WriteZip(p, entries);
        return p;
    }

    [Fact]
    public void A_game_with_nowhere_for_a_mod_to_go_is_blocked_with_the_apps_sentence()
    {
        // Madden on Frostbite: a registration with no mod lane at all (NoModLaneTests).
        var e = EnginePresets.BuildGameEntry(new GameInput { Id = "madden-nfl-27", Name = "Madden NFL 27", Engine = "frostbite", GameRoot = _root }, null);
        e.DataDir = Path.Combine(_root, "data");
        var plan = DropRouter.Plan(Scanner.GameContext(e), new[] { Path.Combine(_root, "a.pak") });
        Assert.Equal(ModListEmptyState.NoModLane, plan.Blocked);
    }

    [Fact]
    public void A_mod_engine_2_game_routes_drops_to_the_not_installed_notice()
    {
        var cfg = Path.Combine(_root, "config_eldenring.toml");
        File.WriteAllText(cfg, "[modengine]");
        var plan = DropRouter.Plan(Scanner.GameContext(Game(engine: "fromsoft", me2Config: cfg)), new[] { Path.Combine(_root, "a.dll") });

        Assert.Equal(DropLane.ModEngine2, plan.Lane);
        var item = Assert.Single(plan.Items);
        Assert.Equal(DropRoute.NotInstalled, item.Route);
        Assert.Equal(DropRouter.ModEngine2DropNotice, item.Problem);
    }

    // The app's order: a Lua mod is claimed before the tool check. A Lua mod zip that also carries an
    // exe would otherwise be extracted as a tool.
    [Fact]
    public void A_lua_mod_is_claimed_before_the_tool_check_sees_it()
    {
        // Named like a catalog tool (the WSE save editor, matched by archive name for this Steam id), so
        // the tool check WOULD claim it. The Lua check runs first in the app, so it is a Lua mod.
        var both = Zip("WSE-Save-Editor-v1.2.zip", ("R5ModSettings/Scripts/main.lua", "x"), ("WSE_Save_Editor.exe", "bin"));
        var g = Game();
        g.SteamAppId = "3041230";
        Assert.Equal(ModManager.Core.Tools.ToolClassification.Tool,
            ModManager.Core.Tools.ToolDetector.Classify(both, "ue-pak", "3041230").Item1);

        Assert.Equal(DropRoute.Ue4ssLua, Assert.Single(DropRouter.Plan(Scanner.GameContext(g), new[] { both }).Items).Route);
    }

    [Fact]
    public void A_corrupt_archive_falls_through_to_regular_intake_as_the_app_does()
    {
        var bad = Path.Combine(_root, "broken.zip");
        File.WriteAllText(bad, "not a zip");
        var plan = DropRouter.Plan(Scanner.GameContext(Game()), new[] { bad });
        Assert.Equal(DropRoute.Mod, Assert.Single(plan.Items).Route);
        // Planning it fails, and the plan says so instead of throwing: the app plans before its try block.
        Assert.NotNull(plan.IntakeProblem);
        Assert.Empty(plan.Intake.ToAdd);
    }

    [Fact]
    public void A_save_mod_for_a_game_whose_saves_the_app_may_not_write_is_claimed_and_refused()
    {
        var g = Game();
        g.SaveDir = Path.Combine(_root, "saves");
        g.EaContentId = "Origin.OFR.50.0003";
        Directory.CreateDirectory(g.SaveDir);
        var world = Zip("world.zip", ("0123456789abcdef0123456789abcdef/data.json", "{}"));

        var item = Assert.Single(DropRouter.Plan(Scanner.GameContext(g), new[] { world }).Items);

        Assert.Equal(DropRoute.SaveMod, item.Route);   // claimed, so regular intake never classifies its contents
        Assert.Equal(SaveWritePolicy.EaRefusal, item.Problem);
    }

    [Fact]
    public void Planning_writes_nothing()
    {
        var g = Game();
        var plan = DropRouter.Plan(Scanner.GameContext(g), new[] { Zip("m.zip", ("Cool_P.pak", "x")) });
        Assert.Single(plan.Intake.ToAdd);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(g.GameRoot!, "Mods")));
        Assert.False(Directory.Exists(g.DataDir));
    }
}
