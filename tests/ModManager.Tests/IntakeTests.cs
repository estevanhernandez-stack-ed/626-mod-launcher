using ModManager.Core;

namespace ModManager.Tests;

// Ports intake-core.test.js — pure drop-file classification (zip / mod / skip).
public class IntakeTests
{
    [Fact]
    public void Classify_mod_file()
        => Assert.Equal("mod", Intake.ClassifyDrop("a/b/Mod_P.pak", new[] { "pak", "ucas" }));

    [Fact]
    public void Classify_zip_case_insensitive()
        => Assert.Equal("zip", Intake.ClassifyDrop("x/pack.ZIP", new[] { "pak" }));

    [Fact]
    public void Classify_skip()
        => Assert.Equal("skip", Intake.ClassifyDrop("x/readme.txt", new[] { "pak" }));

    [Fact]
    public void Classify_by_configured_extensions()
    {
        Assert.Equal("mod", Intake.ClassifyDrop("mods/cool.jar", new[] { "jar" }));
        Assert.Equal("skip", Intake.ClassifyDrop("mods/cool.dll", new[] { "jar" }));
    }

    // Multi-format archive support: .7z and .rar route through the same archive path as .zip.
    [Fact]
    public void Classify_7z_as_archive()
        => Assert.Equal("zip", Intake.ClassifyDrop("x/pack.7z", new[] { "pak" }));

    [Fact]
    public void Classify_rar_as_archive()
        => Assert.Equal("zip", Intake.ClassifyDrop("x/pack.rar", new[] { "pak" }));

    [Fact]
    public void Classify_archive_extensions_case_insensitive()
    {
        Assert.Equal("zip", Intake.ClassifyDrop("x/Pack.7Z", new[] { "pak" }));
        Assert.Equal("zip", Intake.ClassifyDrop("x/Pack.RAR", new[] { "pak" }));
    }
}
