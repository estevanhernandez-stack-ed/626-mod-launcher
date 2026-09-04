namespace ModManager.Core.Nexus;

/// <summary>
/// Where a mod lives on Nexus. One definition, because the shape was already written out inline in
/// SaveBundle and a second copy is a second thing to get wrong when Nexus changes a path.
/// </summary>
public static class NexusModPage
{
    /// <summary>The mod's page, or null when we cannot name the mod. Both parts are required: a
    /// domain with no id is the game's whole mod list, which is not what a row naming one mod
    /// promised. The domain is also held to the shape a Nexus slug actually takes — ASCII
    /// letters/digits/hyphen only — because every caller interpolates it straight into a URL a user
    /// clicks, and at least one of them (a save bundle) got it from data that arrived from somewhere
    /// else. One check here instead of each caller carrying its own copy.</summary>
    public static string? Url(string? nexusDomain, int? modId)
    {
        if (string.IsNullOrWhiteSpace(nexusDomain) || modId is not > 0) return null;
        foreach (var c in nexusDomain)
            if (!char.IsAsciiLetterOrDigit(c) && c != '-') return null;
        return $"https://www.nexusmods.com/{nexusDomain}/mods/{modId}";
    }
}
