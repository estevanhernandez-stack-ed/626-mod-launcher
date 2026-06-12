using ModManager.Core.ConfigMods;

namespace ModManager.Tests.ConfigMods;

public class ConfigModStoreTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "cfgstore-" + Guid.NewGuid().ToString("n"));
    public ConfigModStoreTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private static ConfigModEntry Entry(string id = "perf-enh") => new(
        Id: id, Name: "Performance Enhancer",
        Files: new[] { new ConfigFileRecord("Engine.ini", ExistedAtInstall: true,
            SnapshotPath: @"C:\snap\Engine.ini.123.bak", Payload: "[S]\r\nX=1\r\n") },
        InstalledUtc: new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc), Enabled: true);

    [Fact]
    public void Upsert_then_Load_round_trips()
    {
        ConfigModStore.Upsert(_tmp, Entry());
        var loaded = ConfigModStore.Load(_tmp).Single();
        Assert.Equal("perf-enh", loaded.Id);
        Assert.True(loaded.Enabled);
        var f = loaded.Files.Single();
        Assert.Equal("Engine.ini", f.FileName);
        Assert.True(f.ExistedAtInstall);
        Assert.Equal(@"C:\snap\Engine.ini.123.bak", f.SnapshotPath);
        Assert.Equal("[S]\r\nX=1\r\n", f.Payload);
    }

    [Fact]
    public void On_disk_json_is_camelCase()
    {
        ConfigModStore.Upsert(_tmp, Entry());
        var json = File.ReadAllText(Path.Combine(_tmp, "config-mods.json"));
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"existedAtInstall\"", json);
        Assert.Contains("\"snapshotPath\"", json);
        Assert.DoesNotContain("\"Id\"", json);
        Assert.DoesNotContain("\"ExistedAtInstall\"", json);
    }

    [Fact]
    public void Upsert_replaces_same_id()
    {
        ConfigModStore.Upsert(_tmp, Entry());
        ConfigModStore.Upsert(_tmp, Entry() with { Enabled = false });
        var loaded = ConfigModStore.Load(_tmp);
        Assert.Single(loaded);
        Assert.False(loaded[0].Enabled);
    }

    [Fact]
    public void Remove_deletes_by_id()
    {
        ConfigModStore.Upsert(_tmp, Entry());
        ConfigModStore.Remove(_tmp, "perf-enh");
        Assert.Empty(ConfigModStore.Load(_tmp));
    }

    [Fact]
    public void Missing_or_corrupt_file_loads_empty()
    {
        Assert.Empty(ConfigModStore.Load(_tmp));
        File.WriteAllText(Path.Combine(_tmp, "config-mods.json"), "{not json");
        Assert.Empty(ConfigModStore.Load(_tmp));
    }
}
