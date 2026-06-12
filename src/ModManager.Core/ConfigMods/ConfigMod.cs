namespace ModManager.Core.ConfigMods;

/// <summary>
/// Detection for UE config-tweak mods — mods that are a bare known config file (Engine.ini etc.)
/// merged into the game's Saved/Config tree, not a pak. Known-filename match only: a random .ini
/// is never a config mod, and a payload that contains pak/mod files is a pak mod even if it
/// bundles a config (config-only is the trigger; we never hijack normal mod zips).
/// </summary>
public static class ConfigMod
{
    /// <summary>UE config files a mod may legitimately target. Basename match, case-insensitive.</summary>
    public static readonly string[] KnownConfigFiles =
        { "Engine.ini", "Scalability.ini", "Input.ini", "GameUserSettings.ini", "Game.ini" };

    /// <summary>True when the name's basename matches a known UE config file (case-insensitive; subpaths fine).</summary>
    public static bool IsConfigFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var baseName = Path.GetFileName(fileName.Replace('\\', '/'));
        return KnownConfigFiles.Any(k => string.Equals(k, baseName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the payload's recognized entries are all known config files and none are
    /// pak/mod files. Junk (readme, images) is ignored; an empty payload is not a config mod.</summary>
    public static bool IsConfigOnlyPayload(IEnumerable<string> names, IEnumerable<string>? pakExts)
    {
        var exts = (pakExts ?? Enumerable.Empty<string>()).Select(e => e.ToLowerInvariant()).ToHashSet();
        var sawConfig = false;
        foreach (var n in names)
        {
            if (IsConfigFile(n)) { sawConfig = true; continue; }
            var baseName = Path.GetFileName(n.Replace('\\', '/'));
            var dot = baseName.LastIndexOf('.');
            var ext = dot >= 0 ? baseName[(dot + 1)..].ToLowerInvariant() : "";
            if (exts.Contains(ext)) return false; // pak/mod file present -> pak mod wins
        }
        return sawConfig;
    }
}
