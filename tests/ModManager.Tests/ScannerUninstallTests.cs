using ModManager.Core;

namespace ModManager.Tests;

// Gated uninstall — the one place the launcher permanently deletes mod files. The GATE is the
// UI confirm; the Core does the delete only when explicitly called. Removes live files from
// every location + mirror AND any disabled holding folder. Idempotent.
public class ScannerUninstallTests
{
    private static (string primary, string mirror, GameContext c) Setup()
    {
        var root = TestSupport.TempDir("uninstall-");
        var primary = Path.Combine(root, "mods");
        var mirror = Path.Combine(root, "server");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(mirror);
        File.WriteAllText(Path.Combine(primary, "cool.pak"), "X");
        File.WriteAllText(Path.Combine(mirror, "cool.pak"), "X");
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") { Mirrors = new[] { "server" } } },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        });
        return (primary, mirror, c);
    }

    [Fact]
    public async Task Uninstall_removes_live_mod_files_from_all_locations()
    {
        var (primary, mirror, c) = Setup();
        await Scanner.UninstallModAsync("cool", c);
        Assert.False(File.Exists(Path.Combine(primary, "cool.pak")));
        Assert.False(File.Exists(Path.Combine(mirror, "cool.pak")));
        Assert.DoesNotContain(await Scanner.BuildModListAsync(c), m => m.Name == "cool");
    }

    [Fact]
    public async Task Uninstall_removes_a_disabled_mods_holding_folder()
    {
        var (_, _, c) = Setup();
        await Scanner.DisableModAsync("cool", c);
        Assert.True(Directory.Exists(Path.Combine(c.DisabledRoot, "cool")));

        await Scanner.UninstallModAsync("cool", c);

        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "cool")));
        Assert.DoesNotContain(await Scanner.BuildModListAsync(c), m => m.Name == "cool");
    }

    [Fact]
    public async Task Uninstall_is_idempotent_for_unknown_and_leaves_others_intact()
    {
        var (primary, _, c) = Setup();
        await Scanner.UninstallModAsync("nope", c); // no throw
        Assert.True(File.Exists(Path.Combine(primary, "cool.pak")));
    }
}
