namespace ModManager.Core.Frameworks;

/// <summary>
/// A "how to use this framework" description, read LIVE from the installed files so it reflects the
/// user's actual config (real hotkeys, real mods path) rather than a static blurb. The App renders
/// <see cref="Lines"/> in a toast when the user clicks an installed-framework button.
/// </summary>
public sealed record FrameworkUsageInfo(
    string DisplayName,
    IReadOnlyList<string> Lines,
    string? DocsUrl);

/// <summary>
/// Reads a framework's on-disk config and produces a usage how-to. UE4SS gets a settings-aware reader
/// (hot-reload key + console state from UE4SS-settings.ini, the real Mods folder); unknown frameworks
/// fall back to a generic "installed at &lt;path&gt;" so the toast always says something. Pure Core —
/// no UI, no throw (a missing/garbled settings file degrades to the generic guidance).
/// </summary>
public static class FrameworkUsage
{
    /// <param name="frameworkId">The catalog framework id (e.g. "ue4ss").</param>
    /// <param name="installPath">The resolved absolute install root from the framework's manifest
    /// (for UE4SS: &lt;project&gt;/Binaries/Win64).</param>
    public static FrameworkUsageInfo Describe(string frameworkId, string installPath)
        => frameworkId switch
        {
            "ue4ss" => DescribeUe4ss(installPath),
            _ => Generic(frameworkId, installPath),
        };

    private static FrameworkUsageInfo DescribeUe4ss(string installPath)
    {
        const string docs = "https://docs.ue4ss.com";
        var modsPath = Path.Combine(installPath, "ue4ss", "Mods");
        var ini = ReadIni(Path.Combine(installPath, "ue4ss", "UE4SS-settings.ini"));

        var lines = new List<string>
        {
            "UE4SS loads with the game — just launch it normally. There's no separate .exe to run.",
        };

        // Hot-reload: "Ctrl + <HotReloadKey>" (CTRL is always required per UE4SS).
        var hotKey = ini.TryGetValue(("General", "HotReloadKey"), out var hk) && !string.IsNullOrWhiteSpace(hk)
            ? hk.Trim()
            : "R";
        lines.Add($"Hot-reload Lua mods in-game: Ctrl + {hotKey}.");

        // Console: on when either the text console OR the GUI console flag is enabled.
        var consoleOn = IsEnabled(ini, "Debug", "ConsoleEnabled") || IsEnabled(ini, "Debug", "GuiConsoleEnabled");
        lines.Add(consoleOn
            ? "Debug console: on — it opens with the game."
            : "Debug console: off — enable ConsoleEnabled in ue4ss/UE4SS-settings.ini to turn it on.");

        lines.Add($"Mods go in: {modsPath}");

        return new FrameworkUsageInfo("UE4SS", lines, docs);
    }

    private static FrameworkUsageInfo Generic(string frameworkId, string installPath)
        => new(frameworkId, new[] { $"Installed at: {installPath}" }, DocsUrl: null);

    private static bool IsEnabled(
        IReadOnlyDictionary<(string Section, string Key), string> ini, string section, string key)
        => ini.TryGetValue((section, key), out var v)
           && string.Equals(v.Trim(), "1", StringComparison.Ordinal);

    // Minimal INI reader: [Section] headers + "key = value" lines, ';'-comments skipped. Keyed by
    // (section, key) so same-named keys in different sections don't collide. Never throws — a missing
    // or unreadable file yields an empty map (the describer degrades to defaults + generic guidance).
    private static IReadOnlyDictionary<(string, string), string> ReadIni(string path)
    {
        var map = new Dictionary<(string, string), string>();
        try
        {
            if (!File.Exists(path)) return map;
            var section = "";
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1].Trim();
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                if (key.Length > 0) map[(section, key)] = val;
            }
        }
        catch { /* unreadable -> empty map, caller degrades gracefully */ }
        return map;
    }
}
