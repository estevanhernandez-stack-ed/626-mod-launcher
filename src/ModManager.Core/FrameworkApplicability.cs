namespace ModManager.Core;

/// <summary>
/// Per-mod framework-dependency rules — which mods actually need a given framework, so the row chip
/// is mod-aware rather than engine-wide. Today: UE4SS. A ue-pak game's UE4SS dependency applies only
/// to Lua/script mods and Blueprint LogicMods paks; a plain content pak (in ~mods or a paks-root
/// location) loads with no framework. Pure — decides from the Mod + its resolved location path.
/// </summary>
public static class FrameworkApplicability
{
    /// <summary>True when <paramref name="mod"/> needs UE4SS: it's a Lua/script mod (driven through the
    /// UE4SS manifest, <c>Loader == "ue4ss"</c>) OR a Blueprint pak in a LogicMods location (UE4SS's
    /// BPModLoader mounts that folder). A plain content pak needs nothing. <paramref name="locationPath"/>
    /// is the row's mod-location path (relative or absolute); the LogicMods check is case- and
    /// separator-insensitive.</summary>
    public static bool ModNeedsUe4ss(Mod mod, string locationPath)
    {
        if (mod is not null && string.Equals(mod.Loader, "ue4ss", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrEmpty(locationPath)) return false;
        var norm = locationPath.Replace('\\', '/').TrimEnd('/');
        var leaf = norm.Length == 0 ? "" : norm[(norm.LastIndexOf('/') + 1)..];
        return string.Equals(leaf, "LogicMods", StringComparison.OrdinalIgnoreCase);
    }
}
