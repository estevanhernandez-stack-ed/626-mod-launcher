using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Nexus;
using ModManager.Core.Persistence;
using ModManager.Mcp;
using ModManager.Mcp.Tools;

namespace ModManager.Tests.Mcp;

/// <summary>
/// E1, third slice: the app-level reads an agent gets (themes, preferences, the Nexus connection, save
/// mods). Each reads through the Core function the app reads through, and says why a value is what it is.
/// </summary>
[Collection("McpDataRoot")]
public class AppStateToolsTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-mcp-appstate-");
    private readonly string _savedDataRoot = McpConfig.DataRoot;
    private string DataRoot => McpConfig.DataRoot;

    public AppStateToolsTests()
    {
        McpConfig.DataRoot = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(McpConfig.DataRoot);
    }

    public void Dispose()
    {
        McpConfig.DataRoot = _savedDataRoot;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static JsonElement Json(object result) => JsonSerializer.SerializeToElement(result);

    private void Settings(string json) => File.WriteAllText(AppSettingsFile.PathIn(DataRoot), json);

    private void UserTheme(string fileId, string name)
    {
        var tokens = new Dictionary<string, string>(Themes.BuiltinThemes[Themes.DefaultThemeId].Tokens) { ["name"] = name };
        Directory.CreateDirectory(Path.Combine(DataRoot, "themes"));
        File.WriteAllText(Path.Combine(DataRoot, "themes", fileId + ".json"), JsonSerializer.Serialize(tokens));
    }

    // ---- list_themes ----

    [Fact]
    public void With_no_saved_pick_the_default_theme_is_showing()
    {
        var r = Json(AppStateTools.ListThemes());

        Assert.Equal(Themes.DefaultThemeId, r.GetProperty("activeThemeId").GetString());
        Assert.False(r.GetProperty("savedThemeMissing").GetBoolean());
        Assert.Equal(Themes.BuiltinThemes.Count, r.GetProperty("themes").GetArrayLength());
        Assert.All(r.GetProperty("themes").EnumerateArray(), t => Assert.Equal("builtin", t.GetProperty("source").GetString()));
    }

    [Fact]
    public void A_saved_user_theme_is_listed_as_user_and_showing()
    {
        UserTheme("midnight", "Midnight");
        Settings("""{ "themeId": "midnight" }""");

        var r = Json(AppStateTools.ListThemes());

        Assert.Equal("midnight", r.GetProperty("activeThemeId").GetString());
        var t = r.GetProperty("themes").EnumerateArray().Single(t => t.GetProperty("id").GetString() == "midnight");
        Assert.Equal("user", t.GetProperty("source").GetString());
        Assert.True(t.GetProperty("active").GetBoolean());
        Assert.False(t.GetProperty("overridesBuiltin").GetBoolean());
    }

    [Fact]
    public void A_saved_pick_that_is_gone_says_so_and_the_default_shows()
    {
        Settings("""{ "themeId": "deleted-theme" }""");

        var r = Json(AppStateTools.ListThemes());

        Assert.Equal("deleted-theme", r.GetProperty("savedThemeId").GetString());
        Assert.True(r.GetProperty("savedThemeMissing").GetBoolean());
        Assert.Equal(Themes.DefaultThemeId, r.GetProperty("activeThemeId").GetString());
    }

    [Fact]
    public void A_broken_theme_file_is_named_not_offered()
    {
        Directory.CreateDirectory(Path.Combine(DataRoot, "themes"));
        File.WriteAllText(Path.Combine(DataRoot, "themes", "half-written.json"), "{ \"name\": ");

        var r = Json(AppStateTools.ListThemes());

        Assert.Equal("half-written.json", Assert.Single(r.GetProperty("unreadableUserThemeFiles").EnumerateArray()).GetString());
        Assert.DoesNotContain(r.GetProperty("themes").EnumerateArray(), t => t.GetProperty("id").GetString() == "half-written");
    }

    [Fact]
    public void A_user_theme_with_a_builtin_id_replaces_it()
    {
        UserTheme(Themes.DefaultThemeId, "My Navy");

        var r = Json(AppStateTools.ListThemes());

        var t = r.GetProperty("themes").EnumerateArray().Single(t => t.GetProperty("id").GetString() == Themes.DefaultThemeId);
        Assert.Equal("My Navy", t.GetProperty("name").GetString());
        Assert.True(t.GetProperty("overridesBuiltin").GetBoolean());
    }

    // ---- get_app_settings ----

    [Fact]
    public void Missing_settings_read_as_defaults_and_say_why()
    {
        var r = Json(AppStateTools.GetAppSettings());

        Assert.Equal("missing", r.GetProperty("fileState").GetString());
        Assert.Equal("solid", r.GetProperty("backdrop").GetString());
        Assert.True(r.GetProperty("autoUpdateDefinitions").GetBoolean());
        Assert.False(r.GetProperty("closeToTray").GetBoolean());
        Assert.Contains("closeToTray", r.GetProperty("defaulted").EnumerateArray().Select(d => d.GetString()));
        Assert.Equal("missing", r.GetProperty("nexus").GetProperty("fileState").GetString());
        Assert.False(r.GetProperty("nexus").GetProperty("tokensStored").GetBoolean());
    }

    [Fact]
    public void Saved_settings_are_reported_and_only_the_mistyped_key_defaults()
    {
        Settings("""{ "backdrop": "Mica", "closeToTray": true, "autoCheckModUpdates": "no", "themeId": "forge" }""");

        var r = Json(AppStateTools.GetAppSettings());

        Assert.Equal("ok", r.GetProperty("fileState").GetString());
        Assert.Equal("mica", r.GetProperty("backdrop").GetString());
        Assert.True(r.GetProperty("closeToTray").GetBoolean());
        Assert.True(r.GetProperty("autoCheckModUpdates").GetBoolean());   // "no" is not a bool: default
        Assert.Equal("forge", r.GetProperty("themeId").GetString());
        var defaulted = r.GetProperty("defaulted").EnumerateArray().Select(d => d.GetString()).ToList();
        Assert.Contains("autoCheckModUpdates", defaulted);
        Assert.DoesNotContain("closeToTray", defaulted);
    }

    // Agent-access law 4: the launcher holds the user's sign-in; it never relays it.
    [Fact]
    public void The_nexus_sign_in_is_described_never_relayed()
    {
        const string blob = "ZmFrZS1wcm90ZWN0ZWQtdG9rZW4tYmxvYg==";
        File.WriteAllText(Path.Combine(DataRoot, "nexus.json"),
            NexusTokenStore.Serialize(new NexusTokenSet("access-secret", "refresh-secret", DateTimeOffset.UtcNow.AddHours(1), "public"), "Este", _ => blob));

        var raw = JsonSerializer.Serialize(AppStateTools.GetAppSettings());
        var nexus = JsonDocument.Parse(raw).RootElement.GetProperty("nexus");

        Assert.True(nexus.GetProperty("tokensStored").GetBoolean());
        Assert.Equal("Este", nexus.GetProperty("connectedUser").GetString());
        Assert.DoesNotContain(blob, raw);
        Assert.DoesNotContain("secret", raw);
    }

    [Fact]
    public void A_legacy_api_key_file_is_flagged_and_its_key_never_shown()
    {
        File.WriteAllText(Path.Combine(DataRoot, "nexus.json"), """{ "apiKeyProtected": "old-key-blob" }""");

        var raw = JsonSerializer.Serialize(AppStateTools.GetAppSettings());
        var nexus = JsonDocument.Parse(raw).RootElement.GetProperty("nexus");

        Assert.True(nexus.GetProperty("legacyKeyFile").GetBoolean());
        Assert.False(nexus.GetProperty("tokensStored").GetBoolean());
        Assert.DoesNotContain("old-key-blob", raw);
    }

    // ---- list_save_mods ----

    private GameEntry Register(string id, string? saveDir, string? eaContentId = null)
    {
        var game = new GameEntry
        {
            Id = id, GameName = "Test", Engine = "ue-pak", GameRoot = Path.Combine(_root, id),
            DataDir = Path.Combine(_root, "data-" + id), SaveDir = saveDir, EaContentId = eaContentId,
        };
        RegistryStore.Save(DataRoot, Registry.UpsertGame(Registry.EmptyRegistry(), game));
        return game;
    }

    [Fact]
    public void Save_mods_list_newest_first_and_say_whether_each_can_be_reset()
    {
        var game = Register("pal", saveDir: Path.Combine(_root, "saves"));
        var kept = Path.Combine(_root, "kept.zip");
        File.WriteAllText(kept, "zip");
        SaveModStore.Upsert(game.DataDir!, new SaveModEntry("aaaa", "Old World", Path.Combine(_root, "gone.zip"), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        SaveModStore.Upsert(game.DataDir!, new SaveModEntry("bbbb", "New World", kept, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));

        var r = Json(SaveModTools.ListSaveMods("pal"));

        var mods = r.GetProperty("saveMods").EnumerateArray().ToList();
        Assert.Equal(new[] { "New World", "Old World" }, mods.Select(m => m.GetProperty("name").GetString()));
        Assert.True(mods[0].GetProperty("sourceZipKept").GetBoolean());
        Assert.False(mods[1].GetProperty("sourceZipKept").GetBoolean());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("installBlocked").ValueKind);
    }

    [Fact]
    public void A_game_with_no_save_folder_says_a_save_mod_has_nowhere_to_go()
    {
        Register("nosave", saveDir: null);

        var r = Json(SaveModTools.ListSaveMods("nosave"));

        Assert.Contains("save folder", r.GetProperty("installBlocked").GetString());
        Assert.Empty(r.GetProperty("saveMods").EnumerateArray());
    }

    [Fact]
    public void An_ea_game_says_its_saves_are_not_the_launchers_to_write()
    {
        Register("ea-game", saveDir: Path.Combine(_root, "ea-saves"), eaContentId: "Origin.OFR.50.0001");

        var r = Json(SaveModTools.ListSaveMods("ea-game"));

        Assert.Equal(SaveWritePolicy.EaRefusal, r.GetProperty("installBlocked").GetString());
        Assert.False(r.GetProperty("installNeedsAcknowledgment").GetBoolean());
    }

    // Madden NFL 27's Steam copy carries a high ban-risk floor by app id, and its saves are writable
    // (the EA refusal is for the EA app copy). A new save mod asks first, until the user says not to.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void A_high_risk_game_says_a_save_mod_install_asks_first_until_acknowledged(bool acked, bool needsAck)
    {
        var g = Register("madden-steam-copy", saveDir: Path.Combine(_root, "m-saves"));
        g.SteamAppId = "3940610";
        RegistryStore.Save(DataRoot, Registry.UpsertGame(Registry.EmptyRegistry(), g));
        if (acked) BanRiskAckStore.Ack(g.DataDir!, g.Id, BanRiskAck.WriteSaves);

        var r = Json(SaveModTools.ListSaveMods("madden-steam-copy"));

        Assert.Equal(JsonValueKind.Null, r.GetProperty("installBlocked").ValueKind);
        Assert.Equal(needsAck, r.GetProperty("installNeedsAcknowledgment").GetBoolean());
    }

    [Fact]
    public void An_unknown_game_is_an_error_not_an_empty_list()
        => Assert.Equal("unknown_game", Json(SaveModTools.ListSaveMods("nope")).GetProperty("error").GetProperty("code").GetString());
}
