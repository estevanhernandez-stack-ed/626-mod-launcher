using System.IO.Compression;
using ModManager.Core;

namespace ModManager.Tests;

// worldGuid becomes a directory name combined into the SAVE TREE and is attacker-influenced (it comes
// from a dropped zip). A traversal-laden worldGuid must never let install/reset/remove escape the
// Worlds folder — especially never delete/write the game-managed RocksDB_v2 tree. These pin that guard.
public class SaveModSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mmb-savemod-sec-" + Guid.NewGuid().ToString("N"));
    private string Profiles => Path.Combine(_root, "SaveProfiles");
    private string Profile => Path.Combine(Profiles, "76561198000000000");
    private string SacredV2 => Path.Combine(Profile, "RocksDB_v2", "sacred.db");
    private string Snapshots => Path.Combine(_root, "snapshots");
    private string Store => Path.Combine(_root, "store");

    public SaveModSecurityTests()
    {
        // One profile, a real RocksDB version dir, and a game-managed RocksDB_v2 sentinel that must survive.
        Directory.CreateDirectory(Path.Combine(Profile, "RocksDB", "0.10.0", "Worlds"));
        Directory.CreateDirectory(Path.GetDirectoryName(SacredV2)!);
        File.WriteAllText(SacredV2, "GAME-OWNED");
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private string MakeWorldZip(string guid)
    {
        var path = Path.Combine(_root, "world.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry($"{guid}/level.db").Open())) w.Write("world");
        return path;
    }

    // Goes up out of Worlds/<version>/RocksDB and into the sibling RocksDB_v2 — the exact folder the
    // feature swears never to touch.
    private const string EvilGuid = @"..\..\..\RocksDB_v2";

    [Fact]
    public void RemoveWorld_refuses_a_traversal_worldGuid_and_leaves_v2_untouched()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SaveModInstaller.RemoveWorld(Profiles, Snapshots, EvilGuid, null, null));
        Assert.True(File.Exists(SacredV2));
        Assert.Equal("GAME-OWNED", File.ReadAllText(SacredV2));
    }

    [Fact]
    public void InstallWorld_refuses_a_traversal_worldGuid_and_writes_nothing_outside_worlds()
    {
        var zip = MakeWorldZip(EvilGuid);
        Assert.Throws<InvalidOperationException>(() =>
            SaveModInstaller.InstallWorld(Profiles, Snapshots, Store, zip, EvilGuid, null, null));
        Assert.True(File.Exists(SacredV2)); // v2 untouched
        Assert.False(File.Exists(Path.Combine(Profile, "RocksDB_v2", "level.db"))); // nothing escaped into v2
    }

    [Fact]
    public void ResetWorld_refuses_a_traversal_worldGuid()
    {
        var zip = MakeWorldZip(EvilGuid);
        Assert.Throws<InvalidOperationException>(() =>
            SaveModInstaller.ResetWorld(Profiles, Snapshots, zip, EvilGuid, null, null));
        Assert.True(File.Exists(SacredV2));
    }

    [Fact]
    public void A_worldGuid_with_a_separator_or_non_guid_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SaveModInstaller.RemoveWorld(Profiles, Snapshots, "a/b", null, null));
        Assert.Throws<InvalidOperationException>(() =>
            SaveModInstaller.RemoveWorld(Profiles, Snapshots, "not-a-guid", null, null));
    }

    [Fact]
    public void A_legitimate_guid_worldGuid_is_accepted()
    {
        // Sanity: the guard doesn't reject a real GUID (32 hex) — remove of a non-existent world is a no-op.
        var ex = Record.Exception(() =>
            SaveModInstaller.RemoveWorld(Profiles, Snapshots, "5391A30D5D70487C9486B8E60428ED3B", null, null));
        Assert.Null(ex);
    }
}
