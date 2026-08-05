using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

// The classifier is the safety line: a game file must NEVER be proposed as a mod. These
// fixtures mirror a real UE game folder — engine binaries and shipped paks alongside a
// hand-installed mod, a leftover archive, and an ASI proxy.
public class DiscoverySweepTests
{
    private static DiscoverySweepOptions UeOptions() => new(
        ModPaths: new[] { new DiscoverySweepModPath("Content/Paks/~mods", PaksRoot: false) },
        EngineExtensions: new[] { "pak", "utoc", "ucas" },
        SkipFolders: new[] { "_626mods", "disabled" });

    private static IReadOnlyList<SweptFile> Files(params string[] relativePaths)
        => relativePaths.Select(p => new SweptFile(p, 0)).ToList();

    [Fact]
    public void Game_files_are_never_claimed()
    {
        var files = Files(
            "Binaries/Win64/Game.exe",
            "Engine/Content/Slate/Common.uasset",
            "Content/Paks/Game-WindowsNoEditor.pak",   // shipped pak, NOT in the mod path
            "README.txt");

        Assert.Empty(DiscoverySweep.Classify(files, UeOptions()));
    }

    [Fact]
    public void Engine_shaped_files_in_the_mod_path_are_candidates()
    {
        var files = Files("Content/Paks/~mods/FasterShips_P.pak");

        var found = DiscoverySweep.Classify(files, UeOptions());

        var one = Assert.Single(found);
        Assert.Equal("FasterShips_P.pak", one.FileName);
        Assert.Equal(DiscoveryKind.EngineShaped, one.Kind);
    }

    [Fact]
    public void Signature_files_are_candidates_anywhere()
    {
        var files = Files("dinput8.dll", "mods/Zipliner.asi");

        var found = DiscoverySweep.Classify(files, UeOptions());

        Assert.Equal(2, found.Count);
        Assert.All(found, f => Assert.Equal(DiscoveryKind.Signature, f.Kind));
    }

    [Fact]
    public void Archives_are_candidates_anywhere()
    {
        var files = Files("Downloads/FasterShips10.zip", "old/backup.7z", "notes.rar");

        var found = DiscoverySweep.Classify(files, UeOptions());

        Assert.Equal(3, found.Count);
        Assert.All(found, f => Assert.Equal(DiscoveryKind.Archive, f.Kind));
    }

    [Fact]
    public void Skip_folders_are_not_swept()
    {
        var files = Files(
            "_626mods/anything.pak",
            "disabled/OldMod.asi",
            "Content/Paks/~mods/Real_P.pak");

        var one = Assert.Single(DiscoverySweep.Classify(files, UeOptions()));
        Assert.Equal("Real_P.pak", one.FileName);
    }

    [Fact]
    public void Null_or_empty_mod_path_still_finds_signatures_and_archives()
    {
        var options = new DiscoverySweepOptions(Array.Empty<DiscoverySweepModPath>(), Array.Empty<string>(), Array.Empty<string>());
        var files = Files("dinput8.dll", "Mod.zip", "Content/Paks/Game.pak");

        var found = DiscoverySweep.Classify(files, options);

        Assert.Equal(2, found.Count);   // the shipped pak has no mod path to sit in — not claimed
    }

    // Hardening: the safety line depends on case-insensitive matching and backslash normalization
    // holding everywhere. These pin that behavior so a regression (dropped comparer, dropped
    // Replace('\\','/'), or a substring-based skip check) fails loudly instead of shipping silent.

    [Fact]
    public void Uppercase_signature_file_is_still_classified()
    {
        var files = Files("DINPUT8.DLL");

        var one = Assert.Single(DiscoverySweep.Classify(files, UeOptions()));
        Assert.Equal(DiscoveryKind.Signature, one.Kind);
    }

    [Fact]
    public void Windows_separators_and_mixed_case_extension_still_classify_engine_shaped()
    {
        var files = Files("Content\\Paks\\~mods\\Foo_P.PAK");

        var one = Assert.Single(DiscoverySweep.Classify(files, UeOptions()));
        Assert.Equal(DiscoveryKind.EngineShaped, one.Kind);
        Assert.Equal("Foo_P.PAK", one.FileName);
    }

    [Fact]
    public void Mixed_case_skip_folder_is_still_skipped()
    {
        var files = Files("_626MODS/whatever.pak");

        Assert.Empty(DiscoverySweep.Classify(files, UeOptions()));
    }

    [Fact]
    public void Folder_that_only_contains_a_skip_word_is_not_skipped()
    {
        var options = new DiscoverySweepOptions(
            ModPaths: new[] { new DiscoverySweepModPath("mymods", PaksRoot: false) },
            EngineExtensions: new[] { "pak" },
            SkipFolders: new[] { "mods" });
        var files = Files("mymods/Real_P.pak");

        var one = Assert.Single(DiscoverySweep.Classify(files, options));
        Assert.Equal("Real_P.pak", one.FileName);
    }

    // IMPORTANT 2 (final review): a UE4SS game can have BOTH ~mods and LogicMods populated at
    // once (ModLocator.Detect persists every existing candidate folder, not just the first).
    // Hand-installed Blueprint mods sitting only in LogicMods must not stay invisible.
    [Fact]
    public void Every_configured_mod_path_is_swept_not_just_the_first()
    {
        var options = new DiscoverySweepOptions(
            ModPaths: new[]
            {
                new DiscoverySweepModPath("Content/Paks/~mods", PaksRoot: false),
                new DiscoverySweepModPath("Content/Paks/LogicMods", PaksRoot: false),
            },
            EngineExtensions: new[] { "pak" },
            SkipFolders: Array.Empty<string>());
        var files = Files(
            "Content/Paks/~mods/FasterShips_P.pak",
            "Content/Paks/LogicMods/BP_CoolMod_P.pak");

        var found = DiscoverySweep.Classify(files, options);

        Assert.Equal(2, found.Count);
        Assert.Contains(found, f => f.FileName == "FasterShips_P.pak");
        Assert.Contains(found, f => f.FileName == "BP_CoolMod_P.pak");
    }

    // CRITICAL (final review): a loader-less UE-pak game (Witchfire, no UE4SS) has NO dedicated
    // mod folder — the mod location IS Content/Paks itself (ModLocation.Form == "paks-root",
    // ModLocator.cs:41-51 / ModLocations.cs:75-76). Every pakchunk*-Windows*.pak the game ships
    // sits in that same folder. The classifier must never claim one, on name convention OR size —
    // this is the regression the original test suite's fixture (base pak placed OUTSIDE the mod
    // path) accidentally hid.
    private static DiscoverySweepOptions PaksRootOptions() => new(
        ModPaths: new[] { new DiscoverySweepModPath("Content/Paks", PaksRoot: true) },
        EngineExtensions: new[] { "pak", "utoc", "ucas" },
        SkipFolders: Array.Empty<string>());

    [Fact]
    public void Base_game_pak_inside_a_paksroot_mod_path_is_never_claimed_by_name()
    {
        var files = new[]
        {
            new SweptFile("Content/Paks/pakchunk0-Windows.pak", 50L * 1024 * 1024),   // shipping-name convention
            new SweptFile("Content/Paks/FasterShips_P.pak", 2L * 1024 * 1024),        // a real mod, non-shipping name
        };

        var found = DiscoverySweep.Classify(files, PaksRootOptions());

        var one = Assert.Single(found);
        Assert.Equal("FasterShips_P.pak", one.FileName);
    }

    [Fact]
    public void Oversized_pak_inside_a_paksroot_mod_path_is_never_claimed_even_with_a_modlike_name()
    {
        // The size ceiling is the SECOND signal (PakClassifier.IsBaseGamePak: name OR size) — a
        // base chunk renamed away from the shipping convention (non-standard project layout) is
        // still caught, so the guard doesn't rely on naming alone.
        var files = new[]
        {
            new SweptFile("Content/Paks/CoolMod_P.pak", PakClassifier.ModSizeCeilingBytes + 1),
        };

        Assert.Empty(DiscoverySweep.Classify(files, PaksRootOptions()));
    }

    [Fact]
    public void Small_non_shipping_named_pak_inside_a_paksroot_mod_path_is_still_a_candidate()
    {
        // The guard must not overreach: a genuine small mod pak in the paks-root folder is still
        // claimed — only base-game paks are excluded.
        var files = new[] { new SweptFile("Content/Paks/CoolMod_P.pak", 1024 * 1024) };

        var one = Assert.Single(DiscoverySweep.Classify(files, PaksRootOptions()));
        Assert.Equal(DiscoveryKind.EngineShaped, one.Kind);
    }

    [Fact]
    public void Base_game_pak_outside_a_paksroot_path_is_still_never_claimed()
    {
        // Non-paks-root form (a dedicated ~mods folder): base-game paks never sit there at all, so
        // this is really just re-confirming the existing modPath-prefix boundary still holds.
        var files = Files("Content/Paks/pakchunk0-Windows.pak"); // outside "Content/Paks/~mods"

        Assert.Empty(DiscoverySweep.Classify(files, UeOptions()));
    }
}
