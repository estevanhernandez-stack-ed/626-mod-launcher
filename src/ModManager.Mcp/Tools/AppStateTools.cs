using System.ComponentModel;
using ModelContextProtocol.Server;
using ModManager.Core;
using ModManager.Core.Nexus;
using CoreThemes = ModManager.Core.Themes;

namespace ModManager.Mcp.Tools;

/// <summary>
/// App-level state for an agent (E1, third slice): the themes and the preferences a human sees in
/// Settings. Both read through the same Core functions the app reads through
/// (<see cref="CoreThemes.LoadUserThemes"/>, <see cref="CoreThemes.PickActive"/>,
/// <see cref="AppSettingsFile.Read"/>), so the answer is what the app starts from, not a second reading
/// of the files. Parity plus the reason: each answer also says why a value is what it is (a saved
/// theme that went missing, a key that defaulted, a theme file that would not parse).
/// </summary>
[McpServerToolType]
public static class AppStateTools
{
    [McpServerTool(Name = "list_themes")]
    [Description("The themes the Settings theme picker offers (built-in and user/agent themes) and the active "
                 + "one, resolved from what is on disk the way the app resolves it at start and when Settings "
                 + "closes. If the saved pick is gone, savedThemeMissing is true and the default is active "
                 + "instead. Also lists user theme files the picker leaves out, with the reason, and each "
                 + "theme's contrast warnings.")]
    public static object ListThemes()
    {
        var userLoad = CoreThemes.LoadUserThemes(Path.Combine(McpConfig.DataRoot, "themes"));
        var themes = CoreThemes.BuildThemeList(CoreThemes.BuiltinThemes, userLoad.Themes);
        var userIds = userLoad.Themes.Select(t => t.Id).ToHashSet();
        var settings = AppSettingsFile.Read(AppSettingsFile.PathIn(McpConfig.DataRoot));
        var (active, savedMissing) = CoreThemes.PickActive(themes, settings.ThemeId);

        return new
        {
            ok = true,
            activeThemeId = active.Id,
            activeThemeName = active.Name,
            savedThemeId = settings.ThemeId,
            savedThemeMissing = savedMissing,
            defaultThemeId = CoreThemes.DefaultThemeId,
            themes = themes.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                source = userIds.Contains(t.Id) ? "user" : "builtin",
                overridesBuiltin = userIds.Contains(t.Id) && CoreThemes.BuiltinThemes.ContainsKey(t.Id),
                active = t.Id == active.Id,
                contrastWarnings = CoreThemes.ContrastReport(t),
            }).ToArray(),
            unusableUserThemeFiles = userLoad.Unusable.Select(u => new { file = u.File, reason = u.Reason }).ToArray(),
            hint = "A user theme with the same id as a built-in replaces it. A file in unusableUserThemeFiles is "
                   + "not offered at all. This reads the disk: a window that is already open keeps the theme it "
                   + "loaded until Settings reloads the list, so a theme file changed under a running app shows "
                   + "here first.",
        };
    }

    [McpServerTool(Name = "get_app_settings")]
    [Description("The app-wide preferences from Settings (backdrop, definition auto-update, mod update checks, "
                 + "plugin updates, close to tray, saved theme), as the app reads them, plus the Nexus "
                 + "connection as stored. fileState and defaulted say why a value is what it is. No token "
                 + "or key is ever returned: nexus.tokensStored says only that a sign-in is on file, not "
                 + "that it is still valid.")]
    public static object GetAppSettings()
    {
        var s = AppSettingsFile.Read(AppSettingsFile.PathIn(McpConfig.DataRoot));

        var nexusPath = Path.Combine(McpConfig.DataRoot, "nexus.json");
        NexusTokenStore.StoreSummary? nexus = null;
        try { if (File.Exists(nexusPath)) nexus = NexusTokenStore.Describe(File.ReadAllText(nexusPath)); }
        catch { nexus = new NexusTokenStore.StoreSummary(false, null, false, true); }

        return new
        {
            ok = true,
            backdrop = s.Backdrop,
            autoUpdateDefinitions = s.AutoUpdateDefinitions,
            autoCheckModUpdates = s.AutoCheckModUpdates,
            keepPluginsUpdated = s.KeepPluginsUpdated,
            closeToTray = s.CloseToTray,
            themeId = s.ThemeId,
            fileState = s.FileState,
            defaulted = s.Defaulted,
            nexus = new
            {
                tokensStored = nexus?.TokensStored ?? false,
                connectedUser = nexus?.ConnectedUser,
                // The app deletes a legacy api-key file on its next start and asks for a reconnect.
                legacyKeyFile = nexus?.LegacyKey ?? false,
                unreadable = nexus?.Unreadable ?? false,
                fileState = nexus is null ? "missing" : nexus.Unreadable ? "unreadable" : "ok",
            },
            hint = "A key listed in `defaulted` was absent, the wrong type or a value the app doesn't know in "
                   + "app-settings.json, and reads as its default. nexus.tokensStored means a sign-in is on file, "
                   + "not that it still works: if the app can't open it (expired, revoked, another Windows user), "
                   + "the app shows signed out. Premium status is not stored on disk; the app learns it at sign-in.",
        };
    }
}
