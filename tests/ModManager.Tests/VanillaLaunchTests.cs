using ModManager.Core;

namespace ModManager.Tests;

public class VanillaLaunchTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "vanilla-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string DataDir() { var d = Path.Combine(_tmp, "data"); Directory.CreateDirectory(d); return d; }

    [Fact]
    public void Stash_round_trips_as_camelCase()
    {
        var data = DataDir();
        Assert.Null(VanillaStashStore.Load(data));

        var stash = new VanillaStash
        {
            ModRows = new() { new StashedModRow { Name = "FasterShips10", Location = "mods" } },
            Frameworks = new() { "ue4ss" },
            DirectInjectProxies = new() { "dwmapi.dll" },
        };
        VanillaStashStore.Save(data, stash);

        var json = File.ReadAllText(Path.Combine(data, "vanilla-stash.json"));
        Assert.Contains("\"modRows\"", json);
        Assert.Contains("\"directInjectProxies\"", json);
        Assert.DoesNotContain("\"ModRows\"", json);

        var loaded = VanillaStashStore.Load(data)!;
        Assert.Equal("FasterShips10", loaded.ModRows[0].Name);
        Assert.Equal("mods", loaded.ModRows[0].Location);
        Assert.Contains("ue4ss", loaded.Frameworks);
        Assert.Contains("dwmapi.dll", loaded.DirectInjectProxies);
    }

    [Fact]
    public void Load_returns_null_for_missing_or_corrupt_stash()
    {
        var data = DataDir();
        Assert.Null(VanillaStashStore.Load(data));
        File.WriteAllText(Path.Combine(data, "vanilla-stash.json"), "{ not json");
        Assert.Null(VanillaStashStore.Load(data));
    }

    [Fact]
    public void Clear_removes_the_stash_file()
    {
        var data = DataDir();
        VanillaStashStore.Save(data, new VanillaStash());
        Assert.NotNull(VanillaStashStore.Load(data));
        VanillaStashStore.Clear(data);
        Assert.Null(VanillaStashStore.Load(data));
    }
}
