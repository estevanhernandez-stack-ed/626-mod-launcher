using ModManager.Core;

namespace ModManager.Tests;

public class WriteModConfigTests
{
    [Fact]
    public void Discover_finds_config_files_excluding_manifest_files()
    {
        var d = TestSupport.TempDir("disc-");
        File.WriteAllText(Path.Combine(d, "config.txt"), "k = v");
        File.WriteAllText(Path.Combine(d, "settings.ini"), "[A]\nx = 1");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "");        // manifest, excluded
        File.WriteAllText(Path.Combine(d, "enabled.txt"), "");     // excluded
        File.WriteAllText(Path.Combine(d, "readme.md"), "");       // not config
        var found = ModConfig.Discover(d).Select(Path.GetFileName).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "config.txt", "settings.ini" }, found);
    }

    [Fact]
    public async Task WriteModConfig_backs_up_original_to_data_dir_then_writes()
    {
        var root = TestSupport.TempDir("wmc-");
        var modsDir = Path.Combine(root, "game", "mods", "Foo");
        Directory.CreateDirectory(modsDir);
        var cfg = Path.Combine(modsDir, "config.txt");
        File.WriteAllText(cfg, "pet_name = Truffle\n");
        var c = Scanner.GameContext(new GameEntry { Id = "t", GameName = "T", GameRoot = Path.Combine(root, "game") });

        var newContent = ModConfig.SetValue(File.ReadAllText(cfg), null, "pet_name", "Rocky");
        await Scanner.WriteModConfigAsync(cfg, newContent, c);

        Assert.Contains("pet_name = Rocky", File.ReadAllText(cfg));                 // written
        var backups = Directory.GetFiles(Path.Combine(c.DataDir, "config-backups"), "*", SearchOption.AllDirectories);
        Assert.Single(backups);                                                    // one backup
        Assert.Contains("Truffle", File.ReadAllText(backups[0]));                  // backup holds the original
    }
}
