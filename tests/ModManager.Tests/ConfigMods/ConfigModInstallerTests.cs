using ModManager.Core.ConfigMods;

namespace ModManager.Tests.ConfigMods;

public class ConfigModInstallerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "cfginst-" + Guid.NewGuid().ToString("n"));
    private readonly string _configDir;   // .../Saved/Config/WindowsNoEditor
    private readonly string _dataDir;

    public ConfigModInstallerTests()
    {
        _configDir = Path.Combine(_tmp, "Saved", "Config", "WindowsNoEditor");
        _dataDir = Path.Combine(_tmp, "data");
        Directory.CreateDirectory(_configDir);
        Directory.CreateDirectory(_dataDir);
    }
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private static readonly (string Name, string Content)[] PerfPayload =
        { ("Engine.ini", "[SystemSettings]\r\nr.Lumen=0\r\n") };

    [Fact]
    public void Install_merges_into_existing_file_and_snapshots_first()
    {
        var target = Path.Combine(_configDir, "Engine.ini");
        File.WriteAllText(target, "[SystemSettings]\r\nr.Shadow=1\r\n");

        var entry = ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Performance Enhancer");

        var written = File.ReadAllText(target);
        Assert.Contains("r.Lumen=0", written);
        Assert.Contains("r.Shadow=1", written);

        var f = entry.Files.Single();
        Assert.True(f.ExistedAtInstall);
        Assert.NotNull(f.SnapshotPath);
        Assert.True(File.Exists(f.SnapshotPath));
        Assert.Contains("r.Shadow=1", File.ReadAllText(f.SnapshotPath!));
        Assert.DoesNotContain("r.Lumen=0", File.ReadAllText(f.SnapshotPath!));

        var stored = ConfigModStore.Load(_dataDir).Single();
        Assert.Equal(entry.Id, stored.Id);
        Assert.True(stored.Enabled);
    }

    [Fact]
    public void Install_into_missing_target_records_existedAtInstall_false()
    {
        var entry = ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Perf");
        var f = entry.Files.Single();
        Assert.False(f.ExistedAtInstall);
        Assert.Null(f.SnapshotPath);
        Assert.True(File.Exists(Path.Combine(_configDir, "Engine.ini")));
    }

    [Fact]
    public void Install_refuses_unknown_filenames_nothing_written()
    {
        var bad = new[] { ("evil.ini", "[A]\r\nX=1\r\n") };
        Assert.ThrowsAny<InvalidOperationException>(
            () => ConfigModInstaller.Install(bad, _configDir, _dataDir, "Bad"));
        Assert.Empty(Directory.GetFiles(_configDir));
        Assert.Empty(ConfigModStore.Load(_dataDir));
    }

    [Fact]
    public void Install_refuses_path_traversal_nothing_written()
    {
        var bad = new[] { (@"..\Engine.ini", "[A]\r\nX=1\r\n") };
        Assert.ThrowsAny<InvalidOperationException>(
            () => ConfigModInstaller.Install(bad, _configDir, _dataDir, "Bad"));
        Assert.Empty(Directory.GetFiles(_configDir));
    }

    [Fact]
    public void Install_refuses_empty_payload()
    {
        Assert.ThrowsAny<InvalidOperationException>(
            () => ConfigModInstaller.Install(Array.Empty<(string, string)>(), _configDir, _dataDir, "Empty"));
    }

    [Fact]
    public void Install_refuses_missing_config_dir_nothing_written()
    {
        var gone = Path.Combine(_tmp, "nope", "WindowsNoEditor");
        Assert.ThrowsAny<InvalidOperationException>(
            () => ConfigModInstaller.Install(PerfPayload, gone, _dataDir, "Perf"));
        Assert.Empty(ConfigModStore.Load(_dataDir));
    }

    [Fact]
    public void Redrop_restores_then_remerges_no_stacking()
    {
        var target = Path.Combine(_configDir, "Engine.ini");
        File.WriteAllText(target, "[SystemSettings]\r\nr.Shadow=1\r\n");
        ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Perf");
        ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Perf");

        var written = File.ReadAllText(target);
        Assert.Equal(1, written.Split("r.Lumen=0").Length - 1);
        Assert.Contains("r.Shadow=1", written);
        Assert.Single(ConfigModStore.Load(_dataDir));
    }

    [Fact]
    public void IdFor_slugs_names_stably()
    {
        Assert.Equal("performance-enhancer", ConfigModInstaller.IdFor("Performance Enhancer"));
        Assert.Equal("perf-1-2", ConfigModInstaller.IdFor("Perf!! (1.2)"));
        Assert.Equal("config-mod", ConfigModInstaller.IdFor("???"));
    }
}
