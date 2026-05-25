using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>A resolved profile field with an on-disk check for the wizard preview.</summary>
public sealed record ResolvedField(string Label, string? Path, ResolveStatus Status, string? Note = null);

public enum ResolveStatus { Pass, Warn, Missing }

/// <summary>The draft resolved to this machine's real paths + per-field verification.</summary>
public sealed record ResolvedProfile(
    string? GameRoot, string? ModFolder, string? SaveDir, string? LauncherPath,
    IReadOnlyList<ResolvedField> Checks);

/// <summary>
/// Resolves a validated <see cref="GameProfileDraft"/> to this machine's real paths and verifies
/// them on disk. Structure -> real paths: GameRoot from Steam (matched on app id) or a caller-supplied
/// browse, save dir via <see cref="SaveLocator.DetectAsync"/> (Ludusavi-first then heuristics) falling
/// back to the saveRoot enum, launcher under GameRoot. Read-only: only Directory/File.Exists checks —
/// nothing is written or moved until the user confirms register. Warnings never block (the game's mods
/// or saves may not exist on disk yet).
/// </summary>
public sealed class GameProfileResolver
{
    private readonly SteamService _steam;
    private readonly LudusaviService _ludu;

    public GameProfileResolver(SteamService steam, LudusaviService ludu)
    {
        _steam = steam;
        _ludu = ludu;
    }

    /// <summary>
    /// Resolve the draft. <paramref name="browsedGameRoot"/> overrides Steam detection when the user
    /// already picked a folder.
    /// </summary>
    public async Task<ResolvedProfile> ResolveAsync(GameProfileDraft d, string? browsedGameRoot)
    {
        var checks = new List<ResolvedField>();

        // GameRoot: a browsed folder wins; else Steam-detect by matching the app id against installed games.
        var gameRoot = !string.IsNullOrEmpty(browsedGameRoot) ? browsedGameRoot : SteamInstallDir(d.SteamAppId);
        checks.Add(Check("Install folder", gameRoot, DirExists(gameRoot)));

        // Mod folder: GameRoot + (the draft's modPath or the engine preset's ModPath default).
        var modRel = !string.IsNullOrEmpty(d.ModPath)
            ? d.ModPath
            : (d.Engine is not null && EnginePresets.Presets.TryGetValue(d.Engine, out var p) ? p.ModPath : null);
        var modFolder = (gameRoot is not null && !string.IsNullOrEmpty(modRel))
            ? Path.Combine(gameRoot, modRel.Replace('/', Path.DirectorySeparatorChar))
            : null;
        checks.Add(Check("Mod folder", modFolder, DirExists(modFolder)));

        // Save dir: reuse the existing save-resolution entry point (Ludusavi by app id, then heuristics);
        // fall back to expanding the saveRoot enum + subpath when that finds nothing.
        var saveDir = await SaveLocator.DetectAsync(_ludu, d.Name ?? "", d.Engine, gameRoot, d.SteamAppId);
        saveDir ??= ExpandSaveRoot(d.SaveRoot, d.SaveSubPath, gameRoot);
        checks.Add(Check("Save folder", saveDir, DirExists(saveDir)));

        // Required launcher, resolved under GameRoot.
        var launcher = (gameRoot is not null && !string.IsNullOrEmpty(d.RequiredLauncher))
            ? Path.Combine(gameRoot, d.RequiredLauncher.Replace('/', Path.DirectorySeparatorChar))
            : null;
        if (launcher is not null) checks.Add(Check("Required launcher", launcher, FileExists(launcher)));

        return new ResolvedProfile(gameRoot, modFolder, saveDir, launcher, checks);
    }

    /// <summary>The install folder of the Steam game with this app id, or null if not installed / no id.</summary>
    private string? SteamInstallDir(string? steamAppId)
    {
        if (string.IsNullOrEmpty(steamAppId)) return null;
        foreach (var g in _steam.InstalledGames())
            if (g.AppId == steamAppId) return g.InstallDir;
        return null;
    }

    private static ResolvedField Check(string label, string? path, bool exists) =>
        new(label, path, path is null ? ResolveStatus.Missing : exists ? ResolveStatus.Pass : ResolveStatus.Warn,
            path is null ? "not resolved" : exists ? null : "not found on disk yet");

    private static bool DirExists(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try { return Directory.Exists(path); } catch { return false; }
    }

    private static bool FileExists(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try { return File.Exists(path); } catch { return false; }
    }

    /// <summary>
    /// Expand a saveRoot enum + relative subpath to a machine path. This is the fallback when the
    /// Ludusavi/heuristic save lookup finds nothing. SteamUserData has no reliable per-user resolver
    /// here, so it returns null (Ludusavi is the authoritative source for those games anyway).
    /// </summary>
    private static string? ExpandSaveRoot(string? root, string? sub, string? gameRoot)
    {
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(sub)) return null;
        var rel = sub.Replace('/', Path.DirectorySeparatorChar);
        string? baseDir = root switch
        {
            "DocumentsMyGames" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games"),
            "AppData" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LocalAppData" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameInstall" => gameRoot,
            "SteamUserData" => null, // no reliable resolver here; Ludusavi covers these games
            _ => null,
        };
        return string.IsNullOrEmpty(baseDir) ? null : Path.Combine(baseDir, rel);
    }
}
