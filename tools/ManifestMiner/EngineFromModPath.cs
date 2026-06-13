namespace ManifestMiner;

/// <summary>Conservative reverse-map: a mod path -> the single engine it unambiguously implies, else
/// null. Wrong engine is worse than null (the launcher folder-detects at runtime), so anything that
/// maps to more than one engine ("Mods", "mods") or is unknown returns null.</summary>
public static class EngineFromModPath
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Data"] = "bethesda",
            ["BepInEx/plugins"] = "bepinex",
            ["addons"] = "source",
            ["mod"] = "fromsoft",
        };

    public static string? Infer(string? modPath)
    {
        if (string.IsNullOrWhiteSpace(modPath)) return null;
        var normalized = modPath.Replace('\\', '/').Trim();
        return Map.TryGetValue(normalized, out var engine) ? engine : null;
    }
}
