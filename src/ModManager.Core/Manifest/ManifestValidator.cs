namespace ModManager.Core.Manifest;

/// <summary>Result of validating a manifest: the surviving entries, plus what was dropped and why.</summary>
public sealed record ManifestValidationResult(
    GameManifest Manifest,
    IReadOnlyList<string> SkippedUnknownEngines,
    IReadOnlyList<string> RejectedEntries)
{
    /// <summary>Loader ids skipped because this binary does not know their engine.</summary>
    public IReadOnlyList<string> SkippedLoaders { get; init; } = Array.Empty<string>();

    /// <summary>Loader ids rejected as unsafe or incomplete (see <see cref="ManifestValidator.LoaderProblem"/>).</summary>
    public IReadOnlyList<string> RejectedLoaders { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Pure validation gate for a manifest from any source (embedded or, later, remote). Two rules:
///  - An entry whose non-null engine is unknown to this binary is SKIPPED (forward-compat: an old
///    binary reading a newer manifest simply doesn't see games it can't handle). Null engine is fine.
///  - An entry with an unsafe ModPath (absolute / drive-qualified / contains a ".." segment) is
///    REJECTED. ModPath is the one trust-sensitive field; the forbidden-paths gate at intake is the
///    downstream backstop, this is defense in depth.
/// Loaders get the same split: an unknown engine is skipped, and anything <see cref="LoaderProblem"/>
/// objects to is rejected. A rejected loader is dropped alone; the rest of the manifest still applies.
/// </summary>
public static class ManifestValidator
{
    public static ManifestValidationResult Validate(GameManifest manifest, IReadOnlySet<string> knownEngines)
    {
        var kept = new List<GameManifestEntry>();
        var skipped = new List<string>();
        var rejected = new List<string>();

        foreach (var g in manifest.Games)
        {
            if (g.Engine is { } engine && !knownEngines.Contains(engine))
            {
                skipped.Add(g.Id);
                continue;
            }
            if (g.ModPath is { } path && !IsSafeRelativePath(path))
            {
                rejected.Add(g.Id);
                continue;
            }
            kept.Add(g);
        }

        var keptLoaders = new List<LoaderManifestEntry>();
        var skippedLoaders = new List<string>();
        var rejectedLoaders = new List<string>();
        foreach (var l in manifest.Loaders)
        {
            if (LoaderProblem(l) is not null) { rejectedLoaders.Add(l.Id); continue; }
            if (!knownEngines.Contains(l.Engine!)) { skippedLoaders.Add(l.Id); continue; }
            keptLoaders.Add(l);
        }

        return new ManifestValidationResult(
            manifest with { Games = kept, Loaders = keptLoaders },
            skipped,
            rejected)
        {
            SkippedLoaders = skippedLoaders,
            RejectedLoaders = rejectedLoaders,
        };
    }

    /// <summary>
    /// Why a loader entry must not be used, or null when it may. Shared with the miner, so a curated
    /// loader is refused before it is ever signed rather than after it ships.
    ///
    /// <para>The exe names are the trust-sensitive part: the app launches whatever file has one of
    /// these names in the game folder. Each must be a bare <c>*.exe</c> filename, with no directory, no
    /// drive, no traversal and no padding. The "Get it here" link must be absolute https.</para>
    /// </summary>
    public static string? LoaderProblem(LoaderManifestEntry loader)
    {
        if (string.IsNullOrWhiteSpace(loader.Id)) return "a loader has no id";
        if (string.IsNullOrWhiteSpace(loader.DisplayName)) return $"loader '{loader.Id}' has no displayName";
        if (string.IsNullOrWhiteSpace(loader.Engine)) return $"loader '{loader.Id}' has no engine";
        if (loader.LauncherExeNames is not { Count: > 0 } exes) return $"loader '{loader.Id}' names no launcher exe";
        foreach (var exe in exes)
            if (!IsBareExeName(exe)) return $"loader '{loader.Id}' launcher name '{exe}' is not a bare .exe filename";
        if (!Uri.TryCreate(loader.GetUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            return $"loader '{loader.Id}' getUrl is not an absolute https URL";
        return null;
    }

    // Separators and ':' are checked explicitly, not left to GetInvalidFileNameChars: on Linux that set
    // holds only '/' and NUL, and the gate has to mean the same thing wherever the tests run.
    private static bool IsBareExeName(string? name)
        => !string.IsNullOrEmpty(name)
           && name == name.Trim()
           && name.Length > ".exe".Length
           && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
           && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0
           && !name.Contains("..", StringComparison.Ordinal)
           && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Path.IsPathRooted(path)) return false;          // absolute
        if (path.Contains(':')) return false;               // drive-qualified (e.g. "D:relative")
        var segments = path.Split('/', '\\');
        return !segments.Contains("..");                    // traversal
    }
}
