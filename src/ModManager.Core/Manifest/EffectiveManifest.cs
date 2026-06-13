using System.Threading;

namespace ModManager.Core.Manifest;

/// <summary>
/// Produces the effective game manifest: the embedded snapshot overlaid with a verified remote
/// manifest (when one has been set). The facades read <see cref="Current"/>. <see cref="SetRemote"/>
/// is called once at startup by the App layer after it loads + verifies a cached remote manifest;
/// until then the remote is null and Current == the embedded manifest, so behavior is unchanged.
/// </summary>
public static class EffectiveManifest
{
    // Reference assignment is atomic; volatile gives cross-thread visibility. SetRemote is a
    // startup-time, single-writer operation in production; reads are lock-free.
    private static volatile GameManifest? _remote;
    private static int _generation;

    /// <summary>Monotonic counter; advances on every <see cref="SetRemote"/>. Consumers cache by it.</summary>
    public static int Generation => Volatile.Read(ref _generation);

    /// <summary>The embedded manifest overlaid with the current remote (if any).</summary>
    public static GameManifest Current => Merge(EmbeddedGameManifest.Current, _remote);

    /// <summary>Set (or clear, with null) the verified remote manifest. Bumps <see cref="Generation"/>.</summary>
    public static void SetRemote(GameManifest? remote)
    {
        _remote = remote;
        Interlocked.Increment(ref _generation);
    }

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
