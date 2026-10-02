using System.IO.Compression;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Agent;
using ModManager.Core.Persistence;
using ModManager.Mcp;
using ModManager.Mcp.Tools;

namespace ModManager.Tests.Mcp;

/// <summary>
/// E1, seventh slice: install_save_mod, reset_save_mod and remove_save_mod go through the Core the app's drop
/// and Saves dialog use, refuse where the app would (and where only the user can say yes), take confirm for
/// the two that lose a world's progress, check what they did, and are audited.
/// </summary>
[Collection("McpDataRoot")]
public class SaveModWriteToolsTests : IDisposable
{
    private const string World = "5391A30D5D70487C9486B8E60428ED3B";

    private readonly string _root = TestSupport.TempDir("mmb-mcp-savewrite-");
    private readonly string _savedDataRoot = McpConfig.DataRoot;

    public SaveModWriteToolsTests()
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

    private string Profiles => Path.Combine(_root, "SaveGames");
    private string WorldDir => Path.Combine(Profiles, "76561198000000000", "RocksDB", "0.10.0", "Worlds", World);

    // A Windrose-shaped save tree: one profile, a RocksDB version, and the game-managed RocksDB_v2 beside it.
    private GameEntry Game(string id = "windrose", string? steamAppId = null, string? eaContentId = null, bool saveDir = true,
                           string? registeredSaveDir = null)
    {
        var prof = Path.Combine(Profiles, "76561198000000000");
        Directory.CreateDirectory(Path.Combine(prof, "RocksDB", "0.10.0", "Worlds"));
        Directory.CreateDirectory(Path.Combine(prof, "RocksDB_v2"));
        File.WriteAllText(Path.Combine(prof, "RocksDB_v2", "sacred.db"), "GAME-OWNED");
        var game = new GameEntry
        {
            Id = id, GameName = "Test", Engine = "ue-pak", GameRoot = Path.Combine(_root, "game-" + id),
            DataDir = Path.Combine(_root, "data-" + id), SaveDir = saveDir ? registeredSaveDir ?? Profiles : null,
            SteamAppId = steamAppId, EaContentId = eaContentId,
        };
        Directory.CreateDirectory(game.GameRoot);
        RegistryStore.Save(McpConfig.DataRoot, Registry.UpsertGame(RegistryStore.Load(McpConfig.DataRoot), game));
        return game;
    }

    private string Zip(string name, params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(_root, "Downloads", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
            using (var w = new StreamWriter(zip.CreateEntry(entry).Open())) w.Write(content);
        return path;
    }

    private string WorldZip(string content = "ORIGINAL") => Zip("Island.zip", ($"Worlds/{World}/level.db", content));

    private static int Snapshots(GameEntry g) => SaveManager.ListSnapshots(Scanner.GameContext(g).SavesDir).Count;

    private static IReadOnlyList<AgentAuditEntry> Log(GameEntry g) => AgentAudit.Read(g.DataDir!);

    // ---- install_save_mod ----

    [Fact]
    public void Install_lands_the_world_keeps_its_zip_snapshots_first_and_is_audited()
    {
        var g = Game();
        var zip = WorldZip();

        var r = Json(SaveModTools.InstallSaveMod(g.Id, zip));

        Assert.True(r.GetProperty("ok").GetBoolean(), r.ToString());
        Assert.Equal(World, r.GetProperty("worldId").GetString());
        Assert.Equal("ORIGINAL", File.ReadAllText(Path.Combine(WorldDir, "level.db")));
        Assert.Equal(SaveModInstaller.KeptZipPath(g.DataDir!, World, zip), r.GetProperty("keptZip").GetString());
        Assert.True(Snapshots(g) > 0);
        Assert.Equal("GAME-OWNED", File.ReadAllText(Path.Combine(Profiles, "76561198000000000", "RocksDB_v2", "sacred.db")));
        var listed = Json(SaveModTools.ListSaveMods(g.Id)).GetProperty("saveMods").EnumerateArray().Single();
        Assert.True(listed.GetProperty("sourceZipKept").GetBoolean());
        Assert.Equal(("install_save_mod", "ok"), (Log(g).Single().Tool, Log(g).Single().Result));
    }

    [Fact]
    public void Install_points_a_regular_mod_at_intake_and_writes_nothing()
    {
        var g = Game();

        var r = Json(SaveModTools.InstallSaveMod(g.Id, Zip("Mod.zip", ("Cool_P.pak", "x"))));

        Assert.Equal("refused", r.GetProperty("refusal").GetString());
        Assert.Contains("use intake", r.GetProperty("detail").GetString());
        Assert.Empty(SaveModStore.Load(g.DataDir!));
        Assert.Equal(0, Snapshots(g));
    }

    [Fact]
    public void Install_of_a_world_already_there_is_refused_and_the_world_is_untouched()
    {
        var g = Game();
        SaveModTools.InstallSaveMod(g.Id, WorldZip());
        File.WriteAllText(Path.Combine(WorldDir, "level.db"), "PLAYED");
        var snaps = Snapshots(g);

        var r = Json(SaveModTools.InstallSaveMod(g.Id, Zip("Island-v2.zip", ($"Worlds/{World}/level.db", "V2"))));

        Assert.Equal("already_installed", r.GetProperty("refusal").GetString());
        Assert.Contains("Reset it", r.GetProperty("detail").GetString());
        Assert.Contains("reset_save_mod", r.GetProperty("detail").GetString());
        Assert.Equal("PLAYED", File.ReadAllText(Path.Combine(WorldDir, "level.db")));
        Assert.Equal(snaps, Snapshots(g));
    }

    [Theory]
    [InlineData("Island.zip")]               // relative
    [InlineData("")]
    public void Install_needs_an_absolute_path_to_a_file(string path)
    {
        var g = Game();

        Assert.Equal("not_found", Json(SaveModTools.InstallSaveMod(g.Id, path)).GetProperty("refusal").GetString());
    }

    // Madden NFL 27's Steam copy carries a high ban-risk floor by app id, and its saves are writable.
    [Fact]
    public void A_high_risk_game_refuses_every_save_write_until_the_user_acknowledges_it_in_the_app()
    {
        var g = Game("madden", steamAppId: "3940610");
        var zip = WorldZip();

        var install = Json(SaveModTools.InstallSaveMod(g.Id, zip));
        Assert.Equal("ban_risk_not_acknowledged", install.GetProperty("refusal").GetString());
        Assert.False(Directory.Exists(WorldDir));
        Assert.Equal(0, Snapshots(g));

        BanRiskAckStore.Ack(g.DataDir!, g.Id, BanRiskAck.WriteSaves);   // the user, in the app
        Assert.True(Json(SaveModTools.InstallSaveMod(g.Id, zip)).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void An_ea_game_refuses_with_the_ea_reason()
    {
        var g = Game("ea", eaContentId: "Origin.OFR.50.0002");

        var r = Json(SaveModTools.InstallSaveMod(g.Id, WorldZip()));

        Assert.Equal(SaveWritePolicy.EaRefusal, r.GetProperty("detail").GetString());
    }

    [Fact]
    public void A_game_with_no_save_folder_refuses()
    {
        var g = Game(saveDir: false);

        Assert.Contains("save folder", Json(SaveModTools.InstallSaveMod(g.Id, WorldZip())).GetProperty("detail").GetString());
    }

    // ---- reset_save_mod ----

    [Fact]
    public void Reset_asks_for_confirm_then_starts_the_world_over_even_after_the_download_is_gone()
    {
        var g = Game();
        var zip = WorldZip();
        SaveModTools.InstallSaveMod(g.Id, zip);
        File.Delete(zip);
        File.WriteAllText(Path.Combine(WorldDir, "level.db"), "PLAYED");

        var ask = Json(SaveModTools.ResetSaveMod(g.Id, World));
        Assert.Equal("confirmation_required", ask.GetProperty("refusal").GetString());
        Assert.Equal("PLAYED", File.ReadAllText(Path.Combine(WorldDir, "level.db")));

        var snaps = Snapshots(g);
        var r = Json(SaveModTools.ResetSaveMod(g.Id, World.ToLowerInvariant(), confirm: true));

        Assert.True(r.GetProperty("ok").GetBoolean(), r.ToString());
        Assert.Equal("ORIGINAL", File.ReadAllText(Path.Combine(WorldDir, "level.db")));
        Assert.True(Snapshots(g) > snaps);
    }

    [Fact]
    public void Reset_refuses_when_no_zip_holding_the_world_is_left()
    {
        var g = Game();
        SaveModTools.InstallSaveMod(g.Id, WorldZip());
        File.Delete(SaveModInstaller.KeptZipPath(g.DataDir!, World, "Island.zip"));

        var r = Json(SaveModTools.ResetSaveMod(g.Id, World, confirm: true));

        Assert.Contains("zip it was installed from is gone", r.GetProperty("detail").GetString());
        Assert.True(File.Exists(Path.Combine(WorldDir, "level.db")));
    }

    // ---- remove_save_mod ----

    [Fact]
    public void Remove_asks_for_confirm_then_deletes_the_world_and_stops_listing_it()
    {
        var g = Game();
        SaveModTools.InstallSaveMod(g.Id, WorldZip());

        Assert.Equal("confirmation_required", Json(SaveModTools.RemoveSaveMod(g.Id, World)).GetProperty("refusal").GetString());
        Assert.True(Directory.Exists(WorldDir));

        var snaps = Snapshots(g);
        var r = Json(SaveModTools.RemoveSaveMod(g.Id, World, confirm: true));

        Assert.True(r.GetProperty("ok").GetBoolean(), r.ToString());
        Assert.False(Directory.Exists(WorldDir));
        Assert.Empty(SaveModStore.Load(g.DataDir!));
        Assert.True(Snapshots(g) > snaps);
        Assert.Equal("GAME-OWNED", File.ReadAllText(Path.Combine(Profiles, "76561198000000000", "RocksDB_v2", "sacred.db")));
    }

    // Review on #379: removing is the safe direction, so the ban-risk acknowledgment doesn't stand in its way.
    [Fact]
    public void Remove_is_not_gated_on_ban_risk_and_deletes_the_kept_zip()
    {
        var g = Game("madden", steamAppId: "3940610");
        var ctx = Scanner.GameContext(g);
        // Installed through the app (which asked the user); the acknowledgment isn't on file now.
        SaveModFlow.TryHandleDrops(new[] { WorldZip() }, Array.Empty<string>(), Profiles, ctx.SavesDir, g.DataDir!,
            null, null, writeAllowed: true);
        Assert.Equal("ban_risk_not_acknowledged", Json(SaveModTools.ResetSaveMod(g.Id, World, confirm: true)).GetProperty("refusal").GetString());

        var r = Json(SaveModTools.RemoveSaveMod(g.Id, World, confirm: true));

        Assert.True(r.GetProperty("ok").GetBoolean(), r.ToString());
        Assert.False(Directory.Exists(WorldDir));
        Assert.False(Directory.Exists(Path.GetDirectoryName(SaveModInstaller.KeptZipPath(g.DataDir!, World, "Island.zip"))));
    }

    // Review on #379: a preview (no confirm) writes nothing, not even the Worlds folder.
    [Fact]
    public void A_preview_without_confirm_does_not_create_the_worlds_folder()
    {
        var g = Game();
        var worlds = Path.GetDirectoryName(WorldDir)!;
        Directory.Delete(worlds);
        var kept = SaveModInstaller.KeptZipPath(g.DataDir!, World, "Island.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        File.Copy(WorldZip(), kept);
        SaveModStore.Upsert(g.DataDir!, new SaveModEntry(World, "Island", kept, DateTime.UtcNow));

        Assert.Equal("confirmation_required", Json(SaveModTools.ResetSaveMod(g.Id, World)).GetProperty("refusal").GetString());
        Assert.Equal("confirmation_required", Json(SaveModTools.RemoveSaveMod(g.Id, World)).GetProperty("refusal").GetString());

        Assert.False(Directory.Exists(worlds));
    }

    // #380: Windrose's curated save folder is <profile>\RocksDB_v2. Worlds go beside it, and each message says
    // where the undo is, because Saves doesn't list a snapshot of a folder it doesn't hold.
    [Fact]
    public void With_the_save_folder_inside_the_profile_the_world_installs_beside_it_and_the_undo_is_named()
    {
        var registered = Path.Combine(Profiles, "76561198000000000", "RocksDB_v2");
        Directory.CreateDirectory(Path.Combine(Profiles, "76561198000000000_Backups"));
        var g = Game(registeredSaveDir: registered);
        Directory.CreateDirectory(Path.Combine(registered, "0.10.0"));

        var install = Json(SaveModTools.InstallSaveMod(g.Id, WorldZip()));
        Assert.True(install.GetProperty("ok").GetBoolean(), install.ToString());
        Assert.Equal(WorldDir, install.GetProperty("installedTo").GetString());
        Assert.Contains("new world", install.GetProperty("detail").GetString());

        var ask = Json(SaveModTools.ResetSaveMod(g.Id, World)).GetProperty("detail").GetString();
        var outside = SaveModInstaller.SaveModSnapshotsFor(Scanner.GameContext(g).SavesDir, World);
        Assert.Contains(outside, ask);
        Assert.DoesNotContain("restored from Saves", ask);

        Assert.True(Json(SaveModTools.RemoveSaveMod(g.Id, World, confirm: true)).GetProperty("ok").GetBoolean());
        Assert.Single(SaveManager.ListSnapshots(outside));
        Assert.Empty(SaveManager.ListSnapshots(Scanner.GameContext(g).SavesDir));
    }

    [Fact]
    public void A_world_the_game_already_imported_is_refused_without_pointing_at_reset_or_remove()
    {
        var registered = Path.Combine(Profiles, "76561198000000000", "RocksDB_v2");
        var g = Game(registeredSaveDir: registered);
        var played = Path.Combine(registered, "0.10.0", "Worlds", World);
        Directory.CreateDirectory(played);
        File.WriteAllText(Path.Combine(played, "000123.sst"), "PROGRESS");

        var r = Json(SaveModTools.InstallSaveMod(g.Id, Zip("SaveHub.zip", ($"{World}/000123.sst", "FRESH"))));

        Assert.Equal("world_exists", r.GetProperty("refusal").GetString());
        Assert.Contains("played", r.GetProperty("detail").GetString());
        Assert.DoesNotContain("reset_save_mod", r.GetProperty("detail").GetString());
        Assert.False(Directory.Exists(WorldDir));
        Assert.Equal("PROGRESS", File.ReadAllText(Path.Combine(played, "000123.sst")));
    }

    // #380 review: Reset discards progress on purpose, so a world the game holds is not refused, but the result says
    // the game keeps its own copy, which a reset of 626's copy may not replace.
    [Fact]
    public void Reset_of_a_world_the_game_holds_goes_ahead_and_says_the_game_keeps_its_own_copy()
    {
        var registered = Path.Combine(Profiles, "76561198000000000", "RocksDB_v2");
        var g = Game(registeredSaveDir: registered);
        Directory.CreateDirectory(Path.Combine(registered, "0.10.0"));
        SaveModTools.InstallSaveMod(g.Id, WorldZip());
        var played = Path.Combine(registered, "0.10.0", "Worlds", World);   // the game imported it
        Directory.CreateDirectory(played);
        File.WriteAllText(Path.Combine(played, "level.db"), "PROGRESS");

        var ask = Json(SaveModTools.ResetSaveMod(g.Id, World)).GetProperty("detail").GetString();
        var r = Json(SaveModTools.ResetSaveMod(g.Id, World, confirm: true));

        Assert.Contains("The game also holds this world", ask);
        Assert.True(r.GetProperty("ok").GetBoolean(), r.ToString());
        Assert.Contains("The game also holds this world", r.GetProperty("detail").GetString());
        Assert.Equal("PROGRESS", File.ReadAllText(Path.Combine(played, "level.db")));   // 626 never writes the game's copy
    }

    [Fact]
    public void An_unknown_world_is_not_found_for_reset_and_remove()
    {
        var g = Game();

        Assert.Equal("not_found", Json(SaveModTools.ResetSaveMod(g.Id, World, confirm: true)).GetProperty("refusal").GetString());
        Assert.Equal("not_found", Json(SaveModTools.RemoveSaveMod(g.Id, World, confirm: true)).GetProperty("refusal").GetString());
    }

    [Fact]
    public void Every_attempt_is_in_the_games_agent_log()
    {
        var g = Game();
        SaveModTools.InstallSaveMod(g.Id, WorldZip());
        SaveModTools.ResetSaveMod(g.Id, World);
        SaveModTools.RemoveSaveMod(g.Id, World, confirm: true);

        Assert.Equal(new[] { ("install_save_mod", "ok"), ("reset_save_mod", "confirmation_required"), ("remove_save_mod", "ok") },
            Log(g).Select(e => (e.Tool, e.Result)).ToArray());
    }
}
