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

    // ---- Sidecars ride with their mod file ----
    // Found by live smoke on a 194-file Cyberpunk library: ArchiveXL mods ship "Foo.archive" PLUS
    // "Foo.archive.xl", and the .xl matched no configured extension, so it was invisible. Disabling
    // moved the .archive to holding and STRANDED the .xl pointing at a file that was no longer
    // there; uninstalling deleted the .archive and left the .xl as debris. A sidecar is not its own
    // mod — it is part of one — so it belongs in Mod.Files, which is what disable, uninstall, and
    // load-order all iterate.

    [Fact]
    public async Task Disable_takes_the_sidecar_with_it_and_enable_brings_it_back()
    {
        var (modsDir, c) = Fixture("archive");
        File.WriteAllText(Path.Combine(modsDir, "CoolMod.archive"), "ARCHIVE");
        File.WriteAllText(Path.Combine(modsDir, "CoolMod.archive.xl"), "XL");

        await Scanner.DisableModAsync("CoolMod", c);

        Assert.False(File.Exists(Path.Combine(modsDir, "CoolMod.archive")));
        Assert.False(File.Exists(Path.Combine(modsDir, "CoolMod.archive.xl")));
        Assert.Equal("XL", TestSupport.Read(Path.Combine(c.DisabledRoot, "CoolMod", "CoolMod.archive.xl")));

        await Scanner.EnableModAsync("CoolMod", c);

        Assert.Equal("ARCHIVE", TestSupport.Read(Path.Combine(modsDir, "CoolMod.archive")));
        Assert.Equal("XL", TestSupport.Read(Path.Combine(modsDir, "CoolMod.archive.xl")));
    }

    // A sidecar must never become a row of its own — that would double-count the library and offer
    // the user a toggle for half a mod.
    [Fact]
    public async Task A_sidecar_is_not_its_own_mod()
    {
        var (modsDir, c) = Fixture("archive");
        File.WriteAllText(Path.Combine(modsDir, "CoolMod.archive"), "ARCHIVE");
        File.WriteAllText(Path.Combine(modsDir, "CoolMod.archive.xl"), "XL");

        var mods = await Scanner.BuildModListAsync(c);

        var one = Assert.Single(mods);
        Assert.Equal("CoolMod", one.Name);
        Assert.Equal(2, one.Files.Count);
        Assert.Contains("CoolMod.archive.xl", one.Files);
    }

    // The match is "the whole filename, then a dot" — not a prefix. Two mods whose names share a
    // prefix must not adopt each other's files.
    [Fact]
    public async Task A_similarly_named_mod_is_not_mistaken_for_a_sidecar()
    {
        var (modsDir, c) = Fixture("archive");
        File.WriteAllText(Path.Combine(modsDir, "Cool.archive"), "A");
        File.WriteAllText(Path.Combine(modsDir, "CoolExtra.archive"), "B");   // shares the "Cool" prefix
        File.WriteAllText(Path.Combine(modsDir, "Cool.archive.xl"), "XL");    // genuine sidecar of the first

        var mods = (await Scanner.BuildModListAsync(c)).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(2, mods.Count);
        Assert.Equal(new[] { "Cool.archive", "Cool.archive.xl" }, mods[0].Files.OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(new[] { "CoolExtra.archive" }, mods[1].Files);
    }

    [Fact]
    public async Task Uninstall_removes_the_sidecar_too_rather_than_leaving_debris()
    {
        var (modsDir, c) = Fixture("archive");
        File.WriteAllText(Path.Combine(modsDir, "CoolMod.archive"), "ARCHIVE");
        File.WriteAllText(Path.Combine(modsDir, "CoolMod.archive.xl"), "XL");

        await Scanner.UninstallModAsync("CoolMod", c);

        Assert.False(File.Exists(Path.Combine(modsDir, "CoolMod.archive")));
        Assert.False(File.Exists(Path.Combine(modsDir, "CoolMod.archive.xl")));
    }
}
