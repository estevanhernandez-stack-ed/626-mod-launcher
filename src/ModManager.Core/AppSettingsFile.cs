using System.Text.Json;

namespace ModManager.Core;

/// <summary>The app-level preferences as they stand on disk. <see cref="Backdrop"/> is "solid", "mica"
/// or "acrylic". <see cref="ThemeId"/> is null when nobody has picked a theme. <see cref="FileState"/>
/// is "ok", "missing" or "unreadable", and <see cref="Defaulted"/> names every key that was absent or
/// the wrong type and so reads as its default. Those two say WHY a value is what it is.</summary>
public sealed record AppSettingsSnapshot(
    string Backdrop,
    bool AutoUpdateDefinitions,
    bool AutoCheckModUpdates,
    bool KeepPluginsUpdated,
    bool CloseToTray,
    string? ThemeId,
    string FileState,
    IReadOnlyList<string> Defaulted);

/// <summary>
/// Reads <c>app-settings.json</c> (camelCase keys, written by the app's <c>AppSettingsService</c>).
/// The app and the agent read it through this one function, so an agent's <c>get_app_settings</c>
/// reports what the app is using rather than its own reading of the file. Tolerant: one read, one
/// parse, and each key falls back to its own default on its own (a missing or mistyped key never
/// resets the others). A missing or corrupt file is all defaults. Never throws.
/// </summary>
public static class AppSettingsFile
{
    public const string FileName = "app-settings.json";

    public static string PathIn(string dataRoot) => System.IO.Path.Combine(dataRoot, FileName);

    public static AppSettingsSnapshot Read(string path)
    {
        string state;
        JsonDocument? doc = null;
        try
        {
            if (!File.Exists(path)) state = "missing";
            else { doc = JsonDocument.Parse(File.ReadAllText(path)); state = "ok"; }
        }
        catch { state = "unreadable"; }

        using (doc)
        {
            var root = doc?.RootElement;
            if (state == "ok" && root is not { ValueKind: JsonValueKind.Object }) state = "unreadable";
            var defaulted = new List<string>();

            bool Bool(string key, bool fallback)
            {
                if (root is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty(key, out var v)
                    && v.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return v.GetBoolean();
                defaulted.Add(key);
                return fallback;
            }

            string? Str(string key)
            {
                if (root is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty(key, out var v)
                    && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
                defaulted.Add(key);
                return null;
            }

            var backdrop = Str("backdrop")?.ToLowerInvariant() switch
            {
                "mica" => "mica",
                "acrylic" => "acrylic",
                _ => "solid",
            };
            var auto = Bool("autoUpdateDefinitions", true);
            var check = Bool("autoCheckModUpdates", true);
            var plugins = Bool("keepPluginsUpdated", true);
            var tray = Bool("closeToTray", false);
            var theme = Str("themeId") is { } id && !string.IsNullOrWhiteSpace(id) ? id : null;   // no saved pick

            return new AppSettingsSnapshot(backdrop, auto, check, plugins, tray, theme, state, defaulted);
        }
    }
}
