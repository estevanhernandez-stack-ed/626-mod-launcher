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

    // A folder under a fake game root, holding a Vortex manifest. Returns (gameRoot, folderAbs).
    private (string gameRoot, string folderAbs) FolderWithVortexMarker()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var folder = Path.Combine(gameRoot, "R5", "Binaries", "Win64", "ue4ss", "Mods");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "vortex.deployment.windrose-scripts.json"), "{\"files\":[]}");
        File.WriteAllText(Path.Combine(folder, "SomeMod.lua"), "real mod content"); // must survive
        return (gameRoot, folder);
    }

    [Fact]
    public void TakeOver_archives_the_marker_out_and_records_the_folder()
    {
        var data = DataDir();
        var (gameRoot, folder) = FolderWithVortexMarker();

        var result = VortexTakeover.TakeOver(data, gameRoot, folder);

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(folder, "vortex.deployment.windrose-scripts.json"))); // marker gone
        Assert.Equal("real mod content", File.ReadAllText(Path.Combine(folder, "SomeMod.lua")));      // mod untouched
        Assert.Null(ToolOwnership.Detect(folder));                                                    // now reads unowned
        Assert.Contains(folder, TakenOverStore.Load(data));                                           // recorded
        Assert.True(Directory.Exists(Path.Combine(data, "vortex-takeover")));                         // archive exists
    }

    [Fact]
    public void Undo_restores_the_marker_byte_for_byte_and_clears_the_record()
    {
        var data = DataDir();
        var (gameRoot, folder) = FolderWithVortexMarker();
        var markerPath = Path.Combine(folder, "vortex.deployment.windrose-scripts.json");
        var before = File.ReadAllBytes(markerPath);

        VortexTakeover.TakeOver(data, gameRoot, folder);
        Assert.False(File.Exists(markerPath));

        VortexTakeover.Undo(data, folder);

        Assert.True(File.Exists(markerPath));
        Assert.Equal(before, File.ReadAllBytes(markerPath));         // byte-for-byte
        Assert.Equal(OwnerTool.Vortex, ToolOwnership.Detect(folder)); // owned again
        Assert.DoesNotContain(folder, TakenOverStore.Load(data));    // record cleared
    }

    [Fact]
    public void TakeOver_on_a_folder_with_no_marker_is_a_noop_success()
    {
        var data = DataDir();
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var folder = Path.Combine(gameRoot, "clean");
        Directory.CreateDirectory(folder);

        var result = VortexTakeover.TakeOver(data, gameRoot, folder);
        Assert.True(result.Success);
        Assert.Empty(result.ArchivedMarkers);
    }

    [Fact]
    public void TakeOver_is_idempotent_no_duplicate_set_entry()
    {
        var data = DataDir();
        var (gameRoot, folder) = FolderWithVortexMarker();

        VortexTakeover.TakeOver(data, gameRoot, folder);
        // simulate a Vortex re-deploy dropping a fresh marker, then take over again
        File.WriteAllText(Path.Combine(folder, "vortex.deployment.windrose-scripts.json"), "{\"files\":[]}");
        VortexTakeover.TakeOver(data, gameRoot, folder);

        Assert.Single(TakenOverStore.Load(data), f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(folder, "vortex.deployment.windrose-scripts.json")));
    }

    [Fact]
    public void TakeOver_rolls_back_when_the_manifest_write_fails_leaving_the_folder_owned()
    {
        var data = DataDir();
        var (gameRoot, folder) = FolderWithVortexMarker();
        var markerPath = Path.Combine(folder, "vortex.deployment.windrose-scripts.json");

        // Sabotage the manifest write: put a DIRECTORY where takeover.json must be written.
        var archiveDir = Path.Combine(data, "vortex-takeover", VortexTakeover.LocationKey(gameRoot, folder));
        Directory.CreateDirectory(Path.Combine(archiveDir, "takeover.json")); // a dir, not a file

        var result = VortexTakeover.TakeOver(data, gameRoot, folder);

        Assert.False(result.Success);                                  // reported failure
        Assert.True(File.Exists(markerPath));                          // marker restored -> folder still owned
        Assert.Equal(OwnerTool.Vortex, ToolOwnership.Detect(folder));  // reads owned again
        Assert.DoesNotContain(folder, TakenOverStore.Load(data));      // NOT recorded (un-undoable state avoided)
    }

    [Fact]
    public void TakeOverGame_takes_over_only_the_passed_locations_not_a_sibling_game()
    {
        var data = DataDir();
        var gameRoot = Path.Combine(_tmp, "GameRoot");

        // Two owned folders for THIS game.
        var locA = Path.Combine(gameRoot, "R5", "A");
        var locB = Path.Combine(gameRoot, "R5", "B");
        foreach (var l in new[] { locA, locB })
        {
            Directory.CreateDirectory(l);
            File.WriteAllText(Path.Combine(l, "vortex.deployment.x.json"), "{}");
        }
        // A sibling game's owned folder that must NOT be touched.
        var sibling = Path.Combine(_tmp, "OtherGame", "mods");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "vortex.deployment.y.json"), "{}");

        var results = VortexTakeover.TakeOverGame(data, gameRoot, new[] { locA, locB });

        Assert.Equal(2, results.Count(r => r.Success));
        Assert.Null(ToolOwnership.Detect(locA));
        Assert.Null(ToolOwnership.Detect(locB));
        Assert.Equal(OwnerTool.Vortex, ToolOwnership.Detect(sibling)); // untouched
    }

    [Fact]
    public void LocationKey_is_distinct_for_folders_that_slug_to_the_same_string()
    {
        var gameRoot = @"C:\game";
        var a = VortexTakeover.LocationKey(gameRoot, @"C:\game\R5\Mods");
        var b = VortexTakeover.LocationKey(gameRoot, @"C:\game\R5_Mods");
        Assert.NotEqual(a, b); // lossy slug alone would collide; the hash suffix separates them
    }
}
