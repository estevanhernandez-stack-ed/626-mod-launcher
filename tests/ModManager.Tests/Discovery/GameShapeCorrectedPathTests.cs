using ModManager.Core;
using ModManager.Core.Discovery;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Discovery;

/// <summary>
/// B1 from the registration-repair smoke. <c>GameShape</c> decided "declared or launcher-derived" by
/// matching each STORED location's path against the scanner's locations - which hold the path AFTER a
/// manifest correction. When the game definition corrected the stored preset default (Elden Ring
/// stores <c>mod</c>, the feed says <c>mods</c>) nothing matched, so the registration's own location
/// read as "(added by the launcher, not declared)", <c>get_game_shape</c> said <c>declared:false</c>,
/// and because the attention predicate counts declared entries only, the SETUP chip could never fire
/// for a corrected location that is missing.
///
/// <para>Declared means "comes from the registration's list", before or after correction. Only a
/// location the launcher appends (the UE4SS mods folder) is derived.</para>
///
/// <para>Mutates the process-global EffectiveManifest, so it joins the serialized ManifestState
/// collection and resets the remote on the way out.</para>
/// </summary>
[Collection("ManifestState")]
public class GameShapeCorrectedPathTests : IDisposable
{
    private const string Id = "shape-corrected-path";

    public void Dispose() => EffectiveManifest.SetRemote(null);

    // A custom-engine registration still on the preset default ("mods"), joined by id to a feed
    // entry that says the mods really live in nativePC - the A5 fixture's shape.
    private static void FeedSays(string modPath)
        => EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = Id,
                    Name = "Shape Corrected Path",
                    Engine = "custom",
                    ModPath = modPath,
                    FileExtensions = new[] { "arc" },
                    Provenance = new ManifestProvenance { Sources = new[] { "known-engines" } },
                },
            },
        });

    private static GameEntry Game(string root) => new()
    {
        Id = Id,
        GameName = "Shape Corrected Path",
        GameRoot = root,
        Engine = "custom",
        FileExtensions = new[] { "pak" },
        GroupingRule = "filename_no_ext",
        ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        DataDir = Path.Combine(root, "_data"),
    };

    [Fact]
    public void A_location_the_definition_corrected_is_still_declared()
    {
        FeedSays("nativePC");
        var root = TestSupport.TempDir("shape-corrected-");

        var shape = GameShape.Of(Game(root));

        var loc = Assert.Single(shape.DeclaredLocations);
        Assert.True(loc.Declared);
        Assert.Equal("nativePC", loc.Path);                           // the path the scanner uses
        Assert.Equal(Path.Combine(root, "nativePC"), loc.Absolute);
        Assert.Equal("mods", loc.CorrectedFrom);                      // and what the registration said
        Assert.DoesNotContain(shape.Notes, n => n.Contains("The launcher's own", StringComparison.Ordinal));
    }

    [Fact]
    public void An_uncorrected_location_says_nothing_about_a_correction()
    {
        FeedSays("mods");   // the feed agrees with the registration
        var root = TestSupport.TempDir("shape-uncorrected-");

        var loc = Assert.Single(GameShape.Of(Game(root)).DeclaredLocations);

        Assert.True(loc.Declared);
        Assert.Equal("mods", loc.Path);
        Assert.Null(loc.CorrectedFrom);
    }

    // Este's 2026-08-18 ruling (recorded in ModFolderSeed): a folder the game DEFINITION names that is
    // not on disk means "not started", not "broken". A corrected location's path is the definition's,
    // so its absence on a game with no mods yet must not raise the SETUP chip. It stays declared, and
    // the note still says the folder is missing.
    [Fact]
    public void A_missing_corrected_location_with_no_mods_stays_quiet()
    {
        FeedSays("nativePC");
        var root = TestSupport.TempDir("shape-corrected-missing-");
        var game = Game(root);

        var shape = GameShape.Of(game);

        Assert.Equal(0, shape.ModCount);
        Assert.True(Assert.Single(shape.DeclaredLocations).Declared);
        Assert.False(shape.NeedsAttention);
        Assert.False(GameShape.NeedsAttentionFor(Scanner.GameContext(game), shape.ModCount));
        Assert.Contains(shape.Notes, n => n.Contains("Declared mod location 'nativePC'", StringComparison.Ordinal)
                                          && n.Contains("corrected it from 'mods'", StringComparison.Ordinal));
    }

    // The other half of the ruling: a declared path the REGISTRATION chose (nothing corrected it) that
    // is missing, with nothing found, is still the shape the chip exists for.
    [Fact]
    public void A_missing_uncorrected_declared_location_with_no_mods_still_needs_attention()
    {
        FeedSays("mods");
        var root = TestSupport.TempDir("shape-uncorrected-missing-");
        var game = Game(root);

        var shape = GameShape.Of(game);

        Assert.Null(Assert.Single(shape.DeclaredLocations).CorrectedFrom);
        Assert.True(shape.NeedsAttention);
        Assert.True(GameShape.NeedsAttentionFor(Scanner.GameContext(game), shape.ModCount));
    }

    // m2: the registration's OWN folder still holding files after a correction moved the launcher away
    // from it is worth saying - those files are no longer read.
    [Fact]
    public void Files_left_in_the_stored_folder_after_a_correction_are_named()
    {
        FeedSays("nativePC");
        var root = TestSupport.TempDir("shape-corrected-leftover-");
        TestSupport.Write(Path.Combine(root, "mods", "Old.pak"), "x");
        TestSupport.Write(Path.Combine(root, "mods", "sub", "Older.pak"), "x");

        var shape = GameShape.Of(Game(root));

        Assert.Contains("The registration's own 'mods' folder holds 2 files the launcher no longer reads.", shape.Notes);
    }

    [Fact]
    public void An_empty_or_absent_stored_folder_adds_no_leftover_note()
    {
        FeedSays("nativePC");
        var absent = GameShape.Of(Game(TestSupport.TempDir("shape-corrected-noleft-")));
        var emptyRoot = TestSupport.TempDir("shape-corrected-emptyleft-");
        Directory.CreateDirectory(Path.Combine(emptyRoot, "mods"));
        var empty = GameShape.Of(Game(emptyRoot));

        Assert.DoesNotContain(absent.Notes, n => n.Contains("no longer reads", StringComparison.Ordinal));
        Assert.DoesNotContain(empty.Notes, n => n.Contains("no longer reads", StringComparison.Ordinal));
    }

    // And stays quiet on a working install: the corrected folder exists and holds a mod.
    [Fact]
    public void A_working_corrected_location_does_not_need_attention()
    {
        FeedSays("nativePC");
        var root = TestSupport.TempDir("shape-corrected-ok-");
        TestSupport.Write(Path.Combine(root, "nativePC", "Probe.arc"), "x");
        var game = Game(root);

        var shape = GameShape.Of(game);

        Assert.Equal(1, shape.ModCount);
        Assert.True(Assert.Single(shape.DeclaredLocations).Exists);
        Assert.False(shape.NeedsAttention);
        Assert.False(GameShape.NeedsAttentionFor(Scanner.GameContext(game), shape.ModCount));
    }

    // A corrected primary and a launcher-appended UE4SS folder side by side: the correction must not
    // turn the appended one into a declared entry, nor the declared one into a derived entry.
    [Fact]
    public void A_corrected_location_and_an_appended_one_keep_their_own_kinds()
    {
        FeedSays("nativePC");
        var root = TestSupport.TempDir("shape-corrected-ue4ss-");
        var win64 = Path.Combine(root, "Binaries", "Win64");
        Directory.CreateDirectory(Path.Combine(win64, "ue4ss", "Mods"));
        var game = Game(root);
        GameShapeTests.WriteUe4ssManifest(game.DataDir!, win64);

        var shape = GameShape.Of(game);

        Assert.Equal(2, shape.DeclaredLocations.Count);
        var declared = shape.DeclaredLocations.Single(d => d.Declared);
        Assert.Equal("nativePC", declared.Path);
        Assert.Equal("mods", declared.CorrectedFrom);

        var derived = shape.DeclaredLocations.Single(d => !d.Declared);
        Assert.Equal("ue4ss-mods", derived.Name);
        Assert.Equal(derived.Absolute, derived.Path);
        Assert.Null(derived.CorrectedFrom);
    }
}
