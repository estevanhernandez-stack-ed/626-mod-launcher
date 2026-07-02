namespace ModManager.Core.Recency;

public static class RecencyLadder
{
    public static LastPlayed Merge(GameRecencyKey key, IReadOnlyList<ILastPlayedSource> sources)
    {
        DateTime? lastPlayed = null; TimeSpan? playtime = null; string src = "none";
        foreach (var s in sources)
        {
            LastPlayed? r;
            try { r = s.ForGame(key); } catch { continue; } // a bad source never breaks the ladder
            if (r is null) continue;
            if (lastPlayed is null && r.LastPlayedUtc is not null) { lastPlayed = r.LastPlayedUtc; src = r.Source; }
            if (playtime is null && r.Playtime is not null) playtime = r.Playtime;
            if (lastPlayed is not null && playtime is not null) break;
        }
        return lastPlayed is null && playtime is null ? LastPlayed.None : new LastPlayed(lastPlayed, playtime, src);
    }
}
