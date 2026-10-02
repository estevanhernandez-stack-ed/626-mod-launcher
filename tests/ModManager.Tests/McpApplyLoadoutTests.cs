using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Agent;
using ModManager.Core.Persistence;
using ModManager.Mcp;
using ModManager.Mcp.Tools;

namespace ModManager.Tests;

/// <summary>
/// apply_loadout end to end (E1, second slice): a saved profile applied by an agent through the app's
/// own path, behind the same ban-risk law as set_mod_enabled, and recorded.
/// </summary>
[Collection("McpDataRoot")]
public class McpApplyLoadoutTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-mcp-loadout-");
    private readonly string _savedDataRoot = McpConfig.DataRoot;
    private string GameRoot => Path.Combine(_root, "ELDEN RING");
    private string Play => Path.Combine(GameRoot, "Game");

    public McpApplyLoadoutTests()
    {
        McpConfig.DataRoot = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(Path.Combine(Play, "reshade-shaders"));
        File.WriteAllText(Path.Combine(Play, "reshade-shaders", "shader.fx"), "fx");
        File.WriteAllText(Path.Combine(Play, "ReShadePreset.ini"), "preset");
        File.WriteAllText(Path.Combine(Play, "eldenring.exe"), "game");
    }

    public void Dispose()
    {
        McpConfig.DataRoot = _savedDataRoot;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // Direct-inject lane (fromsoft, no Mod Engine 2). "madden-nfl-27" carries a high ban-risk floor by
    // id, which is how the refusal cases get a gated game without a feed.
    private GameEntry Register(string id)
    {
        var game = new GameEntry
        {
            Id = id, GameName = "Test", Engine = "fromsoft", GameRoot = GameRoot,
            DataDir = Path.Combine(_root, "data-" + id),
        };
        RegistryStore.Save(McpConfig.DataRoot, Registry.UpsertGame(Registry.EmptyRegistry(), game));
        return game;
    }

    private static JsonElement Json(object result) => JsonSerializer.SerializeToElement(result);

    private static async Task SaveWithReShade(GameEntry game, bool on)
    {
        var c = Scanner.GameContext(game);
        var mod = ModListing.Resolve(game).Single(m => m.Name == "ReShade");
        await ModToggle.SetEnabledAsync(c, mod, on);
        await Scanner.SaveProfileAsync(on ? "on" : "off", c);
    }

    [Fact]
    public async Task Listing_names_the_saved_loadouts()
    {
        var game = Register("er-loadout");
        await SaveWithReShade(game, on: true);

        var r = Json(await ProfileTools.ListProfiles("er-loadout"));
        Assert.Equal("on", Assert.Single(r.GetProperty("profiles").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Applying_a_loadout_moves_the_files_and_reports_each_change()
    {
        var game = Register("er-loadout");
        await SaveWithReShade(game, on: true);
        await ModToggle.SetEnabledAsync(Scanner.GameContext(game), ModListing.Resolve(game).Single(m => m.Name == "ReShade"), false);

        var r = Json(await ProfileTools.ApplyLoadout("er-loadout", "on"));

        Assert.True(r.GetProperty("ok").GetBoolean(), r.ToString());
        Assert.Equal("preset", File.ReadAllText(Path.Combine(Play, "ReShadePreset.ini")));
        var change = Assert.Single(r.GetProperty("changes").EnumerateArray());
        Assert.Equal("ReShade", change.GetProperty("modName").GetString());
        Assert.True(change.GetProperty("enable").GetBoolean());
        Assert.Equal("ok", AgentAudit.Read(Scanner.DataDirForGame(game)).Last().Result);
    }

    [Fact]
    public async Task A_dry_run_lists_the_changes_and_makes_none()
    {
        var game = Register("er-loadout");
        await SaveWithReShade(game, on: true);
        await ModToggle.SetEnabledAsync(Scanner.GameContext(game), ModListing.Resolve(game).Single(m => m.Name == "ReShade"), false);

        var r = Json(await ProfileTools.ApplyLoadout("er-loadout", "on", dryRun: true));

        Assert.True(r.GetProperty("dryRun").GetBoolean());
        Assert.Single(r.GetProperty("changes").EnumerateArray());
        Assert.False(File.Exists(Path.Combine(Play, "ReShadePreset.ini")));   // still off
    }

    [Fact]
    public async Task On_an_unacknowledged_ban_risk_game_a_loadout_that_turns_anything_on_is_refused_whole()
    {
        var game = Register("madden-nfl-27");
        await SaveWithReShade(game, on: true);
        await ModToggle.SetEnabledAsync(Scanner.GameContext(game), ModListing.Resolve(game).Single(m => m.Name == "ReShade"), false);

        var r = Json(await ProfileTools.ApplyLoadout("madden-nfl-27", "on"));

        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal("ban_risk_not_acknowledged", r.GetProperty("refusal").GetString());
        Assert.False(File.Exists(Path.Combine(Play, "ReShadePreset.ini")));   // nothing moved
        Assert.Equal("ban_risk_not_acknowledged", AgentAudit.Read(Scanner.DataDirForGame(game)).Last().Result);

        // The dry run still answers, and says applying would be refused.
        var dry = Json(await ProfileTools.ApplyLoadout("madden-nfl-27", "on", dryRun: true));
        Assert.Equal("ban_risk_not_acknowledged", dry.GetProperty("wouldRefuse").GetString());
    }

    [Fact]
    public async Task On_a_ban_risk_game_a_loadout_that_only_turns_things_off_is_never_gated()
    {
        var game = Register("madden-nfl-27");
        await SaveWithReShade(game, on: false);
        await ModToggle.SetEnabledAsync(Scanner.GameContext(game), ModListing.Resolve(game).Single(m => m.Name == "ReShade"), true);

        var r = Json(await ProfileTools.ApplyLoadout("madden-nfl-27", "off"));

        Assert.True(r.GetProperty("ok").GetBoolean(), r.ToString());
        Assert.False(File.Exists(Path.Combine(Play, "ReShadePreset.ini")));
    }

    [Fact]
    public async Task An_unknown_loadout_is_not_found_not_a_crash()
    {
        Register("er-loadout");
        var r = Json(await ProfileTools.ApplyLoadout("er-loadout", "nope"));
        Assert.Equal("not_found", r.GetProperty("refusal").GetString());
    }

    [Fact]
    public async Task An_unreadable_loadout_is_answered_and_recorded_not_thrown()
    {
        var game = Register("er-loadout");
        var c = Scanner.GameContext(game);
        Directory.CreateDirectory(c.ProfilesDir);
        File.WriteAllText(Path.Combine(c.ProfilesDir, "broken.json"), "{\"mods\": [");   // truncated

        var r = Json(await ProfileTools.ApplyLoadout("er-loadout", "broken"));

        Assert.Equal("unreadable", r.GetProperty("refusal").GetString());
        Assert.Equal("unreadable", AgentAudit.Read(Scanner.DataDirForGame(game)).Last().Result);
    }
}
