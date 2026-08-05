namespace ModManager.Core.Discovery;

/// <summary>
/// Pure classification of a swept file listing into mod candidates. The caller enumerates the
/// disk (same contract as <see cref="ModManager.Core.LooseMods.LooseModScan"/>); this decides
/// what is plausibly a mod.
///
/// THE SAFETY LINE: anything not matched by a signature, an engine-shaped rule, or an archive
/// extension is INVISIBLE. A game file must never be proposed as a mod — false silence is the
/// acceptable failure, false accusation is not.
/// </summary>
public static class DiscoverySweep
{
    // The proxy-loader names + .asi convention: a game never ships these, so they are mods
    // regardless of location. Mirrors LooseModScan's by-nature rules.
    private static readonly string[] ProxyNames =
        { "dinput8.dll", "version.dll", "winmm.dll", "d3d11.dll", "dxgi.dll", "winhttp.dll" };

    private static readonly string[] ArchiveExtensions = { "zip", "7z", "rar" };

    public static IReadOnlyList<DiscoveryCandidate> Classify(
        IReadOnlyList<string> relativePaths, DiscoverySweepOptions options)
    {
        var found = new List<DiscoveryCandidate>();
        foreach (var path in relativePaths)
        {
            var normalized = path.Replace('\\', '/');
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

            if (IsEngineShaped(normalized, extension, options))
                found.Add(new DiscoveryCandidate(normalized, fileName, DiscoveryKind.EngineShaped));
        }
        return found;
    }

    private static bool IsSkipped(string path, IReadOnlyList<string> skipFolders)
        => skipFolders.Any(folder =>
            path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/" + folder + "/", StringComparison.OrdinalIgnoreCase));

    private static bool IsSignature(string fileName, string extension)
        => extension == "asi" || ProxyNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    // Engine-typical extension AND inside this game's mod folder. Both halves are required:
    // the same .pak extension is a shipped game file one directory up.
    private static bool IsEngineShaped(string path, string extension, DiscoverySweepOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ModPath)) return false;
        if (!options.EngineExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return false;
        var modPath = options.ModPath.Replace('\\', '/').Trim('/');
        return path.StartsWith(modPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Extension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot < 0 ? "" : fileName[(dot + 1)..].ToLowerInvariant();
    }
}
