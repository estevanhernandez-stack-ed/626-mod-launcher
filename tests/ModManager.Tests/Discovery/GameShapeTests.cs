using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

// The launcher always knew both halves — what the registration declares, and where the mods actually
// are — and never put them side by side. Reconstructing that by hand off a real Elden Ring install
// took a dozen filesystem probes and STILL reached the wrong conclusion, because `mod` and `mods` are
// different folders and a truncated listing hid the second one.
//
// These fixtures are built to that live shape: a declared Mod Engine 2 `mod` folder that does not
// exist, mods resolving by direct-inject under a `Game\` play folder, a loader among them.
public class GameShapeTests
{
    // --- fixture ---------------------------------------------------------------------------------

    private static (GameEntry game, string root) FromSoftGame(bool withGameSubfolder = true)
    {
        var root = TestSupport.TempDir("shape-");
        var play = withGameSubfolder ? Path.Combine(root, "Game") : root;
        Directory.CreateDirectory(play);
        // The exe is what makes the play folder a play folder for direct-inject.
        File.WriteAllText(Path.Combine(play, "eldenring.exe"), "x");

        var game = new GameEntry
        {
            Id = "elden-ring",
            GameName = "ELDEN RING",
            GameRoot = root,
            Engine = "fromsoft",
            FileExtensions = Array.Empty<string>(),
            GroupingRule = "by_folder",
            // Declares Mod Engine 2's folder — which this install never creates.
            ModLocations = new[] { new ModLocation("mods", "mods", "mod") },
            DataDir = Path.Combine(root, "_data"),
        };
        return (game, root);
    }

    private static void LoaderWithDllMods(string root, params string[] names)
    {
        var play = Path.Combine(root, "Game");
        File.WriteAllText(Path.Combine(play, "dinput8.dll"), "loader");   // Elden Mod Loader
        var mods = Path.Combine(play, "mods");                            // note: mods, not mod
        Directory.CreateDirectory(mods);
        foreach (var n in names) File.WriteAllText(Path.Combine(mods, n + ".dll"), "x");
    }

    // --- the failure this type exists for ---------------------------------------------------------

    [Fact]
    public void A_declared_location_that_does_not_exist_is_reported_as_missing()
    {
        var (game, root) = FromSoftGame();
        LoaderWithDllMods(root, "SkipTheIntro");

        var shape = GameShape.Of(game);

        var declared = Assert.Single(shape.DeclaredLocations);
        Assert.Equal("mod", declared.Path);
        Assert.False(declared.Exists);
        Assert.Contains(shape.Notes, n => n.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
    }

    // The trap that produced a wrong answer by hand: `mods` must never be read as being inside `mod`.
    // Plain StartsWith says it is. A separator boundary says it is not.
    [Fact]
    public void The_mods_folder_is_not_treated_as_inside_the_declared_mod_folder()
    {
        var (game, root) = FromSoftGame();
        LoaderWithDllMods(root, "RemoveVignette", "SkipTheIntro");

        var shape = GameShape.Of(game);

        Assert.All(shape.ContentRoots, r => Assert.False(r.InsideDeclared));
        Assert.Equal(LocationAlignment.Drifted, shape.Alignment);
    }

    // Drift is a DESCRIPTION, not a defect. A drifted game is usually a perfectly healthy one, and
    // the note has to say so — otherwise a user "fixes" a working install.
    [Fact]
    public void Drift_is_reported_as_not_damage()
    {
        var (game, root) = FromSoftGame();
        LoaderWithDllMods(root, "SkipTheIntro");

        var shape = GameShape.Of(game);

        Assert.True(shape.Managed);
        Assert.Contains(shape.Notes, n => n.Contains("not damage", StringComparison.OrdinalIgnoreCase));
    }

    // The other half of the hand-reconstruction failure: mod paths are relative to the PLAY folder,
    // so measuring them from the game root makes a healthy install look empty.
    [Fact]
    public void The_play_folder_is_reported_when_it_differs_from_the_game_root()
    {
        var (game, root) = FromSoftGame();
        LoaderWithDllMods(root, "SkipTheIntro");

        var shape = GameShape.Of(game);

        Assert.NotNull(shape.PlayFolder);
        Assert.EndsWith("Game", shape.PlayFolder!.TrimEnd(Path.DirectorySeparatorChar));
        Assert.Contains(shape.Notes, n => n.Contains("play folder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_game_without_a_Game_subfolder_resolves_against_its_root()
    {
        var (game, root) = FromSoftGame(withGameSubfolder: false);
        File.WriteAllText(Path.Combine(root, "dinput8.dll"), "loader");

        var shape = GameShape.Of(game);

        Assert.DoesNotContain(shape.Notes, n => n.Contains("play folder", StringComparison.OrdinalIgnoreCase));
    }

    // A loader explains why siblings load from a folder the registration never mentions. Without it
    // named, the drift has no visible cause and reads as misconfiguration.
    [Fact]
    public void A_loader_is_called_out_so_the_drift_has_a_visible_cause()
    {
        var (game, root) = FromSoftGame();
        LoaderWithDllMods(root, "SkipTheIntro");

        var shape = GameShape.Of(game);

        Assert.NotEmpty(shape.Loaders);
        Assert.Contains(shape.Notes, n => n.Contains("Loader present", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Content_roots_carry_the_real_directory_and_a_count()
    {
        var (game, root) = FromSoftGame();
        LoaderWithDllMods(root, "RemoveVignette", "SkipTheIntro", "UltrawideFix");

        var shape = GameShape.Of(game);

        var modsRoot = shape.ContentRoots.FirstOrDefault(r =>
            r.RelativePath.Equals("mods", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(modsRoot);
        Assert.True(modsRoot!.FileCount >= 3);
        Assert.True(Directory.Exists(modsRoot.Absolute));
    }

    [Fact]
    public void The_mechanism_is_reported_so_a_path_mismatch_reads_as_a_different_lane()
    {
        var (game, root) = FromSoftGame();
        LoaderWithDllMods(root, "SkipTheIntro");

        Assert.Equal(ListingMechanism.DirectInject, GameShape.Of(game).Mechanism);
    }

    // --- the aligned case: a declared location that IS doing the work --------------------------

    [Fact]
    public void A_game_whose_mods_sit_in_its_declared_location_reads_as_aligned()
    {
        var root = TestSupport.TempDir("shape-ok-");
        var mods = Path.Combine(root, "mods");
        Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(mods, "CoolMod.pak"), "x");

        var game = new GameEntry
        {
            Id = "aligned-game", GameName = "Aligned", GameRoot = root, Engine = "ue-pak",
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            DataDir = Path.Combine(root, "_data"),
        };

        var shape = GameShape.Of(game);

        Assert.Equal(LocationAlignment.Aligned, shape.Alignment);
        Assert.True(Assert.Single(shape.DeclaredLocations).Exists);
        Assert.Contains(shape.Notes, n => n.Contains("where the registration says", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_game_with_nothing_installed_reports_no_mods_rather_than_drift()
    {
        var root = TestSupport.TempDir("shape-empty-");
        Directory.CreateDirectory(Path.Combine(root, "mods"));

        var game = new GameEntry
        {
            Id = "empty-game", GameName = "Empty", GameRoot = root, Engine = "ue-pak",
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            DataDir = Path.Combine(root, "_data"),
        };

        var shape = GameShape.Of(game);

        Assert.False(shape.Managed);
        Assert.Equal(0, shape.ModCount);
        Assert.Equal(LocationAlignment.NoMods, shape.Alignment);
    }

    // Read-only, like every discovery surface. A report that repaired what it found would be a
    // launcher deciding it knows better; worse, it would do so from a headless agent call.
    [Fact]
    public void Computing_the_shape_writes_nothing()
    {
        var (game, root) = FromSoftGame();
        LoaderWithDllMods(root, "SkipTheIntro");
        var before = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray();

        GameShape.Of(game);

        var after = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray();
        Assert.Equal(before, after);
    }
}
