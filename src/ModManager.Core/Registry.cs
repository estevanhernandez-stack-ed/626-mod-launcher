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
