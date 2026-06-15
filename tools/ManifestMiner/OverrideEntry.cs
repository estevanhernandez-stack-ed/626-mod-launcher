namespace ManifestMiner;

/// <summary>A hand-curated correction, keyed by Steam app id. Any non-null field overrides the
/// mined value on the matched entry (or seeds a new entry when the Steam id isn't in the backbone).
/// Curated data wins over everything the miner produced.</summary>
public sealed record OverrideEntry
{
    public string SteamAppId { get; init; } = "";   // the key (required)
    public string? Id { get; init; }                 // slug for an ADDED entry (else derived from Name)
    public string? Name { get; init; }
    public string? Engine { get; init; }
    public string? ModPath { get; init; }
    public string? NexusDomain { get; init; }
    public int? Featured { get; init; }
    public string? BanRisk { get; init; }
    public string? SaveDirHint { get; init; }
    public IReadOnlyList<string>? FileExtensions { get; init; }
}
