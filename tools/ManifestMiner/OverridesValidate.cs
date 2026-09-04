using ModManager.Core;

namespace ManifestMiner;

/// <summary>One reason the curated set cannot be merged safely.</summary>
public sealed record OverrideProblem(string Message);

/// <summary>
/// Pure check over the loaded overrides, run before any merging.
///
/// <para>Overrides are addressed by Steam app id when they have one and by slug otherwise, and either
/// key claimed twice means one file silently loses. That is not theoretical: two files in the real
/// directory both claim Steam id 20920, and today the richer one wins purely by iteration order — if
/// that flipped, the game would drop to nexus-only with no engine and no mod path, and nothing would
/// report it.</para>
///
/// <para>So a duplicate is a BUILD FAILURE rather than a resolved conflict. There is no second key to
/// disambiguate on, and picking a winner is what got us here.</para>
/// </summary>
public static class OverridesValidate
{
    /// <summary>The slug an entry will be addressed by: its explicit id, else one derived from its
    /// name, else empty — which is itself a problem.</summary>
    public static string KeyOf(OverrideEntry entry)
        => !string.IsNullOrWhiteSpace(entry.Id) ? entry.Id!
         : !string.IsNullOrWhiteSpace(entry.Name) ? EnginePresets.Slugify(entry.Name)
         : "";

    public static IReadOnlyList<OverrideProblem> Check(IReadOnlyList<OverrideEntry> overrides)
    {
        var problems = new List<OverrideProblem>();
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string Where(OverrideEntry e) => e.SourcePath ?? "(unknown file)";

        foreach (var e in overrides.Where(e => KeyOf(e).Length == 0 && string.IsNullOrWhiteSpace(e.SteamAppId)))
            problems.Add(new OverrideProblem(
                $"{Where(e)} has neither an id nor a name, so nothing can address it."));

        void Duplicates(string label, Func<OverrideEntry, string?> keySelector)
        {
            foreach (var group in overrides
                         .Where(e => !string.IsNullOrWhiteSpace(keySelector(e)))
                         .GroupBy(e => keySelector(e)!, StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Count() > 1))
            {
                // One pair of files is ONE problem even when it collides on both keys - reporting it
                // twice would read as two separate conflicts.
                var files = group.Select(Where).OrderBy(f => f, StringComparer.Ordinal).ToList();
                if (!reported.Add(string.Join("|", files))) continue;

                problems.Add(new OverrideProblem(
                    $"{group.Count()} overrides share the same {label} '{group.Key}': {string.Join(", ", files)}. "
                    + "One would silently win; pick one file and delete the other."));
            }
        }

        Duplicates("Steam app id", e => e.SteamAppId);
        Duplicates("id", e => KeyOf(e) is { Length: > 0 } k ? k : null);

        return problems;
    }
}
