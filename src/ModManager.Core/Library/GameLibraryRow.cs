namespace ModManager.Core.Library;

using ModManager.Core.Recency;

/// <summary>How well 626 knows this game's engine — drives what mod tooling can be offered.</summary>
public enum EngineTier
{
    EngineCurated,
    NexusOnly,
    Unknown,
}

/// <summary>Mod counts + active profile for a game, rolled up onto its library row.</summary>
public sealed record GameModState(int ModCount, int EnabledCount, string? ActiveProfile);

/// <summary>One row in the Game Library home view — a game plus everything the home screen needs to render it.</summary>
public sealed record GameLibraryRow(
    string Id,
    string Name,
    string? StoreSource,
    string? CoverPath,
    LastPlayed Recency,
    int ModCount,
    int EnabledCount,
    string? ActiveProfile,
    EngineTier Tier,
    string? BanRisk,
    IReadOnlyList<string> DetectedLoaders,
    string? NexusDomain);
