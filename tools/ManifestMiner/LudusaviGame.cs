namespace ManifestMiner;

/// <summary>The facts we extract from one Ludusavi manifest entry. Ludusavi is save-data oriented:
/// it gives us the game name, its Steam app id, install-dir name(s), and save/config path strings —
/// no engine and no mod folder (those come from Vortex/MO2 in a later slice).</summary>
public sealed record LudusaviGame(string Name)
{
    public string? SteamAppId { get; init; }
    public IReadOnlyList<string> InstallDirs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SavePaths { get; init; } = Array.Empty<string>();
}
