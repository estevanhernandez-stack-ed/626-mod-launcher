namespace ModManager.Core;

/// <summary>Pure lookup of an installed game by store app id. Used to auto-fill the game folder when
/// a curated quick-pick is chosen and the user already has the game installed.</summary>
public static class InstalledGameMatch
{
    public static InstalledGame? ByAppId(IReadOnlyList<InstalledGame> games, string? appId)
        => string.IsNullOrEmpty(appId) ? null : games.FirstOrDefault(g => g.AppId == appId);
}
