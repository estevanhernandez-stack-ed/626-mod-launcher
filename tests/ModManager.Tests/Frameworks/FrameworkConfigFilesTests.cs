using ModManager.Core.Frameworks;

namespace ModManager.Tests.Frameworks;

// #110 follow-up: the "how to use" toast tells the user to edit UE4SS-settings.ini, so they need a way
// to open it. FrameworkUsage.ConfigFiles finds a framework's editable .ini config files from its install
// manifest (truthful to what was installed; survives a layout change; generic across frameworks).
public class FrameworkConfigFilesTests
{
    private static FrameworkInstallManifest Manifest(string installPath, params string[] files) =>
        new("ue4ss", "UE4SS", "RE-UE4SS team", installPath, files, DateTime.UtcNow, null);

    [Fact]
    public void ConfigFiles_returns_absolute_paths_to_ini_files_in_the_manifest()
    {
        var install = Path.Combine("C:", "game", "R5", "Binaries", "Win64");
        var m = Manifest(install,
            "dwmapi.dll",
            "ue4ss/UE4SS.dll",
            "ue4ss/UE4SS-settings.ini",   // the one we want
            "ue4ss/Mods/mods.txt");

        var inis = FrameworkUsage.ConfigFiles(m);

        var expected = Path.Combine(install, "ue4ss", "UE4SS-settings.ini");
        Assert.Contains(expected, inis);
        Assert.DoesNotContain(inis, p => p.EndsWith(".dll"));   // only .ini files
        Assert.DoesNotContain(inis, p => p.EndsWith("mods.txt"));
    }

    [Fact]
    public void ConfigFiles_is_empty_when_no_ini_was_installed()
    {
        var m = Manifest(Path.Combine("C:", "game", "Binaries"), "loader.dll", "data.bin");
        Assert.Empty(FrameworkUsage.ConfigFiles(m));
    }

    [Fact]
    public void ConfigFiles_handles_backslash_relative_paths_in_the_manifest()
    {
        // Manifests store forward-slash rel paths, but be defensive about a backslash variant.
        var install = Path.Combine("C:", "game", "bin");
        var m = Manifest(install, @"ue4ss\UE4SS-settings.ini");

        var inis = FrameworkUsage.ConfigFiles(m);

        Assert.Contains(Path.Combine(install, "ue4ss", "UE4SS-settings.ini"), inis);
    }
}
