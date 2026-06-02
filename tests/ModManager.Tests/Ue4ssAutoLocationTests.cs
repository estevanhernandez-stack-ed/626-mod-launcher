using ModManager.Core;
using ModManager.Core.Frameworks;

namespace ModManager.Tests;

// Symptom 2, surfacing half: once the launcher OWNS a UE4SS install, its ue4ss\Mods folder should show
// up as a toggleable mod location automatically — even though it's NOT one of the game's configured
// modLocations (Windrose only configures the two Paks folders). Ue4ssAutoLocation derives the extra
// folders-form location from the installed-framework manifest so the scanner can append it.
public class Ue4ssAutoLocationTests
{
    private static FrameworkInstallManifest Ue4ssManifestAt(string installPath) =>
        new("ue4ss", "UE4SS", "RE-UE4SS team", installPath, new[] { "ue4ss/UE4SS.dll" }, DateTime.UtcNow, null);

    [Fact]
    public void Derives_a_ue4ss_Mods_location_from_an_installed_ue4ss_manifest()
    {
        var binWin64 = Path.Combine("C:", "game", "R5", "Binaries", "Win64");
        var loc = Ue4ssAutoLocation.For(new[] { Ue4ssManifestAt(binWin64) });

        Assert.NotNull(loc);
        Assert.Equal(Path.Combine(binWin64, "ue4ss", "Mods"), loc!.Abs);
        Assert.Equal("folders", loc.Form);          // UE4SS Lua mods are one-folder-per-mod
        Assert.False(loc.Primary);                  // never the primary location
    }

    [Fact]
    public void Returns_null_when_no_ue4ss_is_installed()
    {
        Assert.Null(Ue4ssAutoLocation.For(Array.Empty<FrameworkInstallManifest>()));
        // A different framework installed shouldn't conjure a ue4ss Mods location.
        var elm = new FrameworkInstallManifest("elden-mod-loader", "ELM", "x",
            Path.Combine("C:", "g", "Game"), new[] { "dinput8.dll" }, DateTime.UtcNow, null);
        Assert.Null(Ue4ssAutoLocation.For(new[] { elm }));
    }

    [Fact]
    public void Does_not_duplicate_a_ue4ss_Mods_location_already_configured()
    {
        // If the game already configures ue4ss\Mods as a modLocation, don't add a second one.
        var binWin64 = Path.Combine("C:", "game", "R5", "Binaries", "Win64");
        var existingAbs = Path.Combine(binWin64, "ue4ss", "Mods");
        var alreadyHas = Ue4ssAutoLocation.ShouldAppend(
            new[] { Ue4ssManifestAt(binWin64) }, existingLocationAbsPaths: new[] { existingAbs });
        Assert.False(alreadyHas);

        var notYet = Ue4ssAutoLocation.ShouldAppend(
            new[] { Ue4ssManifestAt(binWin64) }, existingLocationAbsPaths: new[] { Path.Combine(binWin64, "other") });
        Assert.True(notYet);
    }
}
