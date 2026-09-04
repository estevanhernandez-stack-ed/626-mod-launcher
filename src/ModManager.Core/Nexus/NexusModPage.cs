namespace ModManager.Core.Nexus;

/// <summary>
/// Where a mod lives on Nexus. One definition, because the shape was already written out inline in
/// SaveBundle and a second copy is a second thing to get wrong when Nexus changes a path.
/// </summary>
public static class NexusModPage
{
    /// <summary>The mod's page, or null when we cannot name the mod. Both parts are required: a
    /// domain with no id is the game's whole mod list, which is not what a row naming one mod
    /// promised.</summary>
    public static string? Url(string? nexusDomain, int? modId)
        => string.IsNullOrWhiteSpace(nexusDomain) || modId is not > 0
            ? null
            : $"https://www.nexusmods.com/{nexusDomain}/mods/{modId}";
}
