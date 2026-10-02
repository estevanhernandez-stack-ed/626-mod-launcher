using ModManager.Core.Stores;

namespace ModManager.Core.Library;

/// <summary>
/// One row of the library home (B6): a game 626 manages, or an installed game it can see and does
/// not manage. Exactly one of <see cref="Managed"/> and <see cref="Unmanaged"/> is set.
/// </summary>
public sealed record LibraryEntry
{
    public GameLibraryRow? Managed { get; private init; }
    public InstalledGame? Unmanaged { get; private init; }

    /// <summary>When the user last played it, from the best source each kind has: the recency ladder
    /// for a managed game, the store's own record for an unmanaged one. Null when nobody knows.</summary>
    public DateTime? LastPlayedUtc { get; private init; }

    public string Name => Managed?.Name ?? Unmanaged!.Name;
    public bool IsManaged => Managed is not null;

    /// <summary>"steam", "ea", ... for either kind: the registered game's store source, or the store
    /// that reported the install.</summary>
    public string? Store => Managed?.StoreSource ?? Unmanaged?.StoreKind;

    public static LibraryEntry Of(GameLibraryRow managed)
        => new() { Managed = managed, LastPlayedUtc = managed.Recency.LastPlayedUtc };

    public static LibraryEntry Of(InstalledGame unmanaged)
        => new() { Unmanaged = unmanaged, LastPlayedUtc = LibraryList.StoreLastPlayed(unmanaged) };
}

/// <summary>
/// The library home as ONE list (B6, Este 2026-08-18): every game on the system 626 can see, managed
/// or not, in a single order. Managing is something a row has, not a lane it moves between. The old
/// home split them into "your games" and an "Installed games not added yet" expander, which read as an
/// inbox; this reads as a library.
///
/// <para>Pure: no disk, no registry. The caller supplies the managed rows (from
/// <see cref="GameLibraryBuilder"/>) and the installs the stores reported that are not registered
/// (from <see cref="StoreDiscovery.Offerable"/>), so an unmanaged game here is exactly one discovery
/// would have offered, and never one already managed under another id.</para>
/// </summary>
public static class LibraryList
{
    /// <summary>Most recently played first, never-played last, then by name. One rule for both kinds,
    /// so a game played yesterday sits above one played last year whether 626 manages it or not.</summary>
    public static IReadOnlyList<LibraryEntry> Compose(
        IEnumerable<GameLibraryRow> managed, IEnumerable<InstalledGame> unmanaged)
        => managed.Select(LibraryEntry.Of)
            .Concat(unmanaged.Select(LibraryEntry.Of))
            .OrderByDescending(e => e.LastPlayedUtc ?? DateTime.MinValue)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Whether an entry passes the home's search and filters. Search and store apply to both kinds.
    /// Tier and ban-risk are facts 626 works out about games it manages, so an unmanaged game never
    /// passes those filters rather than passing by default: a "ban-risk games" view that listed
    /// unmanaged games 626 never assessed would be claiming an assessment it did not make.
    /// </summary>
    public static bool Matches(LibraryEntry e, string? search, string? store, EngineTier? tier, bool banRiskOnly)
    {
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term) && !e.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(store) && !string.Equals(e.Store, store, StringComparison.OrdinalIgnoreCase)) return false;
        if (tier is { } t && e.Managed?.Tier != t) return false;
        if (banRiskOnly && e.Managed?.BanRisk is null) return false;
        return true;
    }

    /// <summary>The store's own last-played time for an install: Steam stamps unix seconds in the
    /// appmanifest (0 when never played); EA records none. Same reading as the Steam recency source.</summary>
    public static DateTime? StoreLastPlayed(InstalledGame game) => FromUnixSeconds(game.LastPlayed);

    /// <summary>A store's unix-seconds timestamp, or null when it is missing, unreadable, zero (Steam's
    /// "never"), or outside the range a date can hold. One malformed appmanifest must cost one row its
    /// last-played time, never the whole library (FromUnixTimeSeconds throws past year 9999).</summary>
    public static DateTime? FromUnixSeconds(string? raw)
        => long.TryParse(raw, out var unixSeconds) && unixSeconds > 0 && unixSeconds <= MaxUnixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime
            : null;

    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();
}

/// <summary>How to start an installed game through its own store, for a game 626 does not manage.
/// The store's launcher does the launching; nothing here touches the game folder or creates any state
/// for it, which is the point: an unmanaged game gets no data dir.</summary>
public static class StoreLaunch
{
    /// <summary>The store URL that launches the install, or null for a store we cannot launch through.</summary>
    public static string? UrlFor(InstalledGame game) => game.StoreKind switch
    {
        "steam" when !string.IsNullOrWhiteSpace(game.AppId) => $"steam://rungameid/{game.AppId}",
        "ea" when !string.IsNullOrWhiteSpace(game.AppId) => EaGameImport.LaunchUrlFor(game.AppId),
        _ => null,
    };
}
