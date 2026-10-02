using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// The ONE table of folders the launcher knows hold nothing but mods. Safe Clear's "Return to vanilla" may
/// move everything left in such a folder into the restore point. Anywhere else it can't tell the game's
/// own files from mods, so it leaves the folder alone and says so. An ALLOWLIST on purpose: a denylist
/// swept Skyrim's <c>Data</c> (Skyrim.esm, the base .bsa), Total War's <c>data.pack</c>, Bannerlord's
/// <c>Modules\Native</c> and a dozen more base-game folders that the curated feed names as mod paths
/// (review r3, C1).
///
/// <para>A location is mod-only when any of these holds:</para>
/// <list type="bullet">
/// <item>its path relative to the game root matches its engine's mod-only shape (<see cref="Shapes"/>);</item>
/// <item>it is the launcher's own UE4SS auto-location;</item>
/// <item>it is the game's PRIMARY location, at exactly the manifest's <c>modPath</c>, the manifest marks
/// that path <c>modPathModOnly</c>, and the user didn't set the locations by hand.</item>
/// </list>
/// <para>Never, whatever the shape: the game root, a folder outside it, or another tool's folder (callers
/// check ownership). The declared extra trees are mod-only by definition and are not decided here.</para>
/// </summary>
public static class ModOnlyFolders
{
    /// <summary>Per engine, the relative paths (forward slashes, case-insensitive, whole path) that hold
    /// nothing but mods. Each shape has its own test.</summary>
    public static readonly IReadOnlyDictionary<string, Regex> Shapes = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase)
    {
        // A UE loader's mod folders under a project's Content/Paks; never Content/Paks itself (base paks).
        ["ue-pak"] = Shape(@"(?:[^/]+/)?Content/Paks/(?:~mods|LogicMods|Mods)"),
        ["bepinex"] = Shape(@"BepInEx/plugins"),
        ["smapi"] = Shape(@"Mods"),
        ["melonloader"] = Shape(@"Mods"),
        ["minecraft"] = Shape(@"mods"),
        // Mod Engine 2's own mod folder; never DS PTDE's DATA or the Game\ play folder.
        ["fromsoft"] = Shape(@"(?:[^/]+/)*mod"),
    };

    private static Regex Shape(string pattern) => new("^" + pattern + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The name the launcher gives its own appended UE4SS mods location.</summary>
    public const string Ue4ssAutoLocationName = Ue4ssAutoLocation.LocationName;

    /// <summary>True when <paramref name="relPath"/> is a mod-only shape for <paramref name="engine"/>.</summary>
    public static bool IsShape(string? engine, string? relPath)
        => !string.IsNullOrEmpty(engine) && Shapes.TryGetValue(engine, out var re)
           && Normalise(relPath) is { Length: > 0 } rel && re.IsMatch(rel);

    /// <summary>Why <paramref name="loc"/> holds nothing but mods, or null when the launcher can't say so.</summary>
    public static string? WhyModOnly(GameContext c, ModLocationCtx loc)
    {
        var rel = RelativeToRoot(c.GameRoot, loc.Abs);
        if (rel is null) return null;                                           // the root itself, or outside it
        if (string.Equals(loc.Name, Ue4ssAutoLocationName, StringComparison.Ordinal)) return "the launcher's UE4SS mods folder";
        if (IsShape(c.Game.Engine, rel)) return $"the {c.Game.Engine} mod folder";
        var userSet = c.Game.UserSet?.Contains(GameEntry.UserSetModLocations, StringComparer.OrdinalIgnoreCase) == true;
        if (!userSet && loc.Primary && ManifestIdLookup.ConfirmedEntryFor(c.Game) is { ModPathModOnly: true, ModPath: { } mp }
            && string.Equals(Normalise(mp), rel, StringComparison.OrdinalIgnoreCase))
            return "marked mod-only by the game's definition";
        return null;
    }

    /// <summary>The path of <paramref name="abs"/> relative to <paramref name="gameRoot"/>, forward slashes,
    /// or null when it is the game root itself or not under it.</summary>
    public static string? RelativeToRoot(string gameRoot, string abs)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(abs));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            return Normalise(full[(root.Length + 1)..]);
        }
        catch { return null; }
    }

    private static string Normalise(string? p) => (p ?? "").Replace('\\', '/').Trim('/');
}
