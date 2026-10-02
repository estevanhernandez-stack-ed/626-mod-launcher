using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// Finds a game's save folder so the user never has to hunt for it. Builds candidate paths from
/// the real OS folders (Documents / LocalAppData / AppData) via Core's pure guesser — including
/// Unreal "project name" folders discovered under the game root (UE saves under the internal
/// project name, e.g. R5 for Windrose, not the store name) — and returns the first that exists.
/// </summary>
public static class SaveLocator
{
    /// <summary>
    /// Authoritative-first, for a registered game: the curated hint resolved through every identity the
    /// game carries (<see cref="SaveDirHints.For"/>), so an EA app game with no Steam id still gets its
    /// hand-checked folder. Then Ludusavi's templates (Steam-keyed), then the folder heuristics.
    /// </summary>
    public static Task<string?> DetectAsync(LudusaviService ludusavi, GameEntry game, string? steamUserId = null)
        => DetectAsync(ludusavi, game.GameName, game.Engine, game.GameRoot, game.SteamAppId,
            SaveDirHints.For(game),
            // The signed-in Steam user names a Steam game's save subfolder and nothing else's. Handing it
            // to a game with no Steam id would resolve a <storeUserId> hint to a folder that is not its.
            string.IsNullOrEmpty(game.SteamAppId) ? null : steamUserId,
            // An EA app install lives under Program Files\EA Games, which gets no read beyond
            // installerdata.xml (EA slice one). The heuristic's project-name discovery lists the install
            // folder and <base> resolves into it, so an EA game gets neither.
            listInstallFolder: string.IsNullOrEmpty(game.EaContentId));

    /// <summary>
    /// Authoritative-first: resolve the Ludusavi save templates for the Steam app id, then fall
    /// back to the folder heuristics. Best-effort — anything missing degrades to the heuristic.
    /// Pass <paramref name="steamUserId"/> (64-bit SteamID) so templates that reference
    /// <c>&lt;storeUserId&gt;</c> (Elden Ring, Sekiro, the FromSoft family) actually resolve;
    /// without it those templates return null and the caller falls back to the heuristic which
    /// doesn't know about the Steam-user-id subfolder.
    /// </summary>
    public static Task<string?> DetectAsync(LudusaviService ludusavi, string gameName, string? engine, string? gameRoot, string? steamAppId, string? steamUserId = null)
        => DetectAsync(ludusavi, gameName, engine, gameRoot, steamAppId, SaveDirHints.ByAppId(steamAppId), steamUserId,
            listInstallFolder: true);

    private static async Task<string?> DetectAsync(LudusaviService ludusavi, string gameName, string? engine,
        string? gameRoot, string? steamAppId, string? curatedHint, string? steamUserId, bool listInstallFolder)
    {
        // <base> is the install folder. An EA install gets no read beyond installerdata.xml, so for one
        // the token is left unset and a <base> hint fails to resolve rather than probing inside it.
        var tokens = WindowsTokens(listInstallFolder ? gameRoot : null, steamUserId);

        // The CURATED hint first. Ludusavi lists every path a game touches and we take the first
        // that exists, which is usually right and occasionally precisely wrong - Stellaris resolves
        // to the game's config directory that way, because it is listed first and it does exist.
        // A hand-checked hint beats a first match, and it is the folder saveLayout describes. It needs
        // no Steam id: the caller resolved it from whatever identity the game has.
        if (curatedHint is not null)
        {
            var hinted = LudusaviPaths.Resolve(curatedHint, tokens);
            try { if (hinted is not null && Directory.Exists(hinted)) return hinted; }
            catch { /* fall through to Ludusavi's own list */ }
        }

        // Ludusavi's catalogue is keyed by Steam app id, so this half stays Steam-only.
        if (!string.IsNullOrEmpty(steamAppId))
        {
            foreach (var template in await ludusavi.SaveTemplatesAsync(steamAppId))
            {
                var resolved = LudusaviPaths.Resolve(template, tokens);
                if (resolved is null) continue;
                try { if (Directory.Exists(resolved)) return resolved; } catch { /* skip */ }
            }
        }
        return Detect(gameName, engine, listInstallFolder ? gameRoot : null);
    }

    private static Dictionary<string, string> WindowsTokens(string? gameRoot, string? steamUserId)
    {
        var tokens = new Dictionary<string, string>
        {
            ["home"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["winLocalAppData"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["winAppData"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["winDocuments"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["winProgramData"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ["winPublic"] = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public",
            ["winDir"] = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
            ["osUserName"] = Environment.UserName,
        };
        // Only set storeUserId when we actually have one — leaving the key absent lets the
        // template-resolver's "unknown token = fail" guard reject ER's template cleanly and fall
        // through to the heuristic for users not signed into Steam.
        if (!string.IsNullOrEmpty(steamUserId)) tokens["storeUserId"] = steamUserId;
        // Likewise <base>: with no game root, an empty value would turn "<base>/saves" into a path
        // relative to wherever the launcher happens to be running. Absent, the template fails cleanly.
        if (!string.IsNullOrEmpty(gameRoot)) tokens["base"] = gameRoot;
        return tokens;
    }

    public static string? Detect(string gameName, string? engine, string? gameRoot)
    {
        var roots = new SaveLocations.SaveRoots(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        foreach (var candidate in SaveLocations.Guess(roots, gameName, engine, ProjectNames(gameRoot)))
        {
            try { if (Directory.Exists(candidate)) return candidate; }
            catch { /* skip an unreadable candidate */ }
        }
        return null;
    }

    /// <summary>Folder-derived names: UE project subfolders (those with a Content dir) + the root name.</summary>
    private static IEnumerable<string> ProjectNames(string? gameRoot)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return names;
        try
        {
            foreach (var sub in Directory.GetDirectories(gameRoot))
                if (Directory.Exists(Path.Combine(sub, "Content")))
                    names.Add(Path.GetFileName(sub));
            names.Add(Path.GetFileName(gameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        }
        catch { /* unreadable game folder */ }
        return names;
    }
}
