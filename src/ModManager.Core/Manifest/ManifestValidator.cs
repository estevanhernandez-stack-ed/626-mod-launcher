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
        var seenLoaderIds = new HashSet<string>(StringComparer.Ordinal);
        // A null list or a null entry is reachable from JSON ("loaders": null overrides the empty
        // default). It must degrade like any other bad entry; a throw here would escape
        // LoadVerifiedRemote, which only catches JsonException, and break its fall-back-to-embedded
        // contract at startup.
        foreach (var l in manifest.Loaders ?? Array.Empty<LoaderManifestEntry>())
        {
            if (l is null) { rejectedLoaders.Add("(null)"); continue; }
            // Same order as games: an engine this binary does not know is SKIPPED before anything else
            // is judged, so a newer feed's loader lands in the forward-compat bucket, not the unsafe one.
            if (!string.IsNullOrWhiteSpace(l.Engine) && !knownEngines.Contains(l.Engine)) { skippedLoaders.Add(l.Id); continue; }
            // A field this binary does not know may be a pin it cannot honour, so the loader is
            // skipped, not used as if unpinned. Forward-compat bucket, the same as an unknown engine.
            if (l.UnknownFields is { Count: > 0 }) { skippedLoaders.Add(l.Id); continue; }
            if (LoaderProblem(l) is not null) { rejectedLoaders.Add(l.Id); continue; }
            // One loader per id. The ban-risk gate keys a dictionary on the id, so a duplicate would
            // throw there; the first one wins, as the miner's own duplicate check would have demanded.
            if (!seenLoaderIds.Add(l.Id)) { rejectedLoaders.Add(l.Id); continue; }
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
        // Lowercase kebab only. The merge matches ids exactly, so "Seamless-Coop" would sit beside the
        // built-in seamless-coop as a second loader rather than correct it. Refusing the spelling is
        // what makes exact matching safe.
        if (!IsLoaderId(loader.Id)) return $"loader id '{loader.Id}' is not lowercase kebab-case";
        if (string.IsNullOrWhiteSpace(loader.DisplayName)) return $"loader '{loader.Id}' has no displayName";
        if (string.IsNullOrWhiteSpace(loader.Engine)) return $"loader '{loader.Id}' has no engine";
        // A pin must be a real Steam app id. "" would pass a null check, pin the loader to nothing,
        // and make it vanish from every game without a word.
        if (loader.SteamAppId is { } app && !IsSteamAppId(app))
            return $"loader '{loader.Id}' steamAppId '{app}' is not a Steam app id; leave it out for an engine-wide loader";
        // Manifest ids are lowercase kebab, so any other spelling could never match a game. An empty
        // list is refused for the reason an empty steamAppId is: leave the field out for engine-wide.
        if (loader.GameIds is { } gameIds)
        {
            if (gameIds.Count == 0)
                return $"loader '{loader.Id}' has an empty gameIds list; leave it out for an engine-wide loader";
            foreach (var gameId in gameIds)
                if (gameId is null || !IsLoaderId(gameId))
                    return $"loader '{loader.Id}' gameIds entry '{gameId}' is not a lowercase kebab-case manifest id";
        }
        if (loader.LauncherExeNames is not { Count: > 0 } exes) return $"loader '{loader.Id}' names no launcher exe";
        foreach (var exe in exes)
            if (!IsBareExeName(exe)) return $"loader '{loader.Id}' launcher name '{exe}' is not a bare .exe filename";
        if (!Uri.TryCreate(loader.GetUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            return $"loader '{loader.Id}' getUrl is not an absolute https URL";
        return null;
    }

    // Windows' invalid filename characters, spelled out rather than taken from
    // Path.GetInvalidFileNameChars(): on Linux that set is only '/' and NUL, so the miner (which runs
    // there) would sign names every Windows client then drops. One fixed set means one verdict.
    private static readonly char[] ForbiddenInExeName =
        "<>:\"/\\|?*".Concat(Enumerable.Range(0, 32).Select(c => (char)c)).ToArray();

    private static bool IsBareExeName(string? name)
        => !string.IsNullOrEmpty(name)
           && name == name.Trim()
           && name.Length > ".exe".Length
           && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
           && name.IndexOfAny(ForbiddenInExeName) < 0
           && !name.Contains("..", StringComparison.Ordinal);

    private static bool IsLoaderId(string id)
        => id.Length > 0
           && id[0] != '-' && id[^1] != '-'
           && !id.Contains("--", StringComparison.Ordinal)
           && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static bool IsSteamAppId(string app)
        => app.Length > 0 && app.All(char.IsAsciiDigit);

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Path.IsPathRooted(path)) return false;          // absolute
        if (path.Contains(':')) return false;               // drive-qualified (e.g. "D:relative")
        var segments = path.Split('/', '\\');
        return !segments.Contains("..");                    // traversal
    }
}
