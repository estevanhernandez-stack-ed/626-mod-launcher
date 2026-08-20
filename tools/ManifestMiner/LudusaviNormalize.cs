using ModManager.Core;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Pure: Ludusavi facts -> GameManifestEntry candidates. Steam-id-keyed (our only probe
/// today); engine/modPath/nexusDomain stay null (Ludusavi carries none). Output is a draft for
/// review, not a shipped manifest.</summary>
public static class LudusaviNormalize
{
    public static IReadOnlyList<GameManifestEntry> ToCandidates(IReadOnlyList<LudusaviGame> games)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GameManifestEntry>();

        foreach (var g in games)
        {
            if (string.IsNullOrWhiteSpace(g.SteamAppId)) continue; // need a Steam id to key/verify

            var baseId = EnginePresets.Slugify(g.Name);
            var id = baseId;
            if (!seen.Add(id)) { id = $"{baseId}-{g.SteamAppId}"; seen.Add(id); }

            result.Add(new GameManifestEntry
            {
                Id = id,
                Name = g.Name,
                Stores = new StoreIds { SteamAppId = g.SteamAppId },
                SaveDirHint = PickSaveDir(g.SaveEntries),
                Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" }, Status = "auto" },
            });
        }

        return result;
    }

    /// <summary>
    /// Which of a game's declared paths is the one a Windows Steam user's saves are actually in.
    ///
    /// <para>This used to be <c>SavePaths[0]</c> — the first key of a YAML map, which is not a
    /// preference and never was. Ludusavi is not being sloppy: it zips whole trees and lists every
    /// path a game touches on every platform and storefront, tagging each one. We were the ones
    /// discarding the tags.</para>
    ///
    /// <para>Returning null is a real answer. A game whose only declared paths are config directories
    /// has no save hint, and saying so keeps "nobody looked" distinguishable from "we looked and it is
    /// here" — a distinction the save-layout work depends on.</para>
    /// </summary>
    private static string? PickSaveDir(IReadOnlyList<LudusaviSavePath> entries)
    {
        var usable = entries.Where(IsSave).Where(AppliesHere).ToList();
        if (usable.Count == 0) return null;

        // Two preferences, in this order, and the order was decided by real data.
        //
        // 1. An EXPLICIT match beats an unqualified one. Lies of P declares its Steam save as
        //    <base>/... with `when: store: steam`, and a macOS path with no `when` at all - so
        //    Ludusavi's own data leaves the Mac path looking universal. Ranking the install-relative
        //    path down first handed us the Mac one. A path that names our platform or our store is
        //    making a claim about us; a path that names nothing is a fallback.
        // 2. Then: a save CAN live in the install directory, but when a user-profile path is also on
        //    offer that is the one people mean, and the one that survives a reinstall.
        //
        // OrderByDescending is stable, so ties keep Ludusavi's own order.
        return usable
            .OrderByDescending(IsExplicitMatch)
            .ThenByDescending(e => !IsInstallRelative(e.Path))
            .First().Path;
    }

    // Ludusavi's entire tag vocabulary is `save` and `config`. Untagged is neither.
    private static bool IsSave(LudusaviSavePath e)
        => e.Tags.Any(t => string.Equals(t, "save", StringComparison.OrdinalIgnoreCase));

    // An absent qualifier means "everywhere", not "nowhere" - most of the corpus declares no `when`.
    private static bool AppliesHere(LudusaviSavePath e)
        => (e.Os.Count == 0 || e.Os.Any(o => string.Equals(o, "windows", StringComparison.OrdinalIgnoreCase)))
        && (e.Stores.Count == 0 || e.Stores.Any(x => string.Equals(x, "steam", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Whether this path names our platform or our storefront, rather than saying nothing.
    /// It has already passed <see cref="AppliesHere"/>, so any qualifier present is one that matched.</summary>
    private static bool IsExplicitMatch(LudusaviSavePath e) => e.Os.Count > 0 || e.Stores.Count > 0;

    private static bool IsInstallRelative(string path)
        => path.StartsWith("<base>", StringComparison.OrdinalIgnoreCase);
}
