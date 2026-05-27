using ModManager.Core.IniEdit;

namespace ModManager.Tests.IniEdit;

public class IniEditServiceTests
{
    [Fact]
    public void SaveWithBackup_writes_new_contents()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "mods", "MyMod", "config.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
        File.WriteAllText(iniPath, "old=1");

        IniEditService.SaveWithBackup(iniPath, "new=2", gameDir, "MyMod");

        Assert.Equal("new=2", File.ReadAllText(iniPath));
    }

    [Fact]
    public void SaveWithBackup_creates_bak_with_previous_contents()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "mods", "MyMod", "config.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
        File.WriteAllText(iniPath, "old=1");

        IniEditService.SaveWithBackup(iniPath, "new=2", gameDir, "MyMod");

        var histDir = Path.Combine(gameDir, ".ini-history", "MyMod");
        var baks = Directory.GetFiles(histDir, "*.bak");
        Assert.Single(baks);
        Assert.Equal("old=1", File.ReadAllText(baks[0]));
    }

    [Fact]
    public void SaveWithBackup_aborts_when_backup_dir_unwritable()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "mods", "MyMod", "config.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
        File.WriteAllText(iniPath, "old=1");

        // Block the history dir by creating a FILE where the directory should land.
        var histDir = Path.Combine(gameDir, ".ini-history");
        File.WriteAllText(histDir, "blocker");

        Assert.Throws<IOException>(() =>
            IniEditService.SaveWithBackup(iniPath, "new=2", gameDir, "MyMod"));

        Assert.Equal("old=1", File.ReadAllText(iniPath));
    }

    [Fact]
    public void SaveWithBackup_keeps_last_10_baks_prunes_older()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "config.ini");
        File.WriteAllText(iniPath, "initial");

        for (int i = 0; i < 15; i++)
        {
            IniEditService.SaveWithBackup(iniPath, $"v{i}", gameDir, "MyMod");
            Thread.Sleep(5);
        }

        var histDir = Path.Combine(gameDir, ".ini-history", "MyMod");
        var baks = Directory.GetFiles(histDir, "*.bak");
        Assert.Equal(10, baks.Length);
    }

    [Fact]
    public void RestorePrevious_returns_most_recent_bak_contents()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "config.ini");
        File.WriteAllText(iniPath, "v1");

        IniEditService.SaveWithBackup(iniPath, "v2", gameDir, "MyMod");
        Thread.Sleep(5);
        IniEditService.SaveWithBackup(iniPath, "v3", gameDir, "MyMod");

        var previous = IniEditService.RestorePrevious(iniPath, gameDir, "MyMod");
        Assert.Equal("v2", previous);
    }

    [Fact]
    public void RestorePrevious_returns_null_when_no_bak_history()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "config.ini");
        File.WriteAllText(iniPath, "current");

        var previous = IniEditService.RestorePrevious(iniPath, gameDir, "MyMod");
        Assert.Null(previous);
    }
}
