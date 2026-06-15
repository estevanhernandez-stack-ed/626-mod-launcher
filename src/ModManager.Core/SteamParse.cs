using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Pure Steam parsing helpers (no IO). App id from a launch URL, library paths from a
/// libraryfolders.vdf. The registry read of the Steam install path and steam:// launch
/// are the integration adapter built on top in the app shell. Mirrors steam-core.js.
/// </summary>
public static partial class SteamParse
{
    [GeneratedRegex(@"rungameid/(\d+)")]
    private static partial Regex RunGameIdRe();

    [GeneratedRegex(@"app/(\d+)")]
    private static partial Regex AppRe();

    [GeneratedRegex("\"path\"\\s*\"([^\"]+)\"")]
    private static partial Regex PathRe();

    [GeneratedRegex("\"appid\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AppIdKeyRe();

    [GeneratedRegex("\"name\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex NameKeyRe();

    [GeneratedRegex("\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirKeyRe();

    [GeneratedRegex("\"buildid\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex BuildIdKeyRe();

    [GeneratedRegex("\"StateFlags\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex StateFlagsKeyRe();

    [GeneratedRegex("\"LastPlayed\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LastPlayedKeyRe();

    public static string? ParseAppId(string? launchUrl, string? steamAppId)
    {
        if (!string.IsNullOrEmpty(steamAppId)) return steamAppId;
        if (string.IsNullOrEmpty(launchUrl)) return null;
        var m = RunGameIdRe().Match(launchUrl);
        if (!m.Success) m = AppRe().Match(launchUrl);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static IReadOnlyList<string> ParseLibraryFolders(string? vdfText)
        => PathRe().Matches(vdfText ?? "")
            .Select(m => m.Groups[1].Value.Replace(@"\\", @"\"))
            .ToList();

    /// <summary>The fields we need from a Steam appmanifest_*.acf (all optional).</summary>
    public static AppManifest ParseAppManifest(string? acfText)
    {
        var s = acfText ?? "";
        return new AppManifest(
            AppIdKeyRe().Match(s) is { Success: true } a ? a.Groups[1].Value : null,
            NameKeyRe().Match(s) is { Success: true } n ? n.Groups[1].Value : null,
            InstallDirKeyRe().Match(s) is { Success: true } d ? d.Groups[1].Value : null,
            BuildIdKeyRe().Match(s) is { Success: true } b ? b.Groups[1].Value : null,
            StateFlagsKeyRe().Match(s) is { Success: true } sf ? sf.Groups[1].Value : null,
            LastPlayedKeyRe().Match(s) is { Success: true } lp ? lp.Groups[1].Value : null);
    }
}

/// <summary>Parsed fields from a Steam appmanifest (appid / name / installdir / buildid / stateFlags / lastPlayed).</summary>
public sealed record AppManifest(string? AppId, string? Name, string? InstallDir, string? BuildId = null, string? StateFlags = null, string? LastPlayed = null);
