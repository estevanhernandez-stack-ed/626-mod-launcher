using System.Text.Json;
using System.Text.Json.Nodes;

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
/// Reads and writes <c>app-settings.json</c> (camelCase keys). The app and the agent read it through
/// <see cref="Read"/> and write it through <see cref="WriteKey"/>, so an agent's <c>get_app_settings</c>
/// reports what the app is using and its <c>apply_theme</c> saves exactly what a pick in the app saves. Tolerant: one read, one
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
            else { doc = JsonDocument.Parse(ReadShared(path)); state = "ok"; }
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

            var rawBackdrop = Str("backdrop")?.ToLowerInvariant();
            var backdrop = rawBackdrop switch
            {
                "mica" => "mica",
                "acrylic" => "acrylic",
                "solid" => "solid",
                _ => null,
            };
            if (backdrop is null && rawBackdrop is not null) defaulted.Add("backdrop");   // a value the app doesn't know
            backdrop ??= "solid";
            var auto = Bool("autoUpdateDefinitions", true);
            var check = Bool("autoCheckModUpdates", true);
            var plugins = Bool("keepPluginsUpdated", true);
            var tray = Bool("closeToTray", false);
            var rawTheme = Str("themeId");
            var theme = string.IsNullOrWhiteSpace(rawTheme) ? null : rawTheme;   // blank is no saved pick
            if (theme is null && rawTheme is not null) defaulted.Add("themeId");

            return new AppSettingsSnapshot(backdrop, auto, check, plugins, tray, theme, state, defaulted);
        }
    }

    /// <summary>Save ONE key, merged into what is on disk. Never a dump of one caller's memory: the app
    /// and the agent's server are separate processes, and a second launcher window holds its own copy
    /// of every setting, so rewriting them all would put stale values back over another writer's
    /// changes (B1 review: the startup redirect reads closeToTray from this file). A file that is not
    /// JSON reads as all defaults already, so it is started over. A file that can't be opened right now
    /// is NOT started over: that throws, so a sharing clash never wipes every other setting. The read,
    /// merge and write happen under a lock file (as games.json's do), so two writers saving different
    /// keys at once lose neither. Atomic temp-write and rename, so a kill mid-write never truncates it.
    /// Throws when the file can't be read or written, or the lock isn't free within a few seconds.</summary>
    public static void WriteKey(string path, string key, JsonNode? value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        lock (Gate)
        {
            using var held = ModManager.Core.Persistence.FileLock.Acquire(path + ".lock", TimeSpan.FromSeconds(5),
                e => new IOException("Another launcher window is saving its settings. Nothing was changed; try again.", e));
            WriteKeyLocked(path, key, value);
        }
    }

    // In-process half of the lock: a FileShare.None lock file alone does not order two threads of one
    // process on every platform.
    private static readonly object Gate = new();

    private static void WriteKeyLocked(string path, string key, JsonNode? value)
    {
        JsonObject root;
        if (!File.Exists(path)) root = new JsonObject();
        else
        {
            var text = ReadShared(path);
            try { root = JsonNode.Parse(text) as JsonObject ?? new JsonObject(); }
            catch (JsonException) { root = new JsonObject(); }   // corrupt: start over rather than refuse to save
        }
        root[key] = value;

        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tmp, root.ToJsonString());
            // A reader in the other process can hold the file for a moment, which makes the replace
            // fail on Windows. Try a few times before giving up.
            for (var attempt = 1; ; attempt++)
            {
                try { File.Move(tmp, path, overwrite: true); break; }
                catch (IOException) when (attempt < 5) { Thread.Sleep(40 * attempt); }
                catch (UnauthorizedAccessException) when (attempt < 5) { Thread.Sleep(40 * attempt); }
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>The theme a running window should switch to because <c>app-settings.json</c> changed
    /// under it (an agent's <c>apply_theme</c>, or another launcher window): the saved id, when the file
    /// read cleanly and names a different theme from <paramref name="knownSavedId"/>, the id this window
    /// last saved or loaded. Null means leave the window alone. That includes a file caught mid-write,
    /// unreadable or with no theme key, which says nothing about what anyone picked.</summary>
    public static string? ThemeChangedOnDisk(AppSettingsSnapshot onDisk, string? knownSavedId)
        => onDisk.FileState == "ok" && onDisk.ThemeId is { } id && !string.Equals(id, knownSavedId, StringComparison.Ordinal)
            ? id
            : null;

    // Shares read, write and delete, so reading never blocks the other process's rename over the file.
    private static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }
}
