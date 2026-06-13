namespace ModManager.Core.Manifest;

/// <summary>
/// Produces the effective game manifest by overlaying a verified remote manifest onto the embedded
/// snapshot. Pure. The remote is assumed already verified + validated (via
/// <see cref="ManifestLoader.LoadVerifiedRemote"/>); a null remote yields the embedded manifest
/// untouched, which is the steady state until the App-layer remote source is wired in (slice 2).
/// Merge is by <see cref="GameManifestEntry.Id"/>: remote entries override same-id embedded entries,
/// remote-only entries are appended, embedded-only entries survive.
/// </summary>
public static class EffectiveManifest
{
    public static GameManifest Merge(GameManifest embedded, GameManifest? remote)
    {
        if (remote is null)
            return embedded;

        var byId = new Dictionary<string, GameManifestEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var g in embedded.Games)
        {
            if (byId.TryAdd(g.Id, g)) order.Add(g.Id);
            else byId[g.Id] = g;
        }
        foreach (var g in remote.Games) // remote wins on id collision; new ids appended in remote order
        {
            if (byId.TryAdd(g.Id, g)) order.Add(g.Id);
            else byId[g.Id] = g;
        }

        return embedded with { Games = order.Select(id => byId[id]).ToList() };
    }
}
