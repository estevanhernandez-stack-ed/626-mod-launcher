using ModManager.Core;

namespace ModManager.Tests;

// Ports data-dir.test.js — dataDirForGame resolution + idempotent migrateDataDir.
public class DataDirTests
{
    private static string J(params string[] parts) => Path.Combine(parts);

    [Fact]
    public void DataDirForGame_steam_install_resolves_to_library_level_626mods()
    {
        var lib = J(Path.GetTempPath(), "SteamLibrary");
        var gameRoot = J(lib, "steamapps", "common", "Skyrim");
        Assert.Equal(J(lib, "_626mods", "skyrim"),
            Scanner.DataDirForGame(new GameEntry { Id = "skyrim", GameRoot = gameRoot }));
    }

    [Fact]
    public void DataDirForGame_non_steam_resolves_to_sibling()
    {
        var gameRoot = J(Path.GetTempPath(), "Games", "MyGame");
        Assert.Equal(J(Path.GetTempPath(), "Games", "_626mods", "mygame"),
            Scanner.DataDirForGame(new GameEntry { Id = "mygame", GameRoot = gameRoot }));
    }

    [Fact]
    public void DataDirForGame_explicit_override_wins()
    {
        var over = J(Path.GetTempPath(), "custom", "spot");
        Assert.Equal(over, Scanner.DataDirForGame(new GameEntry { Id = "x", GameRoot = "/whatever", DataDir = over }));
    }

    [Fact]
    public void GameContext_data_paths_follow_resolved_data_dir()
    {
        var lib = J(Path.GetTempPath(), "Lib2");
        var gameRoot = J(lib, "steamapps", "common", "Game");
        var c = Scanner.GameContext(new GameEntry { Id = "g", GameName = "G", GameRoot = gameRoot, FileExtensions = new[] { "pak" } });
        var expected = J(lib, "_626mods", "g");
        Assert.Equal(expected, c.DataDir);
        Assert.Equal(J(expected, "disabled"), c.DisabledRoot);
    }

    private static (string root, string gameRoot, GameContext c) Fixture()
    {
        var root = TestSupport.TempDir("datadir-");
        var gameRoot = Path.Combine(root, "game");
        Directory.CreateDirectory(gameRoot);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") }, FileExtensions = new[] { "pak" },
        });
        return (root, gameRoot, c);
    }

    [Fact]
    public async Task MigrateDataDir_moves_legacy_in_game_data()
    {
        var (_, gameRoot, c) = Fixture();
        var legacy = Path.Combine(gameRoot, "_mod_launcher_data");
        TestSupport.Write(Path.Combine(legacy, "disabled", "ModX", "meta.json"), "{}");
        TestSupport.Write(Path.Combine(legacy, "classification.json"), "{\"ModX\":\"sp\"}");

        var moved = await Scanner.MigrateDataDirAsync(c);

        Assert.True(moved);
        Assert.False(Directory.Exists(legacy));
        Assert.Equal("{\"ModX\":\"sp\"}", TestSupport.Read(Path.Combine(c.DataDir, "classification.json")));
        Assert.True(File.Exists(Path.Combine(c.DataDir, "disabled", "ModX", "meta.json")));
    }

    [Fact]
    public async Task MigrateDataDir_noop_when_no_legacy()
    {
        var (_, _, c) = Fixture();
        Assert.False(await Scanner.MigrateDataDirAsync(c));
    }

    [Fact]
    public async Task MigrateDataDir_refuses_to_clobber_existing_target()
    {
        var (_, gameRoot, c) = Fixture();
        var legacy = Path.Combine(gameRoot, "_mod_launcher_data");
        TestSupport.Write(Path.Combine(legacy, "legacy.txt"), "OLD");
        TestSupport.Write(Path.Combine(c.DataDir, "existing.txt"), "NEW");

        Assert.False(await Scanner.MigrateDataDirAsync(c));
        Assert.True(File.Exists(Path.Combine(legacy, "legacy.txt")));
        Assert.Equal("NEW", TestSupport.Read(Path.Combine(c.DataDir, "existing.txt")));
    }
}
