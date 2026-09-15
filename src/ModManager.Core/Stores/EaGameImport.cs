using ModManager.Core.Manifest;

namespace ModManager.Core.Stores;

/// <summary>
/// Turns an EA app install into a registration — only when the manifest knows the game.
///
/// <para><b>Curated only, on purpose.</b> Ban risk resolves by manifest id (and a compiled floor for the
/// two EA football titles). An EA install registered under a slug of its display name would read no risk,
/// and the EA catalogue is full of anti-cheat games. So the match is on <c>Stores.EaContentId</c> and
/// nothing else — never the name. Growing EA coverage is a data PR.</para>
///
/// <para><b>Facts, not policy.</b> The plan records what the game is (engine <c>frostbite</c>, its content
/// id, where it lives) and never that it has "no mods": a later mod lane is a preset or manifest change
/// that reaches the registered game without a migration.</para>
/// </summary>
public static class EaGameImport
{
    public static string LaunchUrlFor(string contentId) => "origin2://game/launch/?offerIds=" + contentId;

    public static GameManifestEntry? Match(InstalledGame install, IEnumerable<GameManifestEntry> manifestGames)
        => install.StoreKind == EaInstallScan.StoreKind && !string.IsNullOrWhiteSpace(install.AppId)
            ? manifestGames.FirstOrDefault(g => string.Equals(g.Stores.EaContentId, install.AppId, StringComparison.Ordinal))
            : null;

    public static GameInput? Plan(InstalledGame install, IEnumerable<GameManifestEntry> manifestGames, string localDataRoot)
    {
        if (Match(install, manifestGames) is not { } game) return null;
        return new GameInput
        {
            Id = game.Id,
            Name = game.Name,
            Engine = "frostbite",
            GameRoot = install.InstallDir,
            EaContentId = install.AppId,
            LaunchUrl = LaunchUrlFor(install.AppId),
            // Beside the game root is C:\Program Files\EA Games\_626mods, which an unelevated launcher
            // cannot write. The explicit DataDir override is honoured everywhere.
            DataDir = Path.Combine(localDataRoot, "626mods", game.Id),
            // SteamAppId deliberately unset: it would route Play through steam://.
        };
    }
}
