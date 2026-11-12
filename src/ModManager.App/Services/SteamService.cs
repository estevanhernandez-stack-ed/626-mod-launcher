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

    /// <summary>The signed-in account Steam records at <c>HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser</c>:
    /// 0 until sign-in finishes and after shutdown.</summary>
    public int ActiveUser()
    {
        try
        {
            using var k = Win32Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            return k?.GetValue("ActiveUser") is int u ? u : 0;
        }
        catch { return 0; }
    }

    /// <summary>Running AND signed in, so a game started now can pass its ownership check
    /// (<see cref="SteamReadiness"/>).</summary>
    public bool IsReady() => SteamReadiness.IsReady(IsRunning(), ActiveUser());

    /// <summary>Ensure Steam is up AND signed in, then return true (or false on timeout). Starts Steam via
    /// <c>steam://open/main</c>, which opens the client WITHOUT launching any game, so the caller can run its own
    /// exe target rather than vanilla. Synchronous, so call OFF the UI thread.
    /// <para>It used to return the moment steam.exe existed, about three seconds before sign-in, and ACE COMBAT 8
    /// launched then reported "you don't own this game" (2026-11-11). It now waits for the signed-in account.</para></summary>
    public bool EnsureRunning(TimeSpan timeout)
    {
        if (IsReady()) return true;
        if (!IsRunning())
        {
            try { Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true }); }
            catch { return false; }
        }
        return SteamReadiness.WaitUntilReady(IsRunning, ActiveUser, timeout, Thread.Sleep, () => DateTime.UtcNow);
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
                        games.Add(new InstalledGame("steam", m.AppId, m.Name!, full) { BuildId = m.BuildId, LastPlayed = m.LastPlayed });
                }
                catch { /* skip a malformed manifest */ }
            }
        }
        return InstalledGameSort.RecentlyPlayedFirst(games);
    }

    public string? ResolveCoverArtPath(string appId) => ResolveCoverArtPath(appId, CoverShape.Landscape);

    public string? ResolveCoverArtPath(string appId, CoverShape shape)
    {
        if (string.IsNullOrEmpty(appId)) return null;
        var steam = FindSteamPath();
        if (steam is null) return null;
        var dir = Path.Combine(steam, "appcache", "librarycache", appId);
        if (!Directory.Exists(dir)) return null;
        try { return SteamArt.PickCover(Directory.GetFiles(dir), shape); }
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
