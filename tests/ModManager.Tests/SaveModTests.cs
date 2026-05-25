using System.IO.Compression;
using ModManager.Core;

namespace ModManager.Tests;

// Save/world mods — a mod CLASS that installs into the user's SAVE TREE (Windrose:
// RocksDB\<version>\Worlds\<GUID>), not Content\Paks. SAFETY-CRITICAL: every mutating op
// snapshots first, never writes under a game-managed folder (RocksDB_v2 / _Backups), and
// every extract is zip-slip guarded. These tests pin those three invariants.
public class SaveModTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mmb-savemod-" + Guid.NewGuid().ToString("N"));

    // The save tree: <profiles>\<one profile folder>\RocksDB\<ver>\Worlds (+ a sibling RocksDB_v2).
    private string Profiles => Path.Combine(_root, "SaveGames");
    private string Snaps => Path.Combine(_root, "snaps");
    private string Store => Path.Combine(_root, "store");
    private string Data => Path.Combine(_root, "data");

    private const string Guid32 = "5391A30D5D70487C9486B8E60428ED3B";

    public SaveModTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    // Build a save tree: <profiles>\<prof>\RocksDB\<ver>\Worlds (exists) + RocksDB_v2 sibling we
    // assert is never touched. Returns the profile folder.
    private string MakeSaveTree(string profName = "76561198000000000", string version = "0.10.0")
    {
        var prof = Path.Combine(Profiles, profName);
        Directory.CreateDirectory(Path.Combine(prof, "RocksDB", version, "Worlds"));
        // game-managed: must NEVER be written under
        Directory.CreateDirectory(Path.Combine(prof, "RocksDB_v2"));
        File.WriteAllText(Path.Combine(prof, "RocksDB_v2", "sacred.db"), "GAME-OWNED");
        return prof;
    }

    private string MakeZip(string name, params (string entry, string content)[] entries)
    {
        var path = Path.Combine(_root, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
            using (var w = new StreamWriter(zip.CreateEntry(entry).Open())) w.Write(content);
        return path;
    }

    // ---------------- Detection ----------------

    [Fact]
    public void Detect_world_path_with_guid_is_a_save_mod()
    {
        var v = SaveModDetect.Detect(new[] { $"Worlds/{Guid32}/level.db", $"Worlds/{Guid32}/meta.json" },
            Array.Empty<string>());
        Assert.True(v.IsSaveMod);
        Assert.Equal(Guid32, v.WorldGuid);
    }

    [Fact]
    public void Detect_top_level_guid_folder_is_a_save_mod()
    {
        var v = SaveModDetect.Detect(new[] { $"{Guid32}/level.db" }, Array.Empty<string>());
        Assert.True(v.IsSaveMod);
        Assert.Equal(Guid32, v.WorldGuid);
    }

    [Fact]
    public void Detect_dashed_guid_folder_is_a_save_mod()
    {
        const string dashed = "5391a30d-5d70-487c-9486-b8e60428ed3b";
        var v = SaveModDetect.Detect(new[] { $"Worlds/{dashed}/level.db" }, Array.Empty<string>());
        Assert.True(v.IsSaveMod);
        Assert.Equal(dashed, v.WorldGuid);
    }

    [Fact]
    public void Detect_save_type_extension_contents_is_a_save_mod()
    {
        // No GUID folder, no paks, but a declared save-type extension present -> save mod.
        var v = SaveModDetect.Detect(new[] { "ER0000.sl2" }, new[] { ".sl2" });
        Assert.True(v.IsSaveMod);
        Assert.Null(v.WorldGuid);
    }

    [Fact]
    public void Detect_pak_zip_is_not_a_save_mod()
    {
        Assert.False(SaveModDetect.Detect(new[] { "mod_P.pak" }, Array.Empty<string>()).IsSaveMod);
        Assert.False(SaveModDetect.Detect(new[] { "x.ucas", "x.utoc" }, Array.Empty<string>()).IsSaveMod);
    }

    [Fact]
    public void Detect_pak_vetoes_even_alongside_a_guid_folder()
    {
        // A pak present means it's a content mod regardless of any GUID-looking folder.
        var v = SaveModDetect.Detect(new[] { $"Worlds/{Guid32}/level.db", "mod_P.pak" }, Array.Empty<string>());
        Assert.False(v.IsSaveMod);
    }

    // ---------------- ResolveWorldsTarget ----------------

    [Fact]
    public void Resolve_default_path_returns_worlds_under_scanned_version_and_creates_it()
    {
        MakeSaveTree(version: "0.10.0");
        var target = SaveModInstaller.ResolveWorldsTarget(Profiles, null, null);
        var expected = Path.Combine(Profiles, "76561198000000000", "RocksDB", "0.10.0", "Worlds");
        Assert.Equal(expected, target);
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void Resolve_picks_highest_version_when_several()
    {
        var prof = Path.Combine(Profiles, "prof");
        Directory.CreateDirectory(Path.Combine(prof, "RocksDB", "0.9.0", "Worlds"));
        Directory.CreateDirectory(Path.Combine(prof, "RocksDB", "0.10.0", "Worlds"));
        Directory.CreateDirectory(Path.Combine(prof, "RocksDB", "0.2.0", "Worlds"));

        var target = SaveModInstaller.ResolveWorldsTarget(Profiles, null, null);
        Assert.Equal(Path.Combine(prof, "RocksDB", "0.10.0", "Worlds"), target);
    }

    [Fact]
    public void Resolve_substitutes_version_token_in_custom_path()
    {
        MakeSaveTree(version: "0.10.0");
        var target = SaveModInstaller.ResolveWorldsTarget(Profiles, "RocksDB/{version}/Worlds", null);
        Assert.Equal(Path.Combine(Profiles, "76561198000000000", "RocksDB", "0.10.0", "Worlds"), target);
    }

    [Fact]
    public void Resolve_refuses_a_forbidden_segment_in_savemodpath_and_writes_nothing_under_v2()
    {
        var prof = MakeSaveTree(version: "0.10.0");
        var before = Directory.GetFileSystemEntries(Path.Combine(prof, "RocksDB_v2"), "*", SearchOption.AllDirectories).Length;

        var ex = Assert.Throws<InvalidOperationException>(
            () => SaveModInstaller.ResolveWorldsTarget(Profiles, "RocksDB_v2/{version}/Worlds", null));
        Assert.Contains("RocksDB_v2", ex.Message);

        // nothing created under v2
        var after = Directory.GetFileSystemEntries(Path.Combine(prof, "RocksDB_v2"), "*", SearchOption.AllDirectories).Length;
        Assert.Equal(before, after);
        Assert.False(Directory.Exists(Path.Combine(prof, "RocksDB_v2", "0.10.0")));
    }

    [Fact]
    public void Resolve_refuses_a_profile_supplied_forbidden_name()
    {
        MakeSaveTree(version: "0.10.0");
        // Default path is fine, but the caller declares an extra forbidden segment that it hits.
        var ex = Assert.Throws<InvalidOperationException>(
            () => SaveModInstaller.ResolveWorldsTarget(Profiles, "RocksDB/{version}/Worlds", new[] { "Worlds" }));
        Assert.Contains("Worlds", ex.Message);
    }

    [Fact]
    public void Resolve_throws_clear_error_with_no_profile()
    {
        Directory.CreateDirectory(Profiles); // empty
        var ex = Assert.Throws<InvalidOperationException>(() => SaveModInstaller.ResolveWorldsTarget(Profiles, null, null));
        Assert.Contains("No save profile", ex.Message);
    }

    [Fact]
    public void Resolve_throws_clear_error_with_multiple_profiles()
    {
        Directory.CreateDirectory(Path.Combine(Profiles, "a"));
        Directory.CreateDirectory(Path.Combine(Profiles, "b"));
        var ex = Assert.Throws<InvalidOperationException>(() => SaveModInstaller.ResolveWorldsTarget(Profiles, null, null));
        Assert.Contains("Multiple save profiles", ex.Message);
    }

    [Fact]
    public void Resolve_throws_clear_error_with_no_rocksdb_version()
    {
        var prof = Path.Combine(Profiles, "prof");
        Directory.CreateDirectory(prof); // profile exists but no RocksDB
        var ex = Assert.Throws<InvalidOperationException>(() => SaveModInstaller.ResolveWorldsTarget(Profiles, null, null));
        Assert.Contains("RocksDB", ex.Message);
    }

    // ---------------- InstallWorld ----------------

    [Fact]
    public void Install_lands_world_snapshots_first_copies_zip_and_leaves_v2_untouched()
    {
        var prof = MakeSaveTree(version: "0.10.0");
        var zip = MakeZip("world.zip",
            ($"Worlds/{Guid32}/level.db", "WORLD-DATA"),
            ($"Worlds/{Guid32}/meta.json", "{}"));

        var installed = SaveModInstaller.InstallWorld(Profiles, Snaps, Store, zip, Guid32, null, null);

        // (b) world landed at RocksDB\<ver>\Worlds\<guid>\...
        var worldDir = Path.Combine(prof, "RocksDB", "0.10.0", "Worlds", Guid32);
        Assert.Equal(worldDir, installed);
        Assert.Equal("WORLD-DATA", File.ReadAllText(Path.Combine(worldDir, "level.db")));
        Assert.True(File.Exists(Path.Combine(worldDir, "meta.json")));

        // (a) a snapshot zip was taken (snapshot-first)
        Assert.NotEmpty(SaveManager.ListSnapshots(Snaps));

        // (c) the original zip copied into the store dir
        Assert.True(File.Exists(Path.Combine(Store, Path.GetFileName(zip))));

        // (d) RocksDB_v2 untouched
        Assert.Equal("GAME-OWNED", File.ReadAllText(Path.Combine(prof, "RocksDB_v2", "sacred.db")));
    }

    [Fact]
    public void Install_refuses_zip_slip_entries_but_lands_safe_ones()
    {
        var prof = MakeSaveTree(version: "0.10.0");
        var zip = MakeZip("evil.zip",
            ($"Worlds/{Guid32}/good.db", "OK"),
            ($"Worlds/{Guid32}/../../../evil.txt", "PWN"));

        SaveModInstaller.InstallWorld(Profiles, Snaps, Store, zip, Guid32, null, null);

        var worldDir = Path.Combine(prof, "RocksDB", "0.10.0", "Worlds", Guid32);
        Assert.True(File.Exists(Path.Combine(worldDir, "good.db")));     // safe entry landed
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));      // never escaped
        Assert.False(File.Exists(Path.Combine(Profiles, "evil.txt")));
    }

    // ---------------- ResetWorld ----------------

    [Fact]
    public void Reset_restores_original_contents_and_snapshots_first()
    {
        var prof = MakeSaveTree(version: "0.10.0");
        var zip = MakeZip("world.zip", ($"Worlds/{Guid32}/level.db", "ORIGINAL"));
        SaveModInstaller.InstallWorld(Profiles, Snaps, Store, zip, Guid32, null, null);

        var worldDir = Path.Combine(prof, "RocksDB", "0.10.0", "Worlds", Guid32);
        var levelDb = Path.Combine(worldDir, "level.db");
        File.WriteAllText(levelDb, "MUTATED");                 // user/game mutated the world
        var keptZip = Path.Combine(Store, Path.GetFileName(zip));

        var snapsBefore = SaveManager.ListSnapshots(Snaps).Count;
        SaveModInstaller.ResetWorld(Profiles, Snaps, keptZip, Guid32, null, null);

        Assert.Equal("ORIGINAL", File.ReadAllText(levelDb));   // restored from kept zip
        Assert.True(SaveManager.ListSnapshots(Snaps).Count > snapsBefore); // fresh snapshot taken first
    }

    // ---------------- RemoveWorld ----------------

    [Fact]
    public void Remove_deletes_the_world_snapshots_first_and_leaves_v2_untouched()
    {
        var prof = MakeSaveTree(version: "0.10.0");
        var zip = MakeZip("world.zip", ($"Worlds/{Guid32}/level.db", "X"));
        SaveModInstaller.InstallWorld(Profiles, Snaps, Store, zip, Guid32, null, null);
        var worldDir = Path.Combine(prof, "RocksDB", "0.10.0", "Worlds", Guid32);
        Assert.True(Directory.Exists(worldDir));

        var snapsBefore = SaveManager.ListSnapshots(Snaps).Count;
        SaveModInstaller.RemoveWorld(Profiles, Snaps, Guid32, null, null);

        Assert.False(Directory.Exists(worldDir));                            // world gone
        Assert.True(SaveManager.ListSnapshots(Snaps).Count > snapsBefore);   // snapshot first
        Assert.Equal("GAME-OWNED", File.ReadAllText(Path.Combine(prof, "RocksDB_v2", "sacred.db"))); // v2 untouched
    }

    [Fact]
    public void Remove_refuses_when_savemodpath_is_forbidden()
        // Defense-in-depth: even Remove must refuse a forbidden resolved target.
        => Assert.Throws<InvalidOperationException>(() =>
        {
            MakeSaveTree(version: "0.10.0");
            SaveModInstaller.RemoveWorld(Profiles, Snaps, Guid32, "RocksDB_v2/{version}/Worlds", null);
        });

    // ---------------- DefaultForbidden ----------------

    [Fact]
    public void DefaultForbidden_lists_the_game_managed_folders()
    {
        Assert.Contains("RocksDB_v2", SaveModInstaller.DefaultForbidden);
        Assert.Contains("RocksDB_v2_Backups", SaveModInstaller.DefaultForbidden);
    }

    // ---------------- SaveModStore ----------------

    [Fact]
    public void Store_upsert_then_load_returns_the_entry()
    {
        SaveModStore.Upsert(Data, new SaveModEntry(Guid32, "Cool World", "world.zip", DateTime.UtcNow));
        var loaded = SaveModStore.Load(Data);
        Assert.Single(loaded);
        Assert.Equal("Cool World", loaded[0].Name);
        Assert.Equal(Guid32, loaded[0].Guid);
    }

    [Fact]
    public void Store_upsert_same_guid_replaces_case_insensitively()
    {
        SaveModStore.Upsert(Data, new SaveModEntry(Guid32, "Old Name", "a.zip", DateTime.UtcNow));
        SaveModStore.Upsert(Data, new SaveModEntry(Guid32.ToLowerInvariant(), "New Name", "b.zip", DateTime.UtcNow));
        var loaded = SaveModStore.Load(Data);
        Assert.Single(loaded);
        Assert.Equal("New Name", loaded[0].Name);
    }

    [Fact]
    public void Store_remove_drops_the_entry()
    {
        SaveModStore.Upsert(Data, new SaveModEntry(Guid32, "Keep", "k.zip", DateTime.UtcNow));
        SaveModStore.Upsert(Data, new SaveModEntry("OTHERGUID", "Drop", "d.zip", DateTime.UtcNow));
        SaveModStore.Remove(Data, "OTHERGUID");
        var loaded = SaveModStore.Load(Data);
        Assert.Single(loaded);
        Assert.Equal("Keep", loaded[0].Name);
    }

    [Fact]
    public void Store_load_is_tolerant_of_missing_and_corrupt_files()
    {
        Assert.Empty(SaveModStore.Load(Data)); // missing dir/file

        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "save-mods.json"), "{ this is not valid json ]");
        Assert.Empty(SaveModStore.Load(Data)); // corrupt -> empty, not a throw
    }
}
