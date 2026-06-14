using System.Diagnostics;
using System.IO;
using System.Threading;
using ModManager.Core;
using Win32Registry = Microsoft.Win32.Registry;

namespace ModManager.App.Services;

/// <summary>
/// Discovers installed Steam games so the Add Game wizard can pre-fill name/folder/appId.
/// This is the integration adapter the Core deliberately leaves out: it reads the Windows
/// registry for the Steam path and scans appmanifest files, but the VDF/ACF parsing itself
/// lives in Core (<see cref="SteamParse"/>), so it stays tested + portable.
/// </summary>
public sealed class SteamService : IStoreLibrary
{
    public string StoreKind => "steam";

    /// <summary>
    /// The 64-bit SteamID of the currently signed-in Steam user, or null if Steam isn't installed
    /// or no user is signed in. Used to expand <c>&lt;storeUserId&gt;</c> in Ludusavi save-path
    /// templates (ER's save folder is <c>%APPDATA%/EldenRing/&lt;storeUserId&gt;/</c>, so without
    /// this the save-dir autodetect can't resolve to a real folder). The 32-bit user id lives in
    /// <c>HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser</c>; add the steamID64 offset to
    /// widen it.
    /// </summary>
    public string? CurrentUserId64()
    {
        try
        {
            using var k = Win32Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            if (k?.GetValue("ActiveUser") is int u && u > 0)
                return (76561197960265728L + u).ToString();
        }
        catch { /* no Steam / not signed in / registry unreadable */ }
        return null;
    }

    /// <summary>True when the Steam client is currently running. A Steam-DRM exe launcher (e.g.
    /// Seamless Co-op's ersc_launcher.exe) needs this — its bootstrap silently no-ops if Steam is
    /// closed.</summary>
    public bool IsRunning()
    {
        try { return Process.GetProcessesByName("steam").Length > 0; }
        catch { return false; }
    }

    /// <summary>Ensure the Steam client is up, then return true once it is (or false on timeout).
    /// Starts Steam via <c>steam://open/main</c> — which opens the client WITHOUT launching any
    /// game, so the caller can then run its own exe launcher (Seamless) rather than vanilla. Polls
    /// for the steam process; synchronous, so call OFF the UI thread. Detects the process being
    /// present, which is a close-enough proxy for "ready to bootstrap DRM" — full login-readiness
    /// isn't observable, and the user can retry if Steam is still settling.</summary>
    public bool EnsureRunning(TimeSpan timeout)
    {
        if (IsRunning()) return true;
        try { Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true }); }
        catch { return false; }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsRunning()) return true;
            Thread.Sleep(500);
        }
        return IsRunning();
    }

    public IReadOnlyList<InstalledGame> InstalledGames()
    {
        var steam = FindSteamPath();
        if (steam is null) return Array.Empty<InstalledGame>();

        var games = new List<InstalledGame>();
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
                    if (!SteamInstallState.IsFullyInstalled(m.StateFlags)) continue; // skip mid-download; a fully-installed copy in another library still wins (filter precedes seen.Add)
                    if (!seen.Add(m.AppId)) continue;
                    var full = Path.Combine(steamapps, "common", m.InstallDir);
                    if (Directory.Exists(full))
                        games.Add(new InstalledGame("steam", m.AppId, m.Name!, full) { BuildId = m.BuildId });
                }
                catch { /* skip a malformed manifest */ }
            }
        }
        return games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string? ResolveCoverArtPath(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return null;
        var steam = FindSteamPath();
        if (steam is null) return null;
        var dir = Path.Combine(steam, "appcache", "librarycache", appId);
        if (!Directory.Exists(dir)) return null;
        try { return SteamArt.PickCover(Directory.GetFiles(dir)); }
        catch { return null; }
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
