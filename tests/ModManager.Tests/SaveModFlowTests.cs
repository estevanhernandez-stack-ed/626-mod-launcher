using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
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
            dataDir: NewDir("data"), saveModPath: null, forbidden: null, writeAllowed: true);
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
            dataDir: NewDir("data"), saveModPath: null, forbidden: null, writeAllowed: true);
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
            dataDir: data, saveModPath: null, forbidden: null, writeAllowed: true);

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

    // E1 seventh slice: the record names the kept copy, so a world still resets once the download is gone.
    [Fact]
    public void The_record_names_the_kept_copy_so_reset_works_after_the_download_is_deleted()
    {
        var guid = "0123456789abcdef0123456789abcdef";
        var zip = MakeZip("world.zip", new[] { ($"{guid}/data.json", "ORIGINAL") });
        var profiles = NewDir("saves");
        var oneProfile = Path.Combine(profiles, "user1");
        Directory.CreateDirectory(Path.Combine(oneProfile, "RocksDB", "1.0"));
        var data = NewDir("data");
        var snaps = NewDir("snaps");
        SaveModFlow.TryHandleDrops(new[] { zip }, Array.Empty<string>(), profiles, snaps, data, null, null, writeAllowed: true);
        File.Delete(zip);
        var world = Path.Combine(oneProfile, "RocksDB", "1.0", "Worlds", guid, "data.json");
        File.WriteAllText(world, "PLAYED");

        var entry = SaveModStore.Load(data).Single();
        Assert.Equal(SaveModInstaller.KeptZipPath(data, guid, zip), entry.SourceZip);
        var kept = SaveModStore.KeptZip(data, entry);
        Assert.NotNull(kept);
        SaveModInstaller.ResetWorld(profiles, snaps, kept!, guid, null, null);

        Assert.Equal("ORIGINAL", File.ReadAllText(world));
    }

    [Fact]
    public void Dropping_an_installed_world_again_fails_with_the_reason_and_changes_nothing()
    {
        var guid = "0123456789abcdef0123456789abcdef";
        var profiles = NewDir("saves");
        var oneProfile = Path.Combine(profiles, "user1");
        Directory.CreateDirectory(Path.Combine(oneProfile, "RocksDB", "1.0"));
        var data = NewDir("data");
        SaveModFlow.TryHandleDrops(new[] { MakeZip("world.zip", new[] { ($"{guid}/data.json", "ONE") }) },
            Array.Empty<string>(), profiles, NewDir("snaps"), data, null, null, writeAllowed: true);

        var again = SaveModFlow.TryHandleDrops(new[] { MakeZip("world-2.zip", new[] { ($"{guid}/data.json", "TWO") }) },
            Array.Empty<string>(), profiles, NewDir("snaps2"), data, null, null, writeAllowed: true).Single();

        Assert.Equal(SaveModDropOutcome.AlreadyInstalled, again.Outcome);
        Assert.Contains("already installed", again.Reason);
        Assert.Contains("Reset it", again.Reason);
        Assert.Equal("ONE", File.ReadAllText(Path.Combine(oneProfile, "RocksDB", "1.0", "Worlds", guid, "data.json")));
        Assert.Equal("world", SaveModStore.Load(data).Single().Name);
    }

    // Review on #379: a world folder 626 has no record of can't be reset or removed from Saves, so the reason
    // doesn't send the user there.
    [Fact]
    public void A_world_folder_626_did_not_install_is_refused_with_its_own_reason()
    {
        var guid = "0123456789abcdef0123456789abcdef";
        var profiles = NewDir("saves");
        var worldDir = Path.Combine(profiles, "user1", "RocksDB", "1.0", "Worlds", guid);
        Directory.CreateDirectory(worldDir);
        File.WriteAllText(Path.Combine(worldDir, "data.json"), "THEIRS");
        var data = NewDir("data");

        var v = SaveModFlow.TryHandleDrops(new[] { MakeZip("world.zip", new[] { ($"{guid}/data.json", "MINE") }) },
            Array.Empty<string>(), profiles, NewDir("snaps"), data, null, null, writeAllowed: true).Single();

        Assert.Equal(SaveModDropOutcome.WorldExists, v.Outcome);
        Assert.Contains("626 didn't install it", v.Reason);
        Assert.Equal("THEIRS", File.ReadAllText(Path.Combine(worldDir, "data.json")));
        Assert.Empty(SaveModStore.Load(data));
    }

    // #380 review: a drop that would be refused is refused BEFORE the ban-risk question, so nobody accepts a risk
    // for an install that then doesn't happen. This is Este's Save Hub shape: played, so in the game's own store.
    [Fact]
    public void A_world_the_game_already_holds_is_refused_before_the_ban_risk_question()
    {
        var guid = "0123456789abcdef0123456789abcdef";
        var profiles = NewDir("saves");
        Directory.CreateDirectory(Path.Combine(profiles, "user1", "RocksDB", "1.0"));
        var played = Path.Combine(profiles, "user1", "RocksDB_v2", "1.0", "Worlds", guid);
        Directory.CreateDirectory(played);
        File.WriteAllText(Path.Combine(played, "000123.sst"), "PROGRESS");

        var v = SaveModFlow.TryHandleDrops(new[] { MakeZip("hub.zip", new[] { ($"{guid}/000123.sst", "FRESH") }) },
            Array.Empty<string>(), profiles, NewDir("snaps"), NewDir("data"), null, null, writeAllowed: false).Single();

        Assert.Equal(SaveModDropOutcome.WorldExists, v.Outcome);
        Assert.Contains("has been played", v.Reason);
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
            dataDir: NewDir("data"), saveModPath: null, forbidden: null, writeAllowed: true);
        Assert.Equal(SaveModDropOutcome.Failed, verdicts[0].Outcome);
        Assert.False(string.IsNullOrEmpty(verdicts[0].Reason));
    }

    [Fact]
    public void A_world_zip_with_writes_not_allowed_needs_acknowledgment_and_writes_nothing()
    {
        var guid = "0123456789abcdef0123456789abcdef";
        var zip = MakeZip("world.zip", new[] { ($"{guid}/data.json", "{}") });
        var profiles = NewDir("saves");
        var oneProfile = Path.Combine(profiles, "user1");
        Directory.CreateDirectory(Path.Combine(oneProfile, "RocksDB", "1.0"));
        // A save file that already exists in the profile - the entry-list comparison below would not
        // notice bytes rewritten INTO an existing file, only files added or removed. The hash closes
        // that gap: any in-place write during a gated drop would flip it.
        var existingSave = Path.Combine(oneProfile, "RocksDB", "1.0", "existing.bin");
        File.WriteAllBytes(existingSave, new byte[] { 1, 2, 3, 4, 5 });
        var hashBefore = Sha256(existingSave);
        var snaps = NewDir("snaps");
        var data = NewDir("data");
        var before = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();

        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { zip }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: profiles, snapshotsDir: snaps,
            dataDir: data, saveModPath: null, forbidden: null, writeAllowed: false);

        Assert.Single(verdicts);
        Assert.Equal(SaveModDropOutcome.NeedsAcknowledgment, verdicts[0].Outcome);
        Assert.Equal(guid, verdicts[0].WorldGuid);
        var after = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();
        Assert.Equal(before, after);                 // no world, no snapshot, no store entry
        Assert.Equal(hashBefore, Sha256(existingSave)); // the existing save's own bytes are untouched
        Assert.Empty(SaveModStore.Load(data));
    }

    [Fact]
    public void A_content_zip_is_unaffected_by_writeAllowed()
    {
        var zip = MakeZip("content.zip", new[] { ("AwesomeMod_P.pak", "x") });
        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { zip }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: NewDir("saves"), snapshotsDir: NewDir("snaps"),
            dataDir: NewDir("data"), saveModPath: null, forbidden: null, writeAllowed: false);
        Assert.Equal(SaveModDropOutcome.NotASaveMod, verdicts[0].Outcome);
    }

    // -------- helpers --------
    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    // A save mod for a game whose saves the launcher may not write (EA cloud sync, SaveWritePolicy):
    // still recognised, so regular intake never classifies it, turned away with the reason, and nothing
    // lands. Even with writeAllowed: no acknowledgement unlocks this one.
    [Fact]
    public void A_save_mod_for_a_game_whose_saves_are_refused_is_recognised_and_turned_away()
    {
        var guid = "0123456789abcdef0123456789abcdef";
        var zip = MakeZip("world.zip", new[] { ($"{guid}/data.json", "{}") });
        var profiles = NewDir("saves");
        var oneProfile = Path.Combine(profiles, "user1"); Directory.CreateDirectory(Path.Combine(oneProfile, "RocksDB", "1.0"));
        var data = NewDir("data");

        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { zip }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: profiles, snapshotsDir: NewDir("snaps"),
            dataDir: data, saveModPath: null, forbidden: null, writeAllowed: true,
            writeRefusal: SaveWritePolicy.EaRefusal);

        var v = Assert.Single(verdicts);
        Assert.Equal(SaveModDropOutcome.Failed, v.Outcome);   // carved out of intake, with its reason
        Assert.Equal(SaveWritePolicy.EaRefusal, v.Reason);
        Assert.False(Directory.Exists(Path.Combine(oneProfile, "RocksDB", "1.0", "Worlds")));
        Assert.Empty(SaveModStore.Load(data));
    }

    [Fact]
    public void A_refusal_leaves_a_non_save_mod_to_regular_intake()
    {
        var zip = MakeZip("content.zip", new[] { ("AwesomeMod_P.pak", "x") });
        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { zip }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: NewDir("saves"), snapshotsDir: NewDir("snaps"),
            dataDir: NewDir("data"), saveModPath: null, forbidden: null, writeAllowed: true,
            writeRefusal: SaveWritePolicy.EaRefusal);
        Assert.Equal(SaveModDropOutcome.NotASaveMod, verdicts[0].Outcome);
    }

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
