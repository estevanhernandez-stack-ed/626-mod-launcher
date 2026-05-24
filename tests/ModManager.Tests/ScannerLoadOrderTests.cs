using ModManager.Core;

namespace ModManager.Tests;

// The load-order feature prefixes pak files with NNNN__ to drive Unreal's alphabetical load.
// That prefix must be invisible to identity: a prefixed file still groups to the same mod key
// and still disables/enables. Guards the working core against the load-order rename.
public class ScannerLoadOrderTests
{
    private static (string modsDir, GameContext c) Fixture()
    {
        var root = TestSupport.TempDir("loadorder-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        });
        return (modsDir, c);
    }

    [Fact]
    public async Task A_launcher_prefixed_file_groups_to_its_base_key()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "0010__cool.pak"), "x");
        var mods = await Scanner.BuildModListAsync(c);
        Assert.Contains(mods, m => m.Name == "cool");
        Assert.DoesNotContain(mods, m => m.Name.StartsWith("0010"));
    }

    [Fact]
    public async Task Disable_enable_works_on_a_prefixed_file()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "0020__cool.pak"), "DATA");

        await Scanner.DisableModAsync("cool", c);
        Assert.False(File.Exists(Path.Combine(modsDir, "0020__cool.pak")));
        Assert.Equal("DATA", TestSupport.Read(Path.Combine(c.DisabledRoot, "cool", "0020__cool.pak")));

        await Scanner.EnableModAsync("cool", c);
        Assert.Equal("DATA", TestSupport.Read(Path.Combine(modsDir, "0020__cool.pak")));
    }

    [Fact]
    public async Task ApplyLoadOrder_prefixes_files_in_order_and_reset_restores()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "C");
        File.WriteAllText(Path.Combine(modsDir, "audio.pak"), "A");

        // Want audio to load before cool.
        await Scanner.ApplyLoadOrderAsync(c, new[] { "audio", "cool" });

        var files = Directory.GetFiles(modsDir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "0010__audio.pak", "0020__cool.pak" }, files);

        // Identity survives the prefix.
        var mods = await Scanner.BuildModListAsync(c);
        Assert.Contains(mods, m => m.Name == "audio");
        Assert.Contains(mods, m => m.Name == "cool");

        await Scanner.ResetLoadOrderAsync(c);
        var restored = Directory.GetFiles(modsDir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "audio.pak", "cool.pak" }, restored);
    }

    [Fact]
    public async Task GetLoadOrder_reconciles_saved_with_current_enabled()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "a.pak"), "x");
        File.WriteAllText(Path.Combine(modsDir, "b.pak"), "x");
        await Scanner.ApplyLoadOrderAsync(c, new[] { "b", "a" }); // persists order b,a

        File.WriteAllText(Path.Combine(modsDir, "c.pak"), "x");   // new mod appears
        var order = await Scanner.GetLoadOrderAsync(c);
        Assert.Equal(new[] { "b", "a", "c" }, order.ToArray());
    }
}
