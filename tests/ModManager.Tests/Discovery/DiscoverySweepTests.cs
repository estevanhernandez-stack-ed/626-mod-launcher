using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

// The classifier is the safety line: a game file must NEVER be proposed as a mod. These
// fixtures mirror a real UE game folder — engine binaries and shipped paks alongside a
// hand-installed mod, a leftover archive, and an ASI proxy.
public class DiscoverySweepTests
{
    private static DiscoverySweepOptions UeOptions() => new(
        ModPath: "Content/Paks/~mods",
        EngineExtensions: new[] { "pak", "utoc", "ucas" },
        SkipFolders: new[] { "_626mods", "disabled" });

    [Fact]
    public void Game_files_are_never_claimed()
    {
        var listing = new[]
        {
            "Binaries/Win64/Game.exe",
            "Engine/Content/Slate/Common.uasset",
            "Content/Paks/Game-WindowsNoEditor.pak",   // shipped pak, NOT in the mod path
            "README.txt",
        };

        Assert.Empty(DiscoverySweep.Classify(listing, UeOptions()));
    }

    [Fact]
    public void Engine_shaped_files_in_the_mod_path_are_candidates()
    {
        var listing = new[] { "Content/Paks/~mods/FasterShips_P.pak" };

        var found = DiscoverySweep.Classify(listing, UeOptions());

        var one = Assert.Single(found);
        Assert.Equal("FasterShips_P.pak", one.FileName);
        Assert.Equal(DiscoveryKind.EngineShaped, one.Kind);
    }

    [Fact]
    public void Signature_files_are_candidates_anywhere()
    {
        var listing = new[] { "dinput8.dll", "mods/Zipliner.asi" };

        var found = DiscoverySweep.Classify(listing, UeOptions());

        Assert.Equal(2, found.Count);
        Assert.All(found, f => Assert.Equal(DiscoveryKind.Signature, f.Kind));
    }

    [Fact]
    public void Archives_are_candidates_anywhere()
    {
        var listing = new[] { "Downloads/FasterShips10.zip", "old/backup.7z", "notes.rar" };

        var found = DiscoverySweep.Classify(listing, UeOptions());

        Assert.Equal(3, found.Count);
        Assert.All(found, f => Assert.Equal(DiscoveryKind.Archive, f.Kind));
    }

    [Fact]
    public void Skip_folders_are_not_swept()
    {
        var listing = new[]
        {
            "_626mods/anything.pak",
            "disabled/OldMod.asi",
            "Content/Paks/~mods/Real_P.pak",
        };

        var one = Assert.Single(DiscoverySweep.Classify(listing, UeOptions()));
        Assert.Equal("Real_P.pak", one.FileName);
    }

    [Fact]
    public void Null_or_empty_mod_path_still_finds_signatures_and_archives()
    {
        var options = new DiscoverySweepOptions(null, Array.Empty<string>(), Array.Empty<string>());
        var listing = new[] { "dinput8.dll", "Mod.zip", "Content/Paks/Game.pak" };

        var found = DiscoverySweep.Classify(listing, options);

        Assert.Equal(2, found.Count);   // the shipped pak has no mod path to sit in — not claimed
    }

    // Hardening: the safety line depends on case-insensitive matching and backslash normalization
    // holding everywhere. These pin that behavior so a regression (dropped comparer, dropped
    // Replace('\\','/'), or a substring-based skip check) fails loudly instead of shipping silent.

    [Fact]
    public void Uppercase_signature_file_is_still_classified()
    {
        var listing = new[] { "DINPUT8.DLL" };

        var one = Assert.Single(DiscoverySweep.Classify(listing, UeOptions()));
        Assert.Equal(DiscoveryKind.Signature, one.Kind);
    }

    [Fact]
    public void Windows_separators_and_mixed_case_extension_still_classify_engine_shaped()
    {
        var listing = new[] { "Content\\Paks\\~mods\\Foo_P.PAK" };

        var one = Assert.Single(DiscoverySweep.Classify(listing, UeOptions()));
        Assert.Equal(DiscoveryKind.EngineShaped, one.Kind);
        Assert.Equal("Foo_P.PAK", one.FileName);
    }

    [Fact]
    public void Mixed_case_skip_folder_is_still_skipped()
    {
        var listing = new[] { "_626MODS/whatever.pak" };

        Assert.Empty(DiscoverySweep.Classify(listing, UeOptions()));
    }

    [Fact]
    public void Folder_that_only_contains_a_skip_word_is_not_skipped()
    {
        var options = new DiscoverySweepOptions(
            ModPath: "mymods",
            EngineExtensions: new[] { "pak" },
            SkipFolders: new[] { "mods" });
        var listing = new[] { "mymods/Real_P.pak" };

        var one = Assert.Single(DiscoverySweep.Classify(listing, options));
        Assert.Equal("Real_P.pak", one.FileName);
    }
}
