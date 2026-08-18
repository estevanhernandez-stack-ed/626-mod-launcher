namespace ModManager.Core;

/// <summary>
/// One logical mod's pending update, however many FILES of it Nexus tracks.
///
/// <para>Nexus tracks downloads per file, so a mod that publishes four options — "Faster Ships"
/// ships <c>FasterShips10</c>, <c>FasterShips10_B</c>, <c>aaUltraFastShips</c>,
/// <c>aaUltraFastShipsB</c> — produced four identical rows and counted as four updates. The badge
/// said 115 for an install with far fewer mods behind.</para>
/// </summary>
public sealed record PendingUpdateGroup(string Key, string ModName, IReadOnlyList<PendingUpdate> Members)
{
    /// <summary>The member the row speaks for: whichever one can justify a version pair, else the
    /// first. Picking the pair-capable member is what lets a group of mostly flag-pending files still
    /// show the real comparison one of them has.</summary>
    public PendingUpdate Primary =>
        Members.FirstOrDefault(m => m.Reason == PendingReason.VersionDiffers
                                    && !string.IsNullOrWhiteSpace(m.InstalledVersion)
                                    && !string.IsNullOrWhiteSpace(m.LatestVersion)
                                    && !m.VersionsLookEquivalent)
        ?? Members[0];

    public bool IsMulti => Members.Count > 1;

    /// <summary>The mod keys behind the row, in the order they were listed. Shown under a multi-file
    /// row so collapsing never hides which files are involved.</summary>
    public IReadOnlyList<string> MemberKeys => Members.Select(m => m.ModKey).ToList();
}

/// <summary>
/// Collapses per-file pending updates into one entry per mod, the way the mod list already collapses
/// variant rows (Wave 1 / A12 established that a family is one row and several keys).
/// </summary>
public static class PendingUpdateGroups
{
    /// <summary>Group by Nexus mod id when we have one — that IS the identity of a mod page, and all
    /// four Faster Ships files carry id 285. Where no id was ever learned, fall back to the variant
    /// base the mod list already groups on.
    ///
    /// <para>The two keyspaces are kept apart deliberately: a file with an id and a file without one
    /// are not merged on a name coincidence, because the id is evidence and the name is a guess.
    /// Ids are scoped by domain, since mod id 285 means different things on two games' Nexus
    /// sections.</para></summary>
    public static IReadOnlyList<PendingUpdateGroup> Group(IEnumerable<PendingUpdate> pending)
    {
        var order = new List<string>();
        var byKey = new Dictionary<string, List<PendingUpdate>>(StringComparer.Ordinal);

        foreach (var p in pending ?? Array.Empty<PendingUpdate>())
        {
            var key = KeyFor(p);
            if (!byKey.TryGetValue(key, out var list))
            {
                list = new List<PendingUpdate>();
                byKey[key] = list;
                order.Add(key);
            }
            list.Add(p);
        }

        return order.Select(k => new PendingUpdateGroup(k, Title(byKey[k]), byKey[k])).ToList();
    }

    private static string KeyFor(PendingUpdate p)
    {
        if (p.NexusModId is int id && id > 0)
            return $"nexus:{(p.NexusDomain ?? "").Trim().ToLowerInvariant()}:{id}";
        return "base:" + Variant.ParseVariant(p.ModKey ?? "").Base.ToLowerInvariant();
    }

    /// <summary>What the collapsed row is called. The shortest name in the group, because option
    /// files are usually the mod's name plus a qualifier ("Faster Ships" and "Faster Ships - 10x"),
    /// so the shortest is the one closest to the mod itself. Ties break on first-listed.</summary>
    private static string Title(IReadOnlyList<PendingUpdate> members)
    {
        var best = members[0];
        foreach (var m in members)
        {
            var name = Name(m);
            if (name.Length > 0 && (Name(best).Length == 0 || name.Length < Name(best).Length)) best = m;
        }
        return Name(best) is { Length: > 0 } t ? t : best.ModKey ?? "";
    }

    private static string Name(PendingUpdate p)
        => string.IsNullOrWhiteSpace(p.ModName) ? "" : p.ModName.Trim();
}
