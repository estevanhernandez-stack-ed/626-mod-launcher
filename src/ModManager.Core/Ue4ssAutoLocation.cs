using ModManager.Core.Frameworks;

namespace ModManager.Core;

/// <summary>
/// Derives the implicit <c>ue4ss\Mods</c> mod location from a launcher-owned UE4SS install. UE4SS Lua
/// mods live in <c>&lt;install&gt;\ue4ss\Mods</c>, which is NOT one of a game's configured modLocations
/// (those point at the Paks folders). Once the launcher installs UE4SS itself, that Mods folder becomes
/// a real, launcher-managed surface — so the scanner appends it as a folders-form location and every Lua
/// mod there (installed + UE4SS's own built-ins) gets a row + toggle. Pure path math over the manifests.
/// </summary>
public static class Ue4ssAutoLocation
{
    private const string LocationName = "ue4ss-mods";
    private const string LocationLabel = "UE4SS Mods";

    /// <summary>The <c>ue4ss\Mods</c> location implied by an installed UE4SS manifest, or null if UE4SS
    /// isn't installed. The manifest's InstallPath is the framework root (…/Binaries/Win64); Mods sits at
    /// <c>&lt;InstallPath&gt;/ue4ss/Mods</c>.</summary>
    public static ModLocationCtx? For(IReadOnlyList<FrameworkInstallManifest> installedFrameworks)
    {
        var ue4ss = installedFrameworks?.FirstOrDefault(m =>
            string.Equals(m.FrameworkId, "ue4ss", StringComparison.OrdinalIgnoreCase));
        if (ue4ss is null || string.IsNullOrEmpty(ue4ss.InstallPath)) return null;

        var modsAbs = Path.Combine(ue4ss.InstallPath, "ue4ss", "Mods");
        return new ModLocationCtx(LocationName, LocationLabel, modsAbs, Array.Empty<string>(), Primary: false)
        {
            Form = "folders",
        };
    }

    /// <summary>True iff a ue4ss\Mods location should be appended — i.e. UE4SS is installed AND no existing
    /// configured location already points at that same folder (case-insensitive). Guards against a game
    /// that explicitly configures ue4ss\Mods getting a duplicate.</summary>
    public static bool ShouldAppend(
        IReadOnlyList<FrameworkInstallManifest> installedFrameworks,
        IReadOnlyList<string> existingLocationAbsPaths)
    {
        var loc = For(installedFrameworks);
        if (loc is null) return false;
        var target = Path.GetFullPath(loc.Abs);
        return !(existingLocationAbsPaths ?? Array.Empty<string>())
            .Any(p => string.Equals(Path.GetFullPath(p), target, StringComparison.OrdinalIgnoreCase));
    }
}
