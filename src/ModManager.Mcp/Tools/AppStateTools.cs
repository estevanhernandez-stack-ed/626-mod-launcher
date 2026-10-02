using System.ComponentModel;
using ModelContextProtocol.Server;
using ModManager.Core;
using ModManager.Core.Agent;
using ModManager.Core.Nexus;
using CoreThemes = ModManager.Core.Themes;

namespace ModManager.Mcp.Tools;

/// <summary>
/// App-level state for an agent (E1, third slice; apply_theme in the sixth): the themes and the
/// preferences a human sees in Settings, and switching the theme. Both read through the same Core functions the app reads through
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
        var (userLoad, themes) = LoadThemes();
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

    // The list the Settings theme picker offers, built the way ThemeService builds it.
    private static (CoreThemes.UserThemeLoad UserLoad, IReadOnlyList<Theme> Themes) LoadThemes()
    {
        var userLoad = CoreThemes.LoadUserThemes(Path.Combine(McpConfig.DataRoot, "themes"));
        return (userLoad, CoreThemes.BuildThemeList(CoreThemes.BuiltinThemes, userLoad.Themes));
    }

    [McpServerTool(Name = "apply_theme")]
    [Description("Switch the launcher's theme, as picking one from the THEME menu does: the pick is saved to "
                 + "app-settings.json through the same writer the app uses. A running launcher switches to it within "
                 + "a moment and says so on its status line, and a closed one opens on it. themeId is an id from "
                 + "list_themes. It changes only the look, never a game or a mod, and is safe to repeat. To undo, "
                 + "apply previousThemeId. Recorded in the launcher's agent-log.jsonl (get_agent_log with no gameId).")]
    public static object ApplyTheme(
        [Description("The theme id, from list_themes (e.g. 626-labs, forge).")] string themeId)
    {
        const string tool = "apply_theme";
        var args = new Dictionary<string, string> { ["themeId"] = themeId ?? "" };
        var (userLoad, themes) = LoadThemes();

        // Ids are file names lowercased, so an id typed in another case is the same theme.
        var wanted = themes.FirstOrDefault(t => string.Equals(t.Id, themeId?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (wanted is null)
        {
            var unusable = userLoad.Unusable.FirstOrDefault(u =>
                string.Equals(Path.GetFileNameWithoutExtension(u.File), themeId?.Trim(), StringComparison.OrdinalIgnoreCase));
            var why = unusable is not null
                ? $"The theme file {unusable.File} is not offered: {unusable.Reason}."
                : $"No theme '{themeId}'.";
            return WriteTools.Refuse(tool, McpConfig.DataRoot, "", args, AgentRefusal.NotFound,
                $"{why} Themes: {string.Join(", ", themes.Select(t => t.Id))}.");
        }

        var path = AppSettingsFile.PathIn(McpConfig.DataRoot);
        var before = AppSettingsFile.Read(path);
        var (wasShowing, _) = CoreThemes.PickActive(themes, before.ThemeId);
        var changed = before.ThemeId != wanted.Id;

        void Audit(string result, string detail) =>
            AgentAudit.Append(McpConfig.DataRoot, new AgentAuditEntry(DateTime.UtcNow, tool, "", args, result, detail));

        if (changed)
        {
            try { AppSettingsFile.WriteKey(path, "themeId", wanted.Id); }
            catch (Exception e)
            {
                var detail = ErrorRemedy.Describe(e);
                Audit("error", detail);
                return new { ok = false, refusal = "error", detail };
            }
        }

        // Verify by reading back the way the app will at its next start or file event.
        var after = AppSettingsFile.Read(path);
        var (nowShowing, _) = CoreThemes.PickActive(themes, after.ThemeId);
        if (nowShowing.Id != wanted.Id)
        {
            var notApplied = $"Saved {wanted.Id}, but app-settings.json reads back as '{after.ThemeId}' ({after.FileState}). "
                             + "Something else wrote it at the same moment; apply again.";
            Audit("not_applied", notApplied);
            return new { ok = false, refusal = "not_applied", detail = notApplied };
        }

        var done = changed ? $"Theme set to {wanted.Name} (was {wasShowing.Name})." : $"{wanted.Name} was already the saved theme.";
        Audit("ok", done);
        return new
        {
            ok = true,
            themeId = wanted.Id,
            name = wanted.Name,
            changed,
            previousThemeId = wasShowing.Id,
            previousSavedThemeId = before.ThemeId,
            // A corrupt file already read as all defaults; saving started it over with just this key.
            settingsFileStartedOver = changed && before.FileState == "unreadable",
            contrastWarnings = CoreThemes.ContrastReport(wanted),
            detail = done,
            hint = "contrastWarnings are advisory, as in the app: the theme applies either way. A running launcher "
                   + "switches when it sees the file change; one that is closed opens on this theme.",
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
