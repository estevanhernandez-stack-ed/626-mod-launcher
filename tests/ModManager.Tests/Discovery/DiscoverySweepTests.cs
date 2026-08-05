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
}
