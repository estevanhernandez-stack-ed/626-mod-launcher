using System.Text.Json;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>
/// Hand-curated mod loaders: <c>overrides/loaders/*.json</c>, one <see cref="LoaderManifestEntry"/> per
/// file. They become the published manifest's top-level <c>loaders</c> list.
///
/// <para>A subfolder rather than more files beside the games: <see cref="OverridesLoader"/> reads only
/// the top level of <c>overrides/</c>, so a loader file can never be mistaken for a game override.</para>
///
/// <para>The published loaders are exactly what is curated here. The launcher's embedded snapshot is
/// the baseline the feed merges over, so a loader that needs no correction does not need a file.</para>
/// </summary>
public static class LoaderOverrides
{
    public const string Subfolder = "loaders";

    /// <summary>Read every loader file under <c>&lt;overridesDir&gt;/loaders</c>. A missing folder is no
    /// loaders; a malformed file is skipped with a warning, the way <see cref="OverridesLoader"/> treats
    /// a bad game file.</summary>
    public static IReadOnlyList<LoaderManifestEntry> Load(string overridesDir)
    {
        var dir = Path.Combine(overridesDir, Subfolder);
        if (!Directory.Exists(dir)) return Array.Empty<LoaderManifestEntry>();

        var result = new List<LoaderManifestEntry>();
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<LoaderManifestEntry>(File.ReadAllText(file), ManifestJson.Options);
                if (entry is not null) result.Add(entry);
            }
            catch (JsonException e)
            {
                Console.Error.WriteLine($"  skipped loader override {Path.GetFileName(file)}: {e.Message}");
            }
        }
        return result;
    }

    /// <summary>
    /// Problems that must stop the run. Two files claiming one loader id is a build failure, not a
    /// resolved conflict, for the same reason as duplicate game keys: one curated file would silently
    /// lose. Ids compare case-insensitively, because two spellings of one id are still one loader to a
    /// curator. What the launcher's gate would refuse is <see cref="Rejections"/>, reported separately.
    /// </summary>
    /// <param name="knownGameIds">The draft's game ids. When given, a <c>gameIds</c> pin naming a game
    /// the feed does not carry is a problem: it would pin the loader to nothing and ship silently.</param>
    public static IReadOnlyList<OverrideProblem> Check(
        IReadOnlyList<LoaderManifestEntry> loaders, IReadOnlySet<string>? knownGameIds = null)
    {
        var problems = new List<OverrideProblem>();
        foreach (var dup in loaders.GroupBy(l => l.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add(new OverrideProblem($"loader id '{dup.Key}' is claimed by {dup.Count()} files"));

        foreach (var l in loaders)
        {
            // The miner is built from the same source as the launcher, so a field it does not know is
            // a typo ("gameId" for "gameIds"), and the launcher would skip the loader for carrying it.
            if (l.UnknownFields is { Count: > 0 } unknown)
                problems.Add(new OverrideProblem(
                    $"loader '{l.Id}' has field(s) the launcher does not know: {string.Join(", ", unknown.Keys)}"));

            if (knownGameIds is not null && l.GameIds is { } pins)
                foreach (var pin in pins.Where(p => p is not null && !knownGameIds.Contains(p)))
                    problems.Add(new OverrideProblem(
                        $"loader '{l.Id}' is pinned to game id '{pin}', which no game in the feed has"));
        }
        return problems;
    }

    /// <summary>Validation problems the launcher would reject the loader for. Reported, not fatal on
    /// their own: the shared gate drops the loader from the draft either way.</summary>
    public static IReadOnlyList<string> Rejections(IReadOnlyList<LoaderManifestEntry> loaders)
        => loaders.Select(ManifestValidator.LoaderProblem).OfType<string>().ToList();

    /// <summary>Put the curated loaders on the draft, replacing whatever it carried.</summary>
    public static GameManifest Apply(GameManifest draft, IReadOnlyList<LoaderManifestEntry> loaders)
        => draft with { Loaders = loaders };
}
