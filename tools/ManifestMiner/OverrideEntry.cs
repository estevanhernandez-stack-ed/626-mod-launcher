namespace ManifestMiner;

/// <summary>A hand-curated correction. Keyed by Steam app id when it has one, and otherwise by its
/// slug (<see cref="Id"/>) — a game bought from the EA app, Epic or GOG has no Steam id, and refusing
/// those made a whole category of game uncurateable. Any non-null field overrides the mined value on
/// the matched entry, or seeds a new entry when nothing matches. Curated data wins over everything the
/// miner produced.</summary>
public sealed record OverrideEntry
{
    /// <summary>The Steam app id, when the game is on Steam. Null is normal now, not an error.</summary>
    public string? SteamAppId { get; init; }

    /// <summary>The file this entry was read from. Set by <see cref="OverridesLoader"/>, never by the
    /// JSON — it exists so a build problem can name the offending file, and a curated file must not be
    /// able to lie about where it lives.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SourcePath { get; init; }

    public string? Id { get; init; }                 // slug for an ADDED entry (else derived from Name)
    public string? Name { get; init; }
    public string? Engine { get; init; }
    public string? ModPath { get; init; }
    public string? NexusDomain { get; init; }
    public int? Featured { get; init; }
    public string? BanRisk { get; init; }
    public string? SaveLayout { get; init; }
    public IReadOnlyList<string>? SavePlayerPaths { get; init; }
    public string? SafeRoute { get; init; }          // ban-risk nuance: documented safe modding route (batch 4)
    public string? SafeRouteHint { get; init; }      // one user-facing sentence for the route
    public string? SaveDirHint { get; init; }
    public IReadOnlyList<string>? FileExtensions { get; init; }
}
