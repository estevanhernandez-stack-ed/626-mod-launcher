using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

/// <summary>
/// Wave 1 / A21. <c>get_game_shape</c> reported six files inside a folder it also reported as absent,
/// called the result <c>Aligned</c>, and emitted two notes that contradicted each other — because
/// <c>ContentRoots</c> was built from the mod ROWS and never asked the filesystem. Every one of those
/// mods was disabled, so their files were in the holding folder while the rows still named the
/// location they would occupy.
///
/// <para>It is the tool an agent trusts to answer "are this game's mods where the registration says",
/// and it answered confidently and wrongly. These tests are what should have existed.</para>
/// </summary>
public class GameShapeOnDiskTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "shape-" + Guid.NewGuid().ToString("N"));
    private readonly string _gameRoot;
    private readonly string _dataDir;
    private readonly string _mods;

    public GameShapeOnDiskTests()
    {
        _gameRoot = Path.Combine(_sandbox, "game");
        _dataDir = Path.Combine(_sandbox, "data");
        _mods = Path.Combine(_gameRoot, "mods");
        Directory.CreateDirectory(_gameRoot);
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose() { try { Directory.Delete(_sandbox, true); } catch { } }

    private GameEntry Game() => new()
    {
        Id = "shape-test",
        Engine = "custom",
        GameRoot = _gameRoot,
        DataDir = _dataDir,
        FileExtensions = new[] { "pak" },
        ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
    };

    private void PlacedMod(string name)
    {
        Directory.CreateDirectory(_mods);
        File.WriteAllText(Path.Combine(_mods, name + ".pak"), "x");
    }

    private void HeldMod(string name)
    {
        var dir = Path.Combine(_dataDir, "disabled", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".pak"), "x");
    }

    [Fact]
    public void A_game_whose_mods_are_all_disabled_reports_no_content_roots()
    {
        // The exact failure. Rows exist, files do not, and the old code counted them anyway.
        HeldMod("alpha");
        HeldMod("beta");

        var shape = GameShape.Of(Game());

        Assert.True(shape.ModCount > 0);            // the mods are known
        Assert.Empty(shape.ContentRoots);           // and none of them is placed
    }

    [Fact]
    public void All_disabled_is_reported_as_its_own_state_not_as_Aligned()
    {
        HeldMod("alpha");

        var shape = GameShape.Of(Game());

        Assert.Equal(LocationAlignment.AllDisabled, shape.Alignment);
        Assert.NotEqual(LocationAlignment.Aligned, shape.Alignment);
    }

    [Fact]
    public void All_disabled_is_distinguished_from_having_no_mods_at_all()
    {
        // "You have no mods" and "your mods are all switched off" are different answers to "is this
        // install healthy", and conflating them is the same class of error as the entry itself.
        var withNone = GameShape.Of(Game());
        Assert.Equal(LocationAlignment.NoMods, withNone.Alignment);

        HeldMod("alpha");
        Assert.Equal(LocationAlignment.AllDisabled, GameShape.Of(Game()).Alignment);
    }

    [Fact]
    public void The_notes_never_say_the_folder_is_missing_AND_that_mods_are_where_it_says()
    {
        // Both of these appeared in one payload. They came from two computations that never met.
        HeldMod("alpha");

        var notes = string.Join(" | ", GameShape.Of(Game()).Notes);

        Assert.DoesNotContain("Mods are where the registration says they are", notes);
        Assert.Contains("none is currently placed on disk", notes);
    }

    [Fact]
    public void A_placed_mod_still_produces_a_root_and_still_reads_Aligned()
    {
        // The fix must not make the healthy case report nothing.
        PlacedMod("alpha");

        var shape = GameShape.Of(Game());

        Assert.Single(shape.ContentRoots);
        Assert.Equal(1, shape.ContentRoots[0].FileCount);
        Assert.Equal(LocationAlignment.Aligned, shape.Alignment);
    }

    [Fact]
    public void A_mixed_game_counts_only_what_is_actually_placed()
    {
        PlacedMod("alpha");
        HeldMod("beta");
        HeldMod("gamma");

        var shape = GameShape.Of(Game());

        Assert.Equal(3, shape.ModCount);                       // three known
        Assert.Equal(1, shape.ContentRoots.Sum(r => r.FileCount)); // one on disk
        Assert.Equal(LocationAlignment.Aligned, shape.Alignment);
    }

    [Fact]
    public void A_file_deleted_underneath_the_launcher_stops_being_counted()
    {
        // A root is a claim about disk, so it has to survive the file going away behind our back.
        PlacedMod("alpha");
        Assert.Single(GameShape.Of(Game()).ContentRoots);

        File.Delete(Path.Combine(_mods, "alpha.pak"));
        Assert.Empty(GameShape.Of(Game()).ContentRoots);
    }
}

/// <summary>
/// Wave 1 / A12. The library home said 30 mods, the MCP said 30, and the game view said 27 — because
/// four Faster Ships files are one variant row. Neither number is wrong; reporting only one of them
/// is. An agent said "you have 30 mods" about an install showing 27, and the user reasonably
/// concluded the agent was broken.
/// </summary>
public class GameShapeCountTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "shapecount-" + Guid.NewGuid().ToString("N"));
    private readonly string _gameRoot;
    private readonly string _mods;

    public GameShapeCountTests()
    {
        _gameRoot = Path.Combine(_sandbox, "game");
        _mods = Path.Combine(_gameRoot, "mods");
        Directory.CreateDirectory(_mods);
    }

    public void Dispose() { try { Directory.Delete(_sandbox, true); } catch { } }

    private GameEntry Game() => new()
    {
        Id = "count-test",
        Engine = "custom",
        GameRoot = _gameRoot,
        DataDir = Path.Combine(_sandbox, "data"),
        FileExtensions = new[] { "pak" },
        ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
    };

    private void Place(string name) => File.WriteAllText(Path.Combine(_mods, name + ".pak"), "x");

    [Fact]
    public void Both_counts_are_reported_and_the_family_explains_the_gap()
    {
        // Three keys of one family plus one standalone: four keys, two rows.
        Place("MoreStamina_2x");
        Place("MoreStamina_5x");
        Place("MoreStamina_10x");
        Place("Standalone");

        var shape = GameShape.Of(Game());

        Assert.Equal(4, shape.ModCount);   // keys
        Assert.Equal(2, shape.RowCount);   // what a person counts
        var family = Assert.Single(shape.VariantFamilies);
        Assert.Equal(3, family.Keys.Count);
    }

    [Fact]
    public void The_counts_agree_when_there_is_no_family_to_collapse()
    {
        // The common case must report one number twice rather than an unexplained discrepancy.
        Place("Alpha");
        Place("Beta");

        var shape = GameShape.Of(Game());

        Assert.Equal(shape.ModCount, shape.RowCount);
        Assert.Empty(shape.VariantFamilies);
    }

    [Fact]
    public void RowCount_never_exceeds_ModCount()
    {
        // Collapsing can only ever reduce. A RowCount above ModCount would mean the grouping invented
        // a row, which is worse than the discrepancy this entry is about.
        Place("MoreStamina_2x");
        Place("MoreStamina_5x");

        var shape = GameShape.Of(Game());

        Assert.True(shape.RowCount <= shape.ModCount);
        Assert.Equal(1, shape.RowCount);
    }
}
