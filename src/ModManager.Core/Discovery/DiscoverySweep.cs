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
                found.Add(new DiscoveryCandidate(normalized, fileName, DiscoveryKind.Archive));
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

    private static bool IsSkipped(string path, IReadOnlyList<string> skipFolders)
        => skipFolders.Any(folder =>
            path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/" + folder + "/", StringComparison.OrdinalIgnoreCase));

    private static bool IsSignature(string fileName, string extension)
        => extension == "asi" || LooseModScan.ProxyNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);

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
