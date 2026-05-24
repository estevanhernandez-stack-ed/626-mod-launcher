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
}
