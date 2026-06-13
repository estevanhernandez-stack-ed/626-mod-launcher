namespace ManifestMiner;

/// <summary>Facts mined from one MO2 basic_games game_*.py. No engine field exists in MO2;
/// the mod path is <see cref="DataPath"/> (sometimes empty). Steam ids are a list.</summary>
public sealed record Mo2Game(string GameName)
{
    public IReadOnlyList<string> SteamIds { get; init; } = Array.Empty<string>();
    public string? DataPath { get; init; }   // GameDataPath ("" = no separate mod dir; null = absent)
    public string? NexusName { get; init; }  // GameNexusName slug, when present (not the numeric id)
}
