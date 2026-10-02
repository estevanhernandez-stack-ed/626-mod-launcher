using ModManager.Core;
using ModManager.Core.Persistence;

namespace ModManager.Tests;

/// <summary>
/// A6: games.json is changed in one locked step. Every writer used to Load → change → Save on its own,
/// so a writer holding a stale snapshot could land after another and undo it; for the registration
/// repair that meant a game pointing back at a data folder its mods had just left.
/// </summary>
public class RegistryLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "reglock-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private void Seed(params string[] ids)
    {
        var reg = Registry.EmptyRegistry();
        foreach (var id in ids) reg.Games.Add(new GameEntry { Id = id, GameName = id, Engine = "custom" });
        RegistryStore.Save(_root, reg);
    }

    // The race the lock exists for: many writers each changing a different field of the registry.
    // Unlocked, a stale snapshot overwrites another writer's change and some are lost.
    [Fact]
    public void Concurrent_writers_lose_no_change()
    {
        var ids = Enumerable.Range(0, 24).Select(i => "game-" + i).ToArray();
        Seed(ids);

        Parallel.ForEach(ids, new ParallelOptions { MaxDegreeOfParallelism = 8 }, id =>
            RegistryStore.Update(_root, reg => reg.Games.Single(g => g.Id == id).SaveDir = @"C:\saves\" + id));

        var loaded = RegistryStore.Load(_root);
        Assert.All(ids, id => Assert.Equal(@"C:\saves\" + id, loaded.Games.Single(g => g.Id == id).SaveDir));
    }

    [Fact]
    public void An_update_returns_its_result_and_writes_its_change()
    {
        Seed("a");

        var found = RegistryStore.Update(_root, reg => (reg, reg.Games.Any(g => g.Id == "a")));
        RegistryStore.Update(_root, reg => reg.ActiveGameId = "a");

        Assert.True(found);
        Assert.Equal("a", RegistryStore.Load(_root).ActiveGameId);
    }

    // Another process holding the lock: wait, then refuse and write nothing, never write unlocked.
    [Fact]
    public void A_held_lock_times_out_and_writes_nothing()
    {
        Seed("a");
        var lockPath = Path.GetFullPath(RegistryStore.PathFor(_root)) + ".lock";

        using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var e = Assert.Throws<IOException>(() =>
                RegistryStore.Update(_root, reg => reg.ActiveGameId = "a", TimeSpan.FromMilliseconds(200)));
            Assert.Contains("Nothing was changed", e.Message);
        }

        Assert.Null(RegistryStore.Load(_root).ActiveGameId);
    }

    [Fact]
    public void The_lock_file_is_gone_after_an_update()
    {
        Seed("a");

        RegistryStore.Update(_root, reg => reg.ActiveGameId = "a");

        Assert.False(File.Exists(Path.GetFullPath(RegistryStore.PathFor(_root)) + ".lock"));
    }

    // A change that throws writes nothing and releases the lock for the next writer.
    [Fact]
    public void A_change_that_throws_writes_nothing_and_frees_the_lock()
    {
        Seed("a");

        Assert.Throws<InvalidOperationException>(() =>
            RegistryStore.Update(_root, reg => { reg.ActiveGameId = "a"; throw new InvalidOperationException(); }));

        Assert.Null(RegistryStore.Load(_root).ActiveGameId);
        RegistryStore.Update(_root, reg => reg.ActiveGameId = "a", TimeSpan.FromMilliseconds(200));
        Assert.Equal("a", RegistryStore.Load(_root).ActiveGameId);
    }

    // No surface outside Core writes games.json unlocked. The next new writer copies whatever call it
    // finds first, so the only one it can find is the locked one.
    [Theory]
    [InlineData("ModManager.App")]
    [InlineData("ModManager.Mcp")]
    public void No_surface_writes_the_registry_unlocked(string project)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ModManager.App"))) dir = dir.Parent;
        Assert.True(dir is not null, "could not find src/ above the test output");
        var sep = Path.DirectorySeparatorChar;

        var offenders = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src", project), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .Where(f => File.ReadAllText(f) is var src && (src.Contains("RegistryStore.Save(") || src.Contains("SaveRegistry(")))
            .ToList();

        Assert.True(offenders.Count == 0, "Write games.json through RegistryStore.Update / UpdateRegistry in: " + string.Join(", ", offenders));
    }
}
