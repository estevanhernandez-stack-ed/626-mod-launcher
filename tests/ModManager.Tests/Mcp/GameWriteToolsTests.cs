using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Agent;
using ModManager.Core.Persistence;
using ModManager.Mcp;
using ModManager.Mcp.Tools;

namespace ModManager.Tests.Mcp;

/// <summary>E1, fifth slice: register_game and uninstall_mod, through the Core the app uses.</summary>
[Collection("McpDataRoot")]
public class GameWriteToolsTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-mcp-gamewrite-");
    private readonly string _savedDataRoot = McpConfig.DataRoot;

    public GameWriteToolsTests() => McpConfig.DataRoot = Path.Combine(_root, "appdata");

    public void Dispose()
    {
        McpConfig.DataRoot = _savedDataRoot;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static JsonElement Json(object result) => JsonSerializer.SerializeToElement(result);
    private static IReadOnlyList<GameEntry> Games() => RegistryStore.Load(McpConfig.DataRoot).Games;

    // A Unity game with BepInEx already in it: detected as bepinex, its plugins folder found.
    private string BepInExGame(string name = "Valheim")
    {
        var root = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(root, "BepInEx", "plugins"));
        return root;
    }

    // ---- register_game ----

    [Fact]
    public void Registering_detects_the_engine_and_mod_folder_and_makes_the_game_active()
    {
        var root = BepInExGame();

        var r = Json(GameWriteTools.RegisterGame("Valheim", root));

        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.False(r.GetProperty("alreadyRegistered").GetBoolean());
        Assert.Equal("bepinex", r.GetProperty("engine").GetString());
        var g = Assert.Single(Games());
        Assert.Equal(g.Id, RegistryStore.Load(McpConfig.DataRoot).ActiveGameId);
        Assert.Contains(r.GetProperty("modLocations").EnumerateArray(), l => l.GetProperty("exists").GetBoolean());
        Assert.Equal("register_game", Assert.Single(AgentAudit.Read(Scanner.DataDirForGame(g))).Tool);
    }

    [Fact]
    public void Registering_the_same_install_twice_switches_to_it_and_adds_nothing()
    {
        var root = BepInExGame();
        GameWriteTools.RegisterGame("Valheim", root);

        var r = Json(GameWriteTools.RegisterGame("Valheim again", root));

        Assert.True(r.GetProperty("alreadyRegistered").GetBoolean());
        Assert.Single(Games());
    }

    [Fact]
    public void A_folder_that_does_not_exist_is_refused_and_nothing_is_written()
    {
        var r = Json(GameWriteTools.RegisterGame("Ghost", Path.Combine(_root, "nope")));

        Assert.Equal("not_found", r.GetProperty("refusal").GetString());
        Assert.False(File.Exists(Path.Combine(McpConfig.DataRoot, "games.json")));
    }

    [Fact]
    public void An_engine_626_does_not_know_is_refused()
        => Assert.Equal("refused", Json(GameWriteTools.RegisterGame("X", BepInExGame(), engine: "frostbyte-typo")).GetProperty("refusal").GetString());

    // ---- uninstall_mod ----

    private GameEntry UePak()
    {
        var root = Path.Combine(_root, "wr");
        Directory.CreateDirectory(Path.Combine(root, "Content", "Paks", "~mods"));
        var g = new GameEntry
        {
            Id = "wr", GameName = "WR", Engine = "ue-pak", GameRoot = root, DataDir = Path.Combine(_root, "data-wr"),
            ModLocations = new List<ModLocation> { new("mods", "Mods", "Content/Paks/~mods") },
            FileExtensions = new List<string> { "pak" },
        };
        RegistryStore.Save(McpConfig.DataRoot, Registry.UpsertGame(Registry.EmptyRegistry(), g));
        return g;
    }

    private static string Pak(GameEntry g, string name)
    {
        var p = Path.Combine(g.GameRoot!, "Content", "Paks", "~mods", name);
        File.WriteAllText(p, "pak");
        return p;
    }

    [Fact]
    public void Uninstalling_without_confirm_deletes_nothing_and_says_what_it_would_delete()
    {
        var g = UePak();
        var pak = Pak(g, "CoolMod_P.pak");
        var mod = ModListing.Resolve(g).Single().Name;

        var r = Json(GameWriteTools.UninstallMod("wr", mod));

        Assert.Equal("confirmation_required", r.GetProperty("refusal").GetString());
        Assert.Contains("CoolMod_P.pak", r.GetProperty("detail").GetString());
        Assert.True(File.Exists(pak));
        Assert.Equal("confirmation_required", Assert.Single(AgentAudit.Read(g.DataDir!)).Result);
    }

    [Fact]
    public void Uninstalling_with_confirm_deletes_the_files_and_the_record()
    {
        var g = UePak();
        var pak = Pak(g, "CoolMod_P.pak");
        var mod = ModListing.Resolve(g).Single().Name;

        var r = Json(GameWriteTools.UninstallMod("wr", mod, confirm: true));

        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.False(File.Exists(pak));
        Assert.Empty(ModListing.Resolve(g));
    }

    [Fact]
    public void A_direct_inject_mod_is_never_deleted_even_with_confirm()
    {
        var root = Path.Combine(_root, "ELDEN RING");
        var play = Path.Combine(root, "Game");
        Directory.CreateDirectory(Path.Combine(play, "reshade-shaders"));
        File.WriteAllText(Path.Combine(play, "reshade-shaders", "shader.fx"), "fx");
        File.WriteAllText(Path.Combine(play, "ReShadePreset.ini"), "preset");
        File.WriteAllText(Path.Combine(play, "eldenring.exe"), "game");
        var g = new GameEntry { Id = "er", GameName = "ER", Engine = "fromsoft", GameRoot = root, DataDir = Path.Combine(_root, "data-er") };
        RegistryStore.Save(McpConfig.DataRoot, Registry.UpsertGame(Registry.EmptyRegistry(), g));

        var r = Json(GameWriteTools.UninstallMod("er", "ReShade", confirm: true));

        Assert.Equal("refused", r.GetProperty("refusal").GetString());
        Assert.Contains("never deletes", r.GetProperty("detail").GetString());
        Assert.True(File.Exists(Path.Combine(play, "ReShadePreset.ini")));
    }

    [Fact]
    public void An_unknown_mod_is_not_found()
    {
        UePak();
        Assert.Equal("not_found", Json(GameWriteTools.UninstallMod("wr", "Nope", confirm: true)).GetProperty("refusal").GetString());
    }
}
