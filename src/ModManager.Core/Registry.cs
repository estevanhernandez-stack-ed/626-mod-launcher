namespace ModManager.Core;

/// <summary>
/// Pure registry operations (no IO). Active-game selection falls back to the first game;
/// upsert adds or replaces by id and seeds the active selection. Mirrors registry-core.js.
/// </summary>
public static class Registry
{
    public static GameRegistry EmptyRegistry() => new();

    public static GameEntry? GetActiveGame(GameRegistry? reg)
    {
        if (reg?.Games is null) return null;
        return reg.Games.FirstOrDefault(x => x.Id == reg.ActiveGameId) ?? reg.Games.FirstOrDefault();
    }

    public static GameRegistry UpsertGame(GameRegistry reg, GameEntry game)
    {
        var r = new GameRegistry
        {
            Version = reg.Version,
            ActiveGameId = reg.ActiveGameId,
            Games = new List<GameEntry>(reg.Games),
        };
        var i = r.Games.FindIndex(x => x.Id == game.Id);
        if (i >= 0) r.Games[i] = game;
        else r.Games.Add(game);
        if (string.IsNullOrEmpty(r.ActiveGameId)) r.ActiveGameId = game.Id;
        return r;
    }

    public static GameRegistry SetActiveGame(GameRegistry reg, string id)
    {
        if (!reg.Games.Any(x => x.Id == id)) return reg;
        return new GameRegistry { Version = reg.Version, ActiveGameId = id, Games = new List<GameEntry>(reg.Games) };
    }

    /// <summary>
    /// The already-registered entry for an install, or null when this is genuinely a new game.
    ///
    /// <para>Exists because adding a game the launcher already knows about used to create a duplicate:
    /// <see cref="EnginePresets.UniqueId"/> suffixes on collision, so three add attempts on one clean box
    /// produced <c>windrose</c>, <c>windrose-2</c>, <c>windrose-3</c> — and each new registration silently
    /// stole the active game out from under the one the user had already set up. Every add lane (Steam
    /// quick-add, batch, the manual form, the library's not-added-yet list) funnels through the same
    /// add call, so one guard in front of it covers all four.</para>
    ///
    /// <para>The game root is the primary key because it is what actually identifies an install. The id is
    /// derived from the game's <em>name</em>, so the same folder typed in under a different name looks
    /// brand new to an id comparison — exactly the case that produced the duplicates. Roots are compared
    /// normalised (full path, trailing separators trimmed, case-insensitive) so <c>C:\Games\Windrose\</c>
    /// and <c>c:\games\windrose</c> are one install. The Steam app id is only a fallback, for the same
    /// game installed to a moved or re-pointed library folder.</para>
    ///
    /// <para>A blank or null incoming root matches nothing — including a stored entry whose root also
    /// happens to be blank. Two unknown roots are not evidence of the same install. Likewise an empty
    /// incoming Steam id never matches an empty stored one.</para>
    /// </summary>
    public static GameEntry? FindRegistered(GameRegistry reg, string? gameRoot, string? steamAppId)
    {
        // Same normalisation the data-dir move uses — one spelling of "is this the same folder",
        // malformed-path-tolerant (it falls back to a trimmed compare rather than throwing).
        var root = DataDirMove.Norm(gameRoot);
        if (root.Length > 0)
        {
            var byRoot = reg.Games.FirstOrDefault(g =>
                DataDirMove.Norm(g.GameRoot) is { Length: > 0 } stored
                && string.Equals(stored, root, StringComparison.OrdinalIgnoreCase));
            if (byRoot is not null) return byRoot;
        }

        var appId = steamAppId?.Trim();
        if (string.IsNullOrEmpty(appId)) return null;

        return reg.Games.FirstOrDefault(g =>
            !string.IsNullOrWhiteSpace(g.SteamAppId)
            && string.Equals(g.SteamAppId.Trim(), appId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Drop a game from the registry. If it was active, the active selection moves to the first
    /// remaining game (or null when none are left). Removing an unknown id is a no-op.
    /// Does not touch the game's files or launcher data — re-adding restores it.
    /// </summary>
    public static GameRegistry RemoveGame(GameRegistry reg, string id)
    {
        var games = reg.Games.Where(g => g.Id != id).ToList();
        var active = reg.ActiveGameId == id ? (games.Count > 0 ? games[0].Id : null) : reg.ActiveGameId;
        return new GameRegistry { Version = reg.Version, ActiveGameId = active, Games = games };
    }
}
