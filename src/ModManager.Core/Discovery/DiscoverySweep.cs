using ModManager.Core.LooseMods;

namespace ModManager.Core.Discovery;

/// <summary>
/// Pure classification of a swept file listing into mod candidates. The caller enumerates the
/// disk (same contract as <see cref="ModManager.Core.LooseMods.LooseModScan"/>); this decides
/// what is plausibly a mod.
///
/// THE SAFETY LINE: anything not matched by a signature, an engine-shaped rule, or an archive
/// extension is INVISIBLE. A game file must never be proposed as a mod — false silence is the
/// acceptable failure, false accusation is not. On a paks-root mod path (the mod folder IS
/// Content/Paks itself, e.g. a loader-less UE-pak game like Witchfire) that line also has to hold
/// against the game's OWN shipped paks sitting in the same folder — see the
/// <see cref="PakClassifier.IsBaseGamePak"/> check inside <see cref="Classify"/>, which mirrors
/// the same gate <c>Scanner.cs</c> uses for the regular scan (<c>loc.Form == "paks-root"</c>) and
/// the hard refusal in <c>Scanner.GuardNoBasePakMove</c>.
/// </summary>
public static class DiscoverySweep
{
    // The proxy-loader names + .asi convention: a game never ships these, so they are mods
    // regardless of location. Shares LooseModScan.ProxyNames (internal) rather than a second copy.
    private static readonly string[] ArchiveExtensions = { "zip", "7z", "rar" };

    /// <summary>True when an archive names itself as a mod MANAGER extension rather than a game mod.
    /// Deliberately narrow: it requires the manager name AND the word "extension", so a mod legitimately
    /// called "Vortex Armour" or a mod whose description mentions Vortex is untouched. A refusal here
    /// costs the user a row they would have had to uncheck; a refusal that over-reaches hides a real
    /// mod, so the test is the conservative one.</summary>
    internal static bool IsManagerExtension(string fileName)
    {
        var name = fileName ?? "";
        if (name.IndexOf("extension", StringComparison.OrdinalIgnoreCase) < 0) return false;
        return name.IndexOf("vortex", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("mod organizer", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static IReadOnlyList<DiscoveryCandidate> Classify(
        IReadOnlyList<SweptFile> files, DiscoverySweepOptions options)
    {
        var found = new List<DiscoveryCandidate>();
        foreach (var file in files)
        {
            var normalized = file.RelativePath.Replace('\\', '/');
            if (IsSkipped(normalized, options.SkipFolders)) continue;

            var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
            var extension = Extension(fileName);

            if (ArchiveExtensions.Contains(extension))
            {
                // An archive that says it is a MANAGER extension is not a game mod. Sitting in the
                // same folder is not enough to make something a mod, and offering the user
                // "Vortex Extension Update - Monster Hunter Wilds Vortex Extension v0.1.4.zip" as a
                // mod to adopt is a claim its own filename contradicts (A14).
                if (IsManagerExtension(fileName)) continue;
                found.Add(new DiscoveryCandidate(normalized, fileName, DiscoveryKind.Archive));
                continue;
            }

            // A bare proxy name is a LOADER, not a mod; a .asi is a mod that rides on one.
            // Same safety line, different description downstream — see DiscoveryKind.ProxyLoader.
            if (LooseModScan.ProxyNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                found.Add(new DiscoveryCandidate(normalized, fileName, DiscoveryKind.ProxyLoader));
                continue;
            }

            if (IsSignature(fileName, extension))
            {
                found.Add(new DiscoveryCandidate(normalized, fileName, DiscoveryKind.Signature));
                continue;
            }

            if (IsEngineShaped(normalized, extension, options, out var paksRoot))
            {
                // paks-root: the mod folder IS Content/Paks, so the game's own shipped paks sit in
                // the SAME folder as any mod. Never claim one — the one property this feature must
                // never violate. A dedicated mod folder (paksRoot false) never mixes base-game
                // files in, so this check is a no-op cost there.
                if (paksRoot && PakClassifier.IsBaseGamePak(fileName, file.Size)) continue;
                found.Add(new DiscoveryCandidate(normalized, fileName, DiscoveryKind.EngineShaped));
            }
        }
        return found;
    }

    /// <summary>
    /// Collapse candidates that are the same MOD to one row. The sweep works in file space; the
    /// launcher works in mod-key space, and one mod is routinely several files — a UE mod ships as
    /// a <c>.pak</c> + <c>.ucas</c> + <c>.utoc</c> triplet that <c>Scanner.ModKeyFor</c> folds onto
    /// a single key and the regular scan renders as a single row. Proposing per-file made one mod
    /// look like three and inflated the review dialog's "Adopt N mods" count to the file count
    /// instead of the mod count.
    ///
    /// Grouping is per (<see cref="DiscoveryCandidate.Kind"/>, key): an archive that happens to
    /// share a stem with an extracted mod is DIFFERENT evidence and must stay its own row, since
    /// the tiers resolve them by different means. The representative is the ordinal-first filename,
    /// which is both deterministic and lands on the <c>.pak</c> of a UE triplet (p &lt; u) — the
    /// primary file a user recognizes.
    /// </summary>
    public static IReadOnlyList<DiscoveryCandidate> Deduplicate(
        IReadOnlyList<DiscoveryCandidate> candidates, Func<DiscoveryCandidate, string> keyOf)
        => candidates
            .GroupBy(c => (c.Kind, Key: keyOf(c)), TupleKeyComparer)
            .Select(g => g.OrderBy(c => c.FileName, StringComparer.Ordinal).First())
            .ToList();

    /// <summary>
    /// Drop candidates the launcher ALREADY has a row for. Adoption's promise is "this lists it so
    /// you can turn it on and off" — for a mod that is already listed and already toggleable that
    /// promise is met, so re-offering it is pure noise (and made a Windrose sweep read as though it
    /// wanted to re-add the whole mod list).
    ///
    /// Naming an already-listed-but-unnamed row is a real job, but it belongs to
    /// <c>LooseIdentify</c> ("Identify loose mods"), which reaches exactly these rows. Keeping the
    /// two features disjoint is what stops the same mod appearing in both.
    /// </summary>
    public static IReadOnlyList<DiscoveryCandidate> ExcludeKnownKeys(
        IReadOnlyList<DiscoveryCandidate> candidates,
        Func<DiscoveryCandidate, string> keyOf,
        IEnumerable<string> knownKeys)
    {
        // Built here rather than taken as a set so a caller passing a case-SENSITIVE collection
        // can't silently defeat the exclusion — filename case is not a reliable signal on Windows.
        var known = new HashSet<string>(knownKeys, StringComparer.OrdinalIgnoreCase);
        return candidates.Where(c => !known.Contains(keyOf(c))).ToList();
    }

    private static readonly IEqualityComparer<(DiscoveryKind Kind, string Key)> TupleKeyComparer =
        new KindKeyComparer();

    private sealed class KindKeyComparer : IEqualityComparer<(DiscoveryKind Kind, string Key)>
    {
        public bool Equals((DiscoveryKind Kind, string Key) a, (DiscoveryKind Kind, string Key) b)
            => a.Kind == b.Kind && StringComparer.OrdinalIgnoreCase.Equals(a.Key, b.Key);

        public int GetHashCode((DiscoveryKind Kind, string Key) v)
            => HashCode.Combine(v.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(v.Key));
    }

    /// <summary>
    /// The mod folders the sweep treats as engine-shaped territory: EVERY location the mod list reads,
    /// as resolved by <c>Scanner.GameContext</c>, rebased onto the game root.
    ///
    /// <para>RESOLVED, NOT RAW (B2). The sweep already took its extensions from the resolved context
    /// (<c>ctx.DeclaredExts</c>) and its folders from <c>ctx.Game.ModLocations</c> - the stored
    /// registration. Once the game definition corrected a stale path the two halves described
    /// different games: the sweep looked in a folder the launcher never lists from, and never called a
    /// file in the folder it does list engine-shaped. <c>ctx.Locations</c> is the list the scanner
    /// walks, so a folder the sweep calls a mod folder is one the launcher will actually list from
    /// after adoption. Folders-form locations (one folder per mod, like the appended UE4SS mods
    /// folder) are skipped: the scanner never lists a loose file there, so no row would follow.</para>
    ///
    /// <para>The stale raw folder is deliberately NOT swept as well. Engine-shaped is a promise that
    /// adopting the file gives it a row; a file under a folder the scanner does not read would be
    /// adopted into nothing. An archive or a signature file there is still found, by the rules that
    /// do not depend on a mod folder at all.</para>
    ///
    /// <para>Every path (all locations, not just the first: a UE4SS game can have both ~mods and
    /// LogicMods) is rebased with <see cref="RelativeToRoot"/>; one that resolves outside the game
    /// folder is dropped. A paks-root location (the mod folder IS Content/Paks, e.g. Witchfire) keeps
    /// its flag so <see cref="Classify"/> can refuse the game's own shipped paks.</para>
    /// </summary>
    public static IReadOnlyList<DiscoverySweepModPath> ModPathsFor(GameContext ctx)
    {
        var modPaths = new List<DiscoverySweepModPath>();
        foreach (var loc in ctx.Locations)
            // A folders-form location lists one FOLDER per mod, never a loose file by extension, so an
            // engine-extension file inside one would not become a row - and engine-shaped promises a
            // row. The appended UE4SS mods folder is exactly this shape (m3).
            if (loc.Form != "folders" && RelativeToRoot(loc.Abs, ctx.GameRoot) is { } rel)
                modPaths.Add(new DiscoverySweepModPath(rel, loc.Form == "paks-root"));
        return modPaths;
    }

    /// <summary>Rebase a path that may be absolute OR relative onto "relative to
    /// <paramref name="gameRoot"/>, forward-slashed" - the shape the skip and mod-path matching
    /// expects. Null input, a path that resolves outside the root, or one on another drive (which
    /// makes <see cref="Path.GetRelativePath(string,string)"/> hand back an absolute path unchanged)
    /// all return null, so the caller drops it rather than pass through something that would either
    /// never match or match the wrong thing.</summary>
    public static string? RelativeToRoot(string? path, string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var abs = Path.IsPathRooted(path) ? path : Path.Combine(gameRoot, path);
        var rel = Path.GetRelativePath(gameRoot, abs).Replace('\\', '/');
        if (rel == "." || rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) return null;
        return rel;
    }

    private static bool IsSkipped(string path, IReadOnlyList<string> skipFolders)
        => skipFolders.Any(folder =>
            path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/" + folder + "/", StringComparison.OrdinalIgnoreCase));

    // Proxy names are claimed earlier as ProxyLoader, so by here a signature means the .asi
    // convention: a real mod, riding on whatever loader is installed. A game never ships one.
    private static bool IsSignature(string fileName, string extension)
        => extension == "asi";

    // Engine-typical extension AND inside ONE of this game's mod folders. Both halves are
    // required: the same .pak extension is a shipped game file one directory up. Checks every
    // configured mod path (a UE4SS game can have both ~mods and LogicMods at once) and reports
    // whether the matched path is the paks-root form, so the caller can apply the base-game guard.
    private static bool IsEngineShaped(string path, string extension, DiscoverySweepOptions options, out bool paksRoot)
    {
        paksRoot = false;
        if (!options.EngineExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return false;
        foreach (var modPath in options.ModPaths)
        {
            if (string.IsNullOrWhiteSpace(modPath.Path)) continue;
            var normalized = modPath.Path.Replace('\\', '/').Trim('/');
            if (normalized.Length == 0) continue;
            if (!path.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase)) continue;
            paksRoot = modPath.PaksRoot;
            return true;
        }
        return false;
    }

    private static string Extension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot < 0 ? "" : fileName[(dot + 1)..].ToLowerInvariant();
    }
}
