using System.IO;
using System.IO.Compression;
using ModManager.Core;

namespace ModManager.Tests;

public class SaveModFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smf-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    [Fact]
    public void Non_archive_paths_are_passed_through_as_NotASaveMod()
    {
        Directory.CreateDirectory(_root);
        var loose = Path.Combine(_root, "AwesomeMod.pak"); File.WriteAllText(loose, "");
        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { loose }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: NewDir("saves"), snapshotsDir: NewDir("snaps"),
            dataDir: NewDir("data"), saveModPath: null, forbidden: null);
        Assert.Single(verdicts);
        Assert.Equal(SaveModDropOutcome.NotASaveMod, verdicts[0].Outcome);
    }

    [Fact]
    public void A_content_zip_is_NotASaveMod()
    {
        var zip = MakeZip("content.zip", new[] { ("AwesomeMod_P.pak", "x") });
        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { zip }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: NewDir("saves"), snapshotsDir: NewDir("snaps"),
            dataDir: NewDir("data"), saveModPath: null, forbidden: null);
        Assert.Equal(SaveModDropOutcome.NotASaveMod, verdicts[0].Outcome);
    }

    [Fact]
    public void A_world_zip_installs_into_the_save_tree_and_records_an_entry()
    {
        var guid = "0123456789abcdef0123456789abcdef";
        // World zip carries <guid>/data.json
        var zip = MakeZip("world.zip", new[] { ($"{guid}/data.json", "{}") });

        // A save-profiles dir with one profile + a RocksDB version subfolder.
        var profiles = NewDir("saves");
        var oneProfile = Path.Combine(profiles, "user1"); Directory.CreateDirectory(oneProfile);
        Directory.CreateDirectory(Path.Combine(oneProfile, "RocksDB", "1.0"));

        var data = NewDir("data");
        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { zip }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: profiles, snapshotsDir: NewDir("snaps"),
            dataDir: data, saveModPath: null, forbidden: null);

        Assert.Single(verdicts);
        Assert.Equal(SaveModDropOutcome.Installed, verdicts[0].Outcome);
        Assert.Equal(guid, verdicts[0].WorldGuid);
        // File landed under <profile>/RocksDB/1.0/Worlds/<guid>/data.json
        Assert.True(File.Exists(Path.Combine(oneProfile, "RocksDB", "1.0", "Worlds", guid, "data.json")));
        // Store has an entry.
        var entries = SaveModStore.Load(data);
        Assert.Single(entries);
        Assert.Equal(guid, entries[0].Guid);
    }

    [Fact]
    public void A_save_zip_with_no_savedir_fails_with_a_clear_reason()
    {
        var guid = "0123456789abcdef0123456789abcdef";
        var zip = MakeZip("world.zip", new[] { ($"{guid}/data.json", "{}") });
        var profiles = Path.Combine(_root, "nosaves"); // doesn't exist
        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { zip }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: profiles, snapshotsDir: NewDir("snaps"),
            dataDir: NewDir("data"), saveModPath: null, forbidden: null);
        Assert.Equal(SaveModDropOutcome.Failed, verdicts[0].Outcome);
        Assert.False(string.IsNullOrEmpty(verdicts[0].Reason));
    }

    // -------- helpers --------
    private string NewDir(string name) { var d = Path.Combine(_root, name); Directory.CreateDirectory(d); return d; }
    private string MakeZip(string name, IEnumerable<(string Entry, string Content)> entries)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (e, c) in entries)
        {
            var ent = zip.CreateEntry(e);
            using var w = new StreamWriter(ent.Open());
            w.Write(c);
        }
        return path;
    }
}
