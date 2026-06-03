using ModManager.Core;

namespace ModManager.Tests;

public class VortexTakeoverTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "vtx-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string DataDir()
    {
        var d = Path.Combine(_tmp, "data");
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void TakenOverSet_round_trips_as_camelCase()
    {
        var data = DataDir();
        Assert.Empty(TakenOverStore.Load(data));

        TakenOverStore.Add(data, @"C:\game\R5\Binaries\Win64\ue4ss\Mods");
        var json = File.ReadAllText(Path.Combine(data, "taken-over.json"));
        Assert.Contains("\"folders\"", json);   // camelCase key on disk
        Assert.DoesNotContain("\"Folders\"", json);

        var set = TakenOverStore.Load(data);
        Assert.Contains(@"C:\game\R5\Binaries\Win64\ue4ss\Mods", set);
    }

    [Fact]
    public void Add_is_idempotent_and_Remove_drops_the_entry()
    {
        var data = DataDir();
        TakenOverStore.Add(data, @"C:\g\mods");
        TakenOverStore.Add(data, @"C:\g\mods"); // dup
        Assert.Single(TakenOverStore.Load(data));

        TakenOverStore.Remove(data, @"C:\g\mods");
        Assert.Empty(TakenOverStore.Load(data));
    }

    [Fact]
    public void Load_treats_missing_or_corrupt_file_as_empty()
    {
        var data = DataDir();
        Assert.Empty(TakenOverStore.Load(data));               // missing
        File.WriteAllText(Path.Combine(data, "taken-over.json"), "{ not json");
        Assert.Empty(TakenOverStore.Load(data));               // corrupt
    }

    [Fact]
    public void Contains_is_case_insensitive_on_path()
    {
        var data = DataDir();
        TakenOverStore.Add(data, @"C:\Game\Mods");
        // HashSet uses OrdinalIgnoreCase, so Assert.Contains hits the set's own case-insensitive Contains.
        Assert.Contains(@"c:\game\mods", TakenOverStore.Load(data));
    }

    [Fact]
    public void Remove_of_a_non_present_entry_is_a_safe_noop()
    {
        var data = DataDir();
        TakenOverStore.Add(data, @"C:\g\keep");
        TakenOverStore.Remove(data, @"C:\g\never-added"); // must not throw, must not drop the real entry
        Assert.Contains(@"C:\g\keep", TakenOverStore.Load(data));
    }
}
