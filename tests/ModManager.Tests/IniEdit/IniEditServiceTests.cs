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

    // --- Newline normalization (the WinUI TextBox hands back bare-CR; never write that to disk) ---

    [Fact]
    public void SaveWithBackup_normalizes_bare_CR_to_CRLF()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "config.ini");
        File.WriteAllText(iniPath, "[a]\r\nx=1\r\n"); // existing file is CRLF

        // The WinUI TextBox collapses every \r\n to a bare \r on round-trip — simulate that.
        IniEditService.SaveWithBackup(iniPath, "[a]\rx=2\r", gameDir, "MyMod");

        var text = File.ReadAllText(iniPath);
        Assert.Contains("\r\n", text);
        Assert.DoesNotMatch("\r(?!\n)", text); // zero bare-CR: every \r is followed by \n
    }

    [Fact]
    public void SaveWithBackup_defaults_new_file_to_CRLF()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "config.ini"); // no pre-existing file

        IniEditService.SaveWithBackup(iniPath, "[a]\nx=1\n", gameDir, "MyMod"); // LF input

        var text = File.ReadAllText(iniPath);
        Assert.Contains("\r\n", text);
        Assert.DoesNotMatch("\r(?!\n)", text);
    }

    [Fact]
    public void SaveWithBackup_preserves_LF_only_style()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "config.ini");
        File.WriteAllText(iniPath, "x=1\n"); // existing file is deliberately LF-only

        IniEditService.SaveWithBackup(iniPath, "x=2\r\ny=3\n", gameDir, "MyMod"); // mixed input

        var text = File.ReadAllText(iniPath);
        Assert.DoesNotContain("\r", text); // stays LF-only — not force-converted to CRLF
    }

    [Fact]
    public void SaveWithBackup_backup_is_byte_exact_not_normalized()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "config.ini");
        var originalBytes = System.Text.Encoding.UTF8.GetBytes("[a]\rx=1\r"); // bare-CR on disk
        File.WriteAllBytes(iniPath, originalBytes);

        IniEditService.SaveWithBackup(iniPath, "x=2", gameDir, "MyMod");

        var histDir = Path.Combine(gameDir, ".ini-history", "MyMod");
        var bak = Directory.GetFiles(histDir, "*.bak").Single();
        Assert.Equal(originalBytes, File.ReadAllBytes(bak)); // snapshot is the true previous bytes, unnormalized
    }
}
