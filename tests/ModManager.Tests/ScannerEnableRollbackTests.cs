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

    /// <summary>by_folder fixture: mod is a directory inside the mod location.</summary>
    private static (string modLiveDir, GameContext c) FolderModFixture()
    {
        var root = TestSupport.TempDir("enable-folder-");
        var gameRoot = Path.Combine(root, "game");
        var modLiveDir = Path.Combine(gameRoot, "mod");
        Directory.CreateDirectory(modLiveDir);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            DataDir = Path.Combine(root, "_626mods", "t"),
            ModLocations = new[] { new ModLocation("mods", "mods", "mod") },
            GroupingRule = "by_folder",
        });
        return (modLiveDir, c);
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

    [Fact]
    public async Task Enable_folder_mod_conflict_throws_and_does_not_delete_preexisting_live_folder()
    {
        // 1. Build a folder mod (by_folder grouping: a directory IS the mod) and disable it,
        //    moving it into the holding folder.
        var (modLiveDir, c) = FolderModFixture();
        var modFolder = Path.Combine(modLiveDir, "SeamlessCoop");
        Directory.CreateDirectory(modFolder);
        File.WriteAllText(Path.Combine(modFolder, "mod.dll"), "DLL");
        await Scanner.DisableModAsync("SeamlessCoop", c);

        // Holding folder should now contain the mod directory.
        var holding = Path.Combine(c.DisabledRoot, "SeamlessCoop");
        Assert.True(Directory.Exists(holding), "holding folder should exist after disable");
        Assert.True(File.Exists(Path.Combine(holding, "SeamlessCoop", "mod.dll")),
            "mod.dll should be inside the holding copy");

        // 2. Re-create a DIFFERENT folder at the same live path with user content — this is
        //    the pre-existing data that must survive the attempted re-enable.
        Directory.CreateDirectory(modFolder);
        File.WriteAllText(Path.Combine(modFolder, "user.txt"), "KEEP");

        // 3. EnableModAsync must throw because the live destination already exists.
        await Assert.ThrowsAnyAsync<Exception>(() => Scanner.EnableModAsync("SeamlessCoop", c));

        // 4. The pre-existing user.txt must still be there — rollback must NOT have deleted it.
        Assert.True(File.Exists(Path.Combine(modFolder, "user.txt")),
            "pre-existing user.txt must survive — rollback must not delete pre-existing data");
        Assert.Equal("KEEP", File.ReadAllText(Path.Combine(modFolder, "user.txt")));

        // 5. The holding folder is still intact — the mod is not stranded.
        Assert.True(Directory.Exists(holding), "holding folder must still exist after failed enable");
        Assert.True(File.Exists(Path.Combine(holding, "SeamlessCoop", "mod.dll")),
            "mod.dll must remain in holding after failed enable");
    }
}
