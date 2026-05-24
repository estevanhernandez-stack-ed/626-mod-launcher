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
}
