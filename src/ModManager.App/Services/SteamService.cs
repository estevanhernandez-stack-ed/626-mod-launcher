using System.IO;
using ModManager.Core;
using Win32Registry = Microsoft.Win32.Registry;

namespace ModManager.App.Services;

/// <summary>One installed Steam game: app id, display name, and the resolved install folder.</summary>
public sealed record SteamGame(string AppId, string Name, string InstallDir);

/// <summary>
/// Discovers installed Steam games so the Add Game wizard can pre-fill name/folder/appId.
/// This is the integration adapter the Core deliberately leaves out: it reads the Windows
/// registry for the Steam path and scans appmanifest files, but the VDF/ACF parsing itself
/// lives in Core (<see cref="SteamParse"/>), so it stays tested + portable.
/// </summary>
public sealed class SteamService
{
    public IReadOnlyList<SteamGame> InstalledGames()
    {
        var steam = FindSteamPath();
        if (steam is null) return Array.Empty<SteamGame>();

        var games = new List<SteamGame>();
        var seen = new HashSet<string>();
        foreach (var lib in Libraries(steam))
        {
            var steamapps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            string[] manifests;
            try { manifests = Directory.GetFiles(steamapps, "appmanifest_*.acf"); }
            catch { continue; }
            foreach (var acf in manifests)
            {
                try
                {
                    var m = SteamParse.ParseAppManifest(File.ReadAllText(acf));
                    if (m.AppId is null || string.IsNullOrEmpty(m.Name) || string.IsNullOrEmpty(m.InstallDir)) continue;
                    if (!seen.Add(m.AppId)) continue;
                    var full = Path.Combine(steamapps, "common", m.InstallDir);
                    if (Directory.Exists(full)) games.Add(new SteamGame(m.AppId, m.Name!, full));
                }
                catch { /* skip a malformed manifest */ }
            }
        }
        return games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? FindSteamPath()
    {
        try
        {
            using var k = Win32Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (k?.GetValue("SteamPath") is string p && Directory.Exists(p)) return p;
        }
        catch { /* fall through */ }
        try
        {
            using var k = Win32Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (k?.GetValue("InstallPath") is string p && Directory.Exists(p)) return p;
        }
        catch { /* none */ }
        return null;
    }

    private static IReadOnlyList<string> Libraries(string steam)
    {
        var libs = new List<string> { steam }; // the main Steam dir is itself a library
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        try { if (File.Exists(vdf)) libs.AddRange(SteamParse.ParseLibraryFolders(File.ReadAllText(vdf))); }
        catch { /* main dir only */ }
        return libs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
