using ModManager.Core;

namespace ModManager.Tests;

// Ports scanner-disable.test.js — the reversibility + rollback contract for disable.
public class ScannerDisableTests
{
    private static (string modsDir, GameContext c) Fixture(params string[] exts)
    {
        if (exts.Length == 0) exts = new[] { "pak" };
        var root = TestSupport.TempDir("disable-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = exts, GroupingRule = "filename_no_ext",
        });
        return (modsDir, c);
    }

    [Fact]
    public async Task Disable_moves_to_holding_and_enable_restores()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");

        await Scanner.DisableModAsync("cool", c);
        Assert.False(File.Exists(Path.Combine(modsDir, "cool.pak")));
        Assert.Equal("DATA", TestSupport.Read(Path.Combine(c.DisabledRoot, "cool", "cool.pak")));

        await Scanner.EnableModAsync("cool", c);
        Assert.Equal("DATA", TestSupport.Read(Path.Combine(modsDir, "cool.pak")));
        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "cool")));
    }

    [Fact]
    public async Task Disable_surfaces_error_and_preserves_live_file_when_move_fails()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
        // Block the destination: a non-empty directory where the file needs to land.
        Directory.CreateDirectory(Path.Combine(c.DisabledRoot, "cool", "cool.pak"));
        File.WriteAllText(Path.Combine(c.DisabledRoot, "cool", "cool.pak", "blocker"), "x");

        await Assert.ThrowsAnyAsync<Exception>(() => Scanner.DisableModAsync("cool", c));

        Assert.Equal("DATA", TestSupport.Read(Path.Combine(modsDir, "cool.pak")));
        var mods = await Scanner.BuildModListAsync(c);
        Assert.True(mods.First(m => m.Name == "cool").Enabled);
    }

    [Fact]
    public async Task Disable_rolls_back_already_moved_files_when_a_later_file_fails()
    {
        var (modsDir, c) = Fixture("pak", "ucas");
        File.WriteAllText(Path.Combine(modsDir, "mod.pak"), "PAK");
        File.WriteAllText(Path.Combine(modsDir, "mod.ucas"), "UCAS");
        Directory.CreateDirectory(Path.Combine(c.DisabledRoot, "mod", "mod.ucas"));
        File.WriteAllText(Path.Combine(c.DisabledRoot, "mod", "mod.ucas", "blocker"), "x");

        await Assert.ThrowsAnyAsync<Exception>(() => Scanner.DisableModAsync("mod", c));

        Assert.Equal("PAK", TestSupport.Read(Path.Combine(modsDir, "mod.pak")));
        Assert.Equal("UCAS", TestSupport.Read(Path.Combine(modsDir, "mod.ucas")));
    }
}
