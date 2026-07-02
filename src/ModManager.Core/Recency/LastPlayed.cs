namespace ModManager.Core.Recency;

public sealed record LastPlayed(DateTime? LastPlayedUtc, TimeSpan? Playtime, string Source)
{
    public static readonly LastPlayed None = new(null, null, "none");
}

public sealed record GameRecencyKey(string? SteamAppId, string? GameRoot, string? LaunchExe, string Id);

public interface ILastPlayedSource
{
    string Name { get; }
    LastPlayed? ForGame(GameRecencyKey key);
}
