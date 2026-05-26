using ModManager.Core;

namespace ModManager.Tests;

public class DirectInjectMatchSignaturesTests
{
    [Fact]
    public void Recognizes_Seamless_Co_op_archive()
    {
        // A representative Seamless Co-op archive layout: ersc.dll + ersc_settings.ini at root + a
        // seamlesscoop folder for assets + the launcher exe.
        var entries = new[]
        {
            "ersc.dll",
            "ersc_settings.ini",
            "launch_elden_ring_seamlesscoop.exe",
            "seamlesscoop/.gitkeep",
            "readme.md",
        };
        var matches = DirectInject.MatchSignaturesInZip(entries);
        Assert.Contains("Seamless Co-op", matches);
    }

    [Fact]
    public void Recognizes_ReShade_archive_by_files_and_dir()
    {
        var entries = new[]
        {
            "reshade.ini",
            "reshadepreset.ini",
            "reshade-shaders/Shaders/CRT.fx",
            "reshade-shaders/Textures/lut.png",
        };
        var matches = DirectInject.MatchSignaturesInZip(entries);
        Assert.Contains("ReShade", matches);
    }

    [Fact]
    public void Recognizes_Modded_regulation_bin_archive()
    {
        var matches = DirectInject.MatchSignaturesInZip(new[] { "regulation.bin", "readme.txt" });
        Assert.Contains("Modded regulation.bin", matches);
    }

    [Fact]
    public void Matches_FileContains_pattern_for_ultrawide_filenames_that_vary()
    {
        // Ultrawide mods ship as ULTRAWIDESCREENFIX.DLL, EldenRing_Ultrawide.dll, WidescreenFix.dll...
        // Verify the FileContains fragment hits regardless of the exact name.
        var matches = DirectInject.MatchSignaturesInZip(new[] { "EldenRing_Ultrawide.dll" });
        Assert.Contains("Ultrawide / Widescreen Fix", matches);
    }

    [Fact]
    public void Returns_distinct_results_when_multiple_signatures_match()
    {
        var entries = new[]
        {
            "ersc.dll", "ersc_settings.ini",  // Seamless Co-op
            "regulation.bin",                  // Modded regulation.bin
        };
        var matches = DirectInject.MatchSignaturesInZip(entries);
        Assert.Contains("Seamless Co-op", matches);
        Assert.Contains("Modded regulation.bin", matches);
        Assert.Equal(matches.Count, matches.Distinct().Count());
    }

    [Fact]
    public void Empty_or_unrecognized_archive_returns_empty()
    {
        Assert.Empty(DirectInject.MatchSignaturesInZip(Array.Empty<string>()));
        Assert.Empty(DirectInject.MatchSignaturesInZip(new[] { "random.txt", "screenshot.png" }));
    }

    [Fact]
    public void Case_insensitive_filename_match()
    {
        // Some uploaders ship UPPERCASE filenames. The on-disk recognizer is case-insensitive on
        // Windows; the archive-name recognizer must be too.
        var matches = DirectInject.MatchSignaturesInZip(new[] { "ERSC.DLL", "ERSC_SETTINGS.INI" });
        Assert.Contains("Seamless Co-op", matches);
    }
}
