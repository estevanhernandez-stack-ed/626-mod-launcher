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

    /// <summary>What <see cref="WriteKey"/> found and did: the key's value before (null when absent),
    /// whether it wrote (an unchanged value is not rewritten), and whether it started the file over
    /// because it wasn't JSON. Read under the same lock as the write, so it is what the write replaced.</summary>
    public sealed record KeyWrite(JsonNode? Previous, bool Written, bool StartedOver);

    /// <summary>How long a writer waits for another (the other process, or another thread here) before
    /// giving up and writing nothing. Short, because the app saves on the UI thread.</summary>
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Save ONE key, merged into what is on disk. Never a dump of one caller's memory: the app
    /// and the agent's server are separate processes, and a second launcher window holds its own copy
    /// of every setting, so rewriting them all would put stale values back over another writer's
    /// changes (B1 review: the startup redirect reads closeToTray from this file). A file that is not
    /// JSON reads as all defaults already, so it is started over. A file that can't be opened right now
    /// is NOT started over: that throws, so a sharing clash never wipes every other setting. A key
    /// written twice by hand reads last-wins, as <see cref="Read"/> reads it. Done under
    /// <see cref="WithLock"/>, so two writers saving different keys at once lose neither, with an atomic
    /// temp-write and rename, so a kill mid-write never truncates it. Throws when the file can't be read
    /// or written, or the lock isn't free within <see cref="LockTimeout"/>.</summary>
    public static KeyWrite WriteKey(string path, string key, JsonNode? value)
    {
        KeyWrite result = null!;
        WithLock(path, () => result = WriteKeyLocked(path, key, value));
        return result;
    }

    /// <summary>Run <paramref name="action"/> holding the app-settings lock: the in-process gate and the
    /// lock file beside the file, so another launcher window or the agent's server waits too. For
    /// anything that replaces or deletes the file outside <see cref="WriteKey"/> (restore points).
    /// Bounded by <see cref="LockTimeout"/>; past it, throws and runs nothing.</summary>
    public static void WithLock(string path, Action action)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var deadline = DateTime.UtcNow + LockTimeout;
        if (!Monitor.TryEnter(Gate, LockTimeout)) throw Busy(null);
        try
        {
            var left = deadline - DateTime.UtcNow;
            using var held = ModManager.Core.Persistence.FileLock.Acquire(path + ".lock",
                left > TimeSpan.Zero ? left : TimeSpan.Zero, Busy);
            action();
        }
        finally { Monitor.Exit(Gate); }
    }

    private static IOException Busy(Exception? inner)
        => new("Another launcher window is saving its settings. Nothing was changed; try again.", inner);

    // In-process half of the lock: a FileShare.None lock file alone does not order two threads of one
    // process on every platform.
    private static readonly object Gate = new();

    private static KeyWrite WriteKeyLocked(string path, string key, JsonNode? value)
    {
        JsonObject root;
        var startedOver = false;
        if (!File.Exists(path)) root = new JsonObject();
        else
        {
            var text = ReadShared(path);
            try { root = ParseLastWins(text); }
            catch (JsonException) { root = new JsonObject(); startedOver = true; }   // corrupt: start over rather than refuse to save
        }

        var previous = root[key]?.DeepClone();
        if (!startedOver && root.ContainsKey(key) && JsonNode.DeepEquals(previous, value))
            return new KeyWrite(previous, Written: false, StartedOver: false);
        root[key] = value?.DeepClone();

        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tmp, root.ToJsonString());
            // A reader in the other process can hold the file for a moment, which makes the replace
            // fail on Windows. Try a few times before giving up.
            for (var attempt = 1; ; attempt++)
            {
                try { File.Move(tmp, path, overwrite: true); break; }
                catch (IOException) when (attempt < 4) { Thread.Sleep(30 * attempt); }
                catch (UnauthorizedAccessException) when (attempt < 4) { Thread.Sleep(30 * attempt); }
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
        return new KeyWrite(previous, Written: true, StartedOver: startedOver);
    }

    // The settings object rebuilt property by property, a later duplicate replacing an earlier one.
    // JsonNode.Parse accepts a duplicate key but throws on first touch of the object, which would make
    // every save fail on a hand-edited file that Read reads fine. Not an object counts as not JSON.
    private static JsonObject ParseLastWins(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("app-settings.json is not a JSON object.");
        var root = new JsonObject();
        foreach (var prop in doc.RootElement.EnumerateObject())
            root[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
        return root;
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
