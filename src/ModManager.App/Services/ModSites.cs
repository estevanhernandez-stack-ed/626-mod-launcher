namespace ModManager.App.Services;

/// <summary>A mod site we can send the user to, to find mods for a game.</summary>
public sealed record ModSite(string Key, string Name, string Domain);

/// <summary>
/// "Find mods" targets. We open a site-scoped web search for the game name — neither Nexus nor
/// CurseForge exposes a stable keyword-search URL, and a search reliably lands on the right
/// game's mod page (foolproof for a non-technical user). Add a row here to support a new site.
/// </summary>
public static class ModSites
{
    public static readonly IReadOnlyList<ModSite> All = new[]
    {
        new ModSite("nexus", "Nexus Mods", "nexusmods.com"),
        new ModSite("curseforge", "CurseForge", "curseforge.com"),
    };

    public static string? SearchUrl(string key, string gameName)
    {
        var site = All.FirstOrDefault(s => s.Key == key);
        if (site is null) return null;
        var q = Uri.EscapeDataString($"{gameName} mods site:{site.Domain}");
        return $"https://duckduckgo.com/?q={q}";
    }
}
