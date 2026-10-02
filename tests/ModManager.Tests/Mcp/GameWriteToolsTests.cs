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

    // Review on #373: a relative path resolves against whichever process reads it.
    [Fact]
    public void A_relative_folder_is_refused()
    {
        // A relative path that DOES exist from this process's working folder: still refused.
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, BepInExGame());
        Assert.True(Directory.Exists(relative));
        Assert.Equal("not_found", Json(GameWriteTools.RegisterGame("Valheim", relative)).GetProperty("refusal").GetString());
        Assert.False(File.Exists(Path.Combine(McpConfig.DataRoot, "games.json")));
    }

    // Review on #373: an undetectable engine was registered as "custom", a guess the app refuses to make.
    [Fact]
    public void A_folder_whose_engine_cant_be_told_is_refused_not_guessed()
    {
        var blank = Path.Combine(_root, "Blank");
        Directory.CreateDirectory(blank);

        var r = Json(GameWriteTools.RegisterGame("Blank", blank));

        Assert.Equal("refused", r.GetProperty("refusal").GetString());
        Assert.Contains("Pass engine", r.GetProperty("detail").GetString());
        Assert.Contains(AgentAudit.Read(McpConfig.DataRoot), e => e.Tool == "register_game" && e.Result == "refused");   // logged, launcher-level
    }

    // Review on #373: a Steam id goes through the app's Steam add, so the curated manifest facts come too.
    [Fact]
    public void A_steam_game_gets_its_curated_id_and_mod_folder_like_the_apps_steam_add()
    {
        var root = Path.Combine(_root, "Cyberpunk 2077");
        Directory.CreateDirectory(root);

        // Like the app: the engine isn't on the curated Steam-id map and the empty folder shows none,
        // so without one it is refused; with one, the curated id and mod folder still come through.
        Assert.Equal("refused", Json(GameWriteTools.RegisterGame("Cyberpunk 2077", root, steamAppId: "1091500")).GetProperty("refusal").GetString());
        var r = Json(GameWriteTools.RegisterGame("Cyberpunk 2077", root, engine: "custom", steamAppId: "1091500"));

        Assert.True(r.TryGetProperty("gameId", out _), r.GetRawText());
        Assert.Equal("cyberpunk-2077", r.GetProperty("gameId").GetString());
        Assert.Equal("given", r.GetProperty("engineSource").GetString());
        Assert.Contains(Games().Single().ModLocations, l => l.Path == "archive/pc/mod");
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
        Assert.Empty(r.GetProperty("deletedHeld").EnumerateArray());
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

    // B4 follow-up: a turned-off mod's extra-tree entries wait in disabled-trees/<Mod>. Uninstall no longer
    // refuses it: the preview names the held folder and its trees, and confirm deletes that folder too.
    private async Task<(GameEntry Game, string HeldDir)> CyberpunkWithCoolModOff()
    {
        var root = Path.Combine(_root, "Cyberpunk 2077");
        void Put(string rel, string content)
        {
            var p = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, content);
        }
        Put("archive/pc/mod/CoolMod.archive", "MAIN");
        Put("r6/scripts/CoolMod/main.reds", "SCRIPTS");
        Put("r6/tweaks/CoolMod.yaml", "TWEAK");
        var g = new GameEntry
        {
            Id = "cyberpunk-2077", GameName = "Cyberpunk 2077", Engine = "custom", GameRoot = root, DataDir = Path.Combine(_root, "data-cp"),
            ModLocations = new List<ModLocation> { new("mods", "Mods", "archive/pc/mod") },
            FileExtensions = new List<string> { "archive" },
        };
        RegistryStore.Save(McpConfig.DataRoot, Registry.UpsertGame(Registry.EmptyRegistry(), g));
        var ctx = Scanner.GameContext(g);
        Assert.Contains("r6/scripts", ctx.ExtraModTrees ?? Array.Empty<string>()); // pre-condition: the curated trees
        await Scanner.DisableModAsync("CoolMod", ctx);
        var heldDir = Path.Combine(ctx.DataDir, "disabled-trees", "CoolMod");
        Assert.True(File.Exists(Path.Combine(heldDir, "r6", "scripts", "CoolMod", "main.reds"))); // pre-condition
        return (g, heldDir);
    }

    [Fact]
    public async Task Uninstalling_without_confirm_names_the_held_folder_and_its_trees()
    {
        var (g, heldDir) = await CyberpunkWithCoolModOff();

        var r = Json(GameWriteTools.UninstallMod("cyberpunk-2077", "CoolMod"));

        Assert.Equal("confirmation_required", r.GetProperty("refusal").GetString());
        var detail = r.GetProperty("detail").GetString()!;
        Assert.Contains("CoolMod.archive", detail);
        Assert.Contains($"and the files 626 is holding for it in {heldDir} (r6/scripts, r6/tweaks)", detail);
        Assert.True(Directory.Exists(heldDir));
    }

    [Fact]
    public async Task Uninstalling_with_confirm_deletes_the_held_folder_too()
    {
        var (g, heldDir) = await CyberpunkWithCoolModOff();

        var r = Json(GameWriteTools.UninstallMod("cyberpunk-2077", "CoolMod", confirm: true));

        Assert.True(r.GetProperty("ok").GetBoolean(), r.GetRawText());
        Assert.False(Directory.Exists(heldDir));
        Assert.Equal(new[] { heldDir }, r.GetProperty("deletedHeld").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain(ModListing.Resolve(g), m => m.Name == "CoolMod");
    }
}
