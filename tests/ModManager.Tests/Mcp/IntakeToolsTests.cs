using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Agent;
using ModManager.Core.Persistence;
using ModManager.Mcp;
using ModManager.Mcp.Tools;

namespace ModManager.Tests.Mcp;

/// <summary>
/// E1, fourth slice: one drop router (<see cref="DropRouter"/>) that the app's drop acts on, returned by
/// dry_run_intake and carried out by intake under the agent rules.
/// </summary>
[Collection("McpDataRoot")]
public class IntakeToolsTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-mcp-intake-");
    private readonly string _savedDataRoot = McpConfig.DataRoot;
    private string Drops => Path.Combine(_root, "drops");

    public IntakeToolsTests()
    {
        McpConfig.DataRoot = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(Drops);
    }

    public void Dispose()
    {
        McpConfig.DataRoot = _savedDataRoot;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static JsonElement Json(object result) => JsonSerializer.SerializeToElement(result);

    // A ue-pak game whose mods go in Content/Paks/~mods, with a save folder laid out like Windrose's.
    private GameEntry UePak(string id, string? steamAppId = null, bool withSaves = false)
    {
        var gameRoot = Path.Combine(_root, id);
        Directory.CreateDirectory(Path.Combine(gameRoot, "Content", "Paks", "~mods"));
        string? saves = null;
        if (withSaves)
        {
            saves = Path.Combine(_root, id + "-saves");
            Directory.CreateDirectory(Path.Combine(saves, "user1", "RocksDB", "1.0"));
        }
        return Save(new GameEntry
        {
            Id = id, GameName = id, Engine = "ue-pak", GameRoot = gameRoot, SteamAppId = steamAppId,
            DataDir = Path.Combine(_root, "data-" + id), SaveDir = saves,
            ModLocations = new List<ModLocation> { new("mods", "Mods", "Content/Paks/~mods") },
            FileExtensions = new List<string> { "pak" },
        });
    }

    private GameEntry Save(GameEntry game)
    {
        var reg = File.Exists(Path.Combine(McpConfig.DataRoot, "games.json"))
            ? RegistryStore.Load(McpConfig.DataRoot) : Registry.EmptyRegistry();
        RegistryStore.Save(McpConfig.DataRoot, Registry.UpsertGame(reg, game));
        return game;
    }

    private string Loose(string name, string content = "pak")
    {
        var p = Path.Combine(Drops, name);
        File.WriteAllText(p, content);
        return p;
    }

    private string Zip(string name, params (string, string)[] entries)
    {
        var p = Path.Combine(Drops, name);
        TestSupport.WriteZip(p, entries);
        return p;
    }

    private static string ModsDir(GameEntry g) => Path.Combine(g.GameRoot!, "Content", "Paks", "~mods");

    private static IReadOnlyList<JsonElement> Items(JsonElement r) => r.GetProperty("items").EnumerateArray().ToList();
    private static string RouteOf(JsonElement r, string path)
        => Items(r).Single(i => i.GetProperty("path").GetString() == path).GetProperty("route").GetString()!;

    // ---- dry_run_intake ----

    [Fact]
    public void A_dry_run_routes_each_path_the_way_the_apps_drop_would_and_writes_nothing()
    {
        var g = UePak("wr", withSaves: true);
        var pak = Loose("CoolMod_P.pak");
        var lua = Zip("lua.zip", ("R5ModSettings/Scripts/main.lua", "x"), ("R5ModSettings/enabled.txt", ""));
        var tool = Zip("some-random-utility.zip", ("utility.exe", "bin"), ("README.md", "docs"));
        var world = Zip("world.zip", ("0123456789abcdef0123456789abcdef/data.json", "{}"));

        var r = Json(IntakeTools.DryRunIntake("wr", new[] { pak, lua, tool, world }));

        Assert.Equal("mod_folder", r.GetProperty("lane").GetString());
        Assert.Equal("mod", RouteOf(r, pak));
        Assert.Equal("ue4ss_lua", RouteOf(r, lua));
        Assert.Equal("tool", RouteOf(r, tool));
        Assert.Equal("save_mod", RouteOf(r, world));
        Assert.Equal("CoolMod_P.pak", Assert.Single(r.GetProperty("mods").GetProperty("toAdd").EnumerateArray()).GetProperty("name").GetString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(ModsDir(g)));
        Assert.False(Directory.Exists(g.DataDir));   // not even an audit entry: a dry run is a read
    }

    [Fact]
    public void A_save_mod_is_only_looked_for_when_the_game_has_a_save_folder()
    {
        UePak("nosaves");
        var world = Zip("world.zip", ("0123456789abcdef0123456789abcdef/data.json", "{}"));

        Assert.NotEqual("save_mod", RouteOf(Json(IntakeTools.DryRunIntake("nosaves", new[] { world })), world));
    }

    [Fact]
    public void A_collision_is_shown_with_the_relPath_intake_would_need()
    {
        var g = UePak("wr");
        File.WriteAllText(Path.Combine(ModsDir(g), "CoolMod_P.pak"), "old");

        var r = Json(IntakeTools.DryRunIntake("wr", new[] { Loose("CoolMod_P.pak", "new") }));

        var c = Assert.Single(r.GetProperty("mods").GetProperty("collisions").EnumerateArray());
        Assert.Equal("CoolMod_P.pak", c.GetProperty("relPath").GetString());
    }

    [Fact]
    public void A_high_risk_game_is_shown_as_needing_the_users_acknowledgment()
    {
        UePak("madden", steamAppId: "3940610");
        Assert.True(Json(IntakeTools.DryRunIntake("madden", new[] { Loose("a.pak") })).GetProperty("needsBanRiskAcknowledgment").GetBoolean());
    }

    // ---- intake ----

    [Fact]
    public void Intake_installs_a_mod_into_the_mod_folder_and_records_the_call()
    {
        var g = UePak("wr");

        var r = Json(IntakeTools.Intake("wr", new[] { Loose("CoolMod_P.pak") }));

        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal("CoolMod_P.pak", Assert.Single(r.GetProperty("mods").GetProperty("added").EnumerateArray()).GetString());
        Assert.True(File.Exists(Path.Combine(ModsDir(g), "CoolMod_P.pak")));
        Assert.Equal("intake", Assert.Single(AgentAudit.Read(g.DataDir!)).Tool);
        // The same install record the app's drop writes (A25), so the row knows which files are its own.
        Assert.Contains(ModInstallRegistry.List(g.DataDir!), m => m.Files.Any(f => f.EndsWith("CoolMod_P.pak")));
    }

    [Fact]
    public void A_collision_is_skipped_unless_named_in_replace()
    {
        var g = UePak("wr");
        var existing = Path.Combine(ModsDir(g), "CoolMod_P.pak");
        File.WriteAllText(existing, "old");
        var drop = Loose("CoolMod_P.pak", "new");

        var skipped = Json(IntakeTools.Intake("wr", new[] { drop }));
        Assert.Equal("old", File.ReadAllText(existing));
        Assert.Contains(skipped.GetProperty("skipped").EnumerateArray(), s => s.GetProperty("relPath").GetString() == "CoolMod_P.pak");

        var replaced = Json(IntakeTools.Intake("wr", new[] { drop }, replace: new[] { "CoolMod_P.pak" }));
        Assert.Equal("new", File.ReadAllText(existing));
        Assert.Equal("CoolMod_P.pak", Assert.Single(replaced.GetProperty("mods").GetProperty("updated").EnumerateArray()).GetString());
    }

    [Fact]
    public void A_high_risk_game_refuses_the_whole_drop_and_writes_nothing()
    {
        var g = UePak("madden", steamAppId: "3940610");

        var r = Json(IntakeTools.Intake("madden", new[] { Loose("a.pak") }));

        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal("ban_risk_not_acknowledged", r.GetProperty("refusal").GetString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(ModsDir(g)));
        Assert.Equal("ban_risk_not_acknowledged", Assert.Single(AgentAudit.Read(g.DataDir!)).Result);
    }

    [Fact]
    public void Once_the_user_has_acknowledged_the_risk_the_drop_installs()
    {
        var g = UePak("madden", steamAppId: "3940610");
        BanRiskAckStore.Ack(g.DataDir!, g.Id);

        Assert.True(Json(IntakeTools.Intake("madden", new[] { Loose("a.pak") })).GetProperty("ok").GetBoolean());
        Assert.True(File.Exists(Path.Combine(ModsDir(g), "a.pak")));
    }

    [Fact]
    public void A_world_mod_installs_into_the_save_tree()
    {
        var g = UePak("wr", withSaves: true);
        const string guid = "0123456789abcdef0123456789abcdef";

        var r = Json(IntakeTools.Intake("wr", new[] { Zip("world.zip", ($"{guid}/data.json", "{}")) }));

        var i = Assert.Single(r.GetProperty("installed").EnumerateArray());
        Assert.Equal("save_mod", i.GetProperty("route").GetString());
        Assert.True(File.Exists(Path.Combine(g.SaveDir!, "user1", "RocksDB", "1.0", "Worlds", guid, "data.json")));
        Assert.Equal(guid, Assert.Single(SaveModStore.Load(g.DataDir!)).Guid);
    }

    [Fact]
    public void A_lua_mod_with_no_626_managed_ue4ss_is_skipped_with_the_reason_never_dropped_into_the_mod_folder()
    {
        var g = UePak("wr");

        var r = Json(IntakeTools.Intake("wr", new[] { Zip("lua.zip", ("R5ModSettings/Scripts/main.lua", "x")) }));

        var s = Assert.Single(r.GetProperty("skipped").EnumerateArray());
        Assert.Equal("ue4ss_lua", s.GetProperty("route").GetString());
        Assert.Contains("UE4SS", s.GetProperty("reason").GetString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(ModsDir(g)));
    }

    [Fact]
    public void A_tool_is_extracted_and_registered_not_installed_as_a_mod()
    {
        var g = UePak("wr");

        var r = Json(IntakeTools.Intake("wr", new[] { Zip("some-random-utility.zip", ("utility.exe", "bin"), ("README.md", "docs")) }));

        Assert.Equal("tool", Assert.Single(r.GetProperty("installed").EnumerateArray()).GetProperty("route").GetString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(ModsDir(g)));
    }

    // ---- frameworks and the direct-inject lane (Elden Ring without Mod Engine 2) ----

    private GameEntry EldenRing()
    {
        var gameRoot = Path.Combine(_root, "ELDEN RING");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Game"));
        File.WriteAllText(Path.Combine(gameRoot, "Game", "eldenring.exe"), "game");
        return Save(new GameEntry
        {
            Id = "er", GameName = "ELDEN RING", Engine = "fromsoft", GameRoot = gameRoot, SteamAppId = "1245620",
            DataDir = Path.Combine(_root, "data-er"),
        });
    }

    private string ElmZip() => Zip("elden-mod-loader.zip", ("dinput8.dll", "dll"), ("mod_loader_config.ini", "[x]"));

    [Fact]
    public void A_framework_is_planned_with_where_it_lands_and_is_skipped_without_allowFrameworks()
    {
        var g = EldenRing();
        var elm = ElmZip();

        var plan = Json(IntakeTools.DryRunIntake("er", new[] { elm }));
        Assert.Equal("direct_inject", plan.GetProperty("lane").GetString());
        var fw = Items(plan).Single().GetProperty("framework");
        Assert.Equal(Path.Combine(g.GameRoot!, "Game"), fw.GetProperty("installRoot").GetString());

        var r = Json(IntakeTools.Intake("er", new[] { elm }));
        Assert.Equal("framework", Assert.Single(r.GetProperty("skipped").EnumerateArray()).GetProperty("route").GetString());
        Assert.False(File.Exists(Path.Combine(g.GameRoot!, "Game", "dinput8.dll")));
    }

    [Fact]
    public void With_allowFrameworks_the_framework_installs_into_the_play_folder()
    {
        var g = EldenRing();

        var r = Json(IntakeTools.Intake("er", new[] { ElmZip() }, allowFrameworks: true));

        Assert.Equal("framework", Assert.Single(r.GetProperty("installed").EnumerateArray()).GetProperty("route").GetString());
        Assert.True(File.Exists(Path.Combine(g.GameRoot!, "Game", "dinput8.dll")));
    }

    [Fact]
    public void An_archive_that_only_looks_like_a_framework_needs_continueAsMod()
    {
        EldenRing();
        var odd = Zip("homebrew.zip", ("winhttp.dll", "dll"), ("notes.txt", "x"));

        Assert.True(Items(Json(IntakeTools.DryRunIntake("er", new[] { odd }))).Single().GetProperty("looksLikeFramework").GetBoolean());
        var r = Json(IntakeTools.Intake("er", new[] { odd }));
        Assert.Equal("looks_like_framework", Assert.Single(r.GetProperty("skipped").EnumerateArray()).GetProperty("route").GetString());
    }

    [Fact]
    public void An_unknown_game_is_refused_not_found()
        => Assert.Equal("not_found", Json(IntakeTools.Intake("nope", new[] { "x" })).GetProperty("refusal").GetString());
}
