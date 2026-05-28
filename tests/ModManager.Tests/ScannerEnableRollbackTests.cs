using ModManager.Core;

namespace ModManager.Tests;

public class ScannerEnableRollbackTests
{
    private static (string modsDir, GameContext c) Fixture(params string[] exts)
    {
        if (exts.Length == 0) exts = new[] { "pak" };
        var root = TestSupport.TempDir("enable-");
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
    public async Task Enable_rolls_back_and_keeps_holding_when_a_live_copy_fails()
    {
        var (modsDir, c) = Fixture("pak", "ucas");
        File.WriteAllText(Path.Combine(modsDir, "mod.pak"), "PAK");
        File.WriteAllText(Path.Combine(modsDir, "mod.ucas"), "UCAS");
        await Scanner.DisableModAsync("mod", c);   // mod now sits in holding

        // Block one live destination: a non-empty directory where the file must land.
        Directory.CreateDirectory(Path.Combine(modsDir, "mod.ucas"));
        File.WriteAllText(Path.Combine(modsDir, "mod.ucas", "blocker"), "x");

        await Assert.ThrowsAnyAsync<Exception>(() => Scanner.EnableModAsync("mod", c));

        // Holding folder intact — nothing stranded.
        Assert.Equal("PAK", TestSupport.Read(Path.Combine(c.DisabledRoot, "mod", "mod.pak")));
        Assert.Equal("UCAS", TestSupport.Read(Path.Combine(c.DisabledRoot, "mod", "mod.ucas")));
        // The mod.pak live copy (if created before the failure) was rolled back.
        Assert.False(File.Exists(Path.Combine(modsDir, "mod.pak")));
    }

    [Fact]
    public async Task Enable_returns_enabled_outcome_on_success()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
        await Scanner.DisableModAsync("cool", c);

        var outcome = await Scanner.EnableModWithOutcomeAsync("cool", c);

        Assert.True(outcome.Enabled);
        Assert.False(outcome.Skipped);
        Assert.Equal("DATA", TestSupport.Read(Path.Combine(modsDir, "cool.pak")));
    }

    [Fact]
    public async Task Enable_returns_skipped_outcome_when_no_disabled_metadata()
    {
        var (_, c) = Fixture();
        var outcome = await Scanner.EnableModWithOutcomeAsync("ghost", c);
        Assert.True(outcome.Skipped);
        Assert.False(outcome.Enabled);
        Assert.NotNull(outcome.Reason);
    }
}
