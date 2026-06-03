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

    [Fact]
    public async Task StepAside_records_exactly_the_active_set_and_steps_each_aside()
    {
        var data = DataDir();
        var disabledRows = new List<string>();
        var disabledFw = new List<string>();
        var disabledProxies = new List<string>();

        var ops = new VanillaLaunchOps
        {
            ActiveModRows = () => new[] { new StashedModRow { Name = "FasterShips10", Location = "mods" },
                                          new StashedModRow { Name = "RareDrops", Location = "mods" } },
            ActiveFrameworks = () => new[] { "ue4ss" },
            ActiveDirectInjectProxies = () => new[] { "dwmapi.dll" },
            DisableModRow = (name, loc) => { disabledRows.Add(name); return Task.CompletedTask; },
            EnableModRow = (name, loc) => Task.CompletedTask,
            DisableFramework = id => disabledFw.Add(id),
            EnableFramework = id => { },
            DisableDirectInjectProxy = p => disabledProxies.Add(p),
            EnableDirectInjectProxy = p => { },
        };

        var result = await VanillaLaunch.StepAsideAsync(data, ops);

        Assert.True(result.Success);
        Assert.Equal(new[] { "FasterShips10", "RareDrops" }, disabledRows);
        Assert.Equal(new[] { "ue4ss" }, disabledFw);
        Assert.Equal(new[] { "dwmapi.dll" }, disabledProxies);

        var stash = VanillaStashStore.Load(data)!;
        Assert.Equal(2, stash.ModRows.Count);
        Assert.Contains(stash.ModRows, r => r.Name == "FasterShips10" && r.Location == "mods");
        Assert.Equal(new[] { "ue4ss" }, stash.Frameworks);
        Assert.Equal(new[] { "dwmapi.dll" }, stash.DirectInjectProxies);
    }

    [Fact]
    public async Task StepAside_rolls_back_and_writes_no_stash_when_a_step_fails()
    {
        var data = DataDir();
        var enabledBack = new List<string>();
        var ops = new VanillaLaunchOps
        {
            ActiveModRows = () => new[] { new StashedModRow { Name = "A", Location = "mods" },
                                          new StashedModRow { Name = "B", Location = "mods" } },
            ActiveFrameworks = () => Array.Empty<string>(),
            ActiveDirectInjectProxies = () => Array.Empty<string>(),
            DisableModRow = (name, loc) => name == "B"
                ? throw new IOException("locked")
                : Task.CompletedTask,
            EnableModRow = (name, loc) => { enabledBack.Add(name); return Task.CompletedTask; },
            DisableFramework = _ => { }, EnableFramework = _ => { },
            DisableDirectInjectProxy = _ => { }, EnableDirectInjectProxy = _ => { },
        };

        var result = await VanillaLaunch.StepAsideAsync(data, ops);

        Assert.False(result.Success);
        Assert.Contains("A", enabledBack);
        Assert.Null(VanillaStashStore.Load(data));
    }

    [Fact]
    public async Task Restore_re_enables_exactly_the_stashed_set_and_clears_the_stash()
    {
        var data = DataDir();
        VanillaStashStore.Save(data, new VanillaStash
        {
            ModRows = new() { new StashedModRow { Name = "FasterShips10", Location = "mods" },
                              new StashedModRow { Name = "RareDrops", Location = "mods" } },
            Frameworks = new() { "ue4ss" },
            DirectInjectProxies = new() { "dwmapi.dll" },
        });

        var enabledRows = new List<string>();
        var enabledFw = new List<string>();
        var enabledProxies = new List<string>();
        var ops = MakeOps(
            enableRow: (n, l) => { enabledRows.Add(n); return Task.CompletedTask; },
            enableFw: id => enabledFw.Add(id),
            enableProxy: p => enabledProxies.Add(p));

        var result = await VanillaLaunch.RestoreAsync(data, ops);

        Assert.True(result.Success);
        Assert.Equal(new[] { "FasterShips10", "RareDrops" }, enabledRows);
        Assert.Equal(new[] { "ue4ss" }, enabledFw);
        Assert.Equal(new[] { "dwmapi.dll" }, enabledProxies);
        Assert.Null(VanillaStashStore.Load(data));
    }

    [Fact]
    public async Task Restore_with_no_stash_is_a_safe_noop()
    {
        var data = DataDir();
        var result = await VanillaLaunch.RestoreAsync(data, MakeOps());
        Assert.True(result.Success);
    }

    [Fact]
    public void CurrentMode_is_Vanilla_when_a_stash_exists_else_Modded()
    {
        var data = DataDir();
        Assert.Equal(LaunchMode.Modded, VanillaLaunch.CurrentMode(data));
        VanillaStashStore.Save(data, new VanillaStash());
        Assert.Equal(LaunchMode.Vanilla, VanillaLaunch.CurrentMode(data));
    }

    // Helper: an ops with no-op defaults, overridable per call.
    private static VanillaLaunchOps MakeOps(
        Func<string, string, Task>? enableRow = null,
        Action<string>? enableFw = null,
        Action<string>? enableProxy = null) => new()
    {
        ActiveModRows = () => Array.Empty<StashedModRow>(),
        ActiveFrameworks = () => Array.Empty<string>(),
        ActiveDirectInjectProxies = () => Array.Empty<string>(),
        DisableModRow = (_, _) => Task.CompletedTask,
        EnableModRow = enableRow ?? ((_, _) => Task.CompletedTask),
        DisableFramework = _ => { },
        EnableFramework = enableFw ?? (_ => { }),
        DisableDirectInjectProxy = _ => { },
        EnableDirectInjectProxy = enableProxy ?? (_ => { }),
    };
}
