namespace ModManager.Core;

/// <summary>
/// Find-by-name predicate for the mod list (vibe-glow F-015). Case-insensitive contains
/// over display name and author; a blank query matches everything. Pure — the App binds
/// this to the MODS-bar filter box.
/// </summary>
public static class ModSearch
{
    public static bool Matches(string? name, string? author, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim();
        return Contains(name, q) || Contains(author, q);
    }

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
