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
    [Description("The themes the Settings theme picker offers (built-in and user/agent themes) and which one "
                 + "is showing. If the saved pick is gone, savedThemeMissing is true and the default is "
                 + "showing instead. Also lists user theme files that could not be parsed (and so are not "
                 + "offered), and each theme's contrast warnings.")]
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
            unreadableUserThemeFiles = userLoad.Unreadable,
            hint = "A user theme with the same id as a built-in replaces it. A file listed in "
                   + "unreadableUserThemeFiles is not offered in the picker at all.",
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
            hint = "A key listed in `defaulted` was absent or the wrong type in app-settings.json and reads as "
                   + "its default. Premium status is not stored on disk; the app learns it at sign-in.",
        };
    }
}
