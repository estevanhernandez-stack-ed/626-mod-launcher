using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModManager.App.Services;

/// <summary>Which Windows backdrop the main window uses. Solid keeps the navy fill (default);
/// Mica is a subtle Windows 11 effect; Acrylic is more translucent.</summary>
public enum WindowBackdropKind { Solid, Mica, Acrylic }

/// <summary>
/// App-level user preferences (the ones that aren't per-game and aren't covered by ThemeService /
/// NexusService / AvatarService). Persisted to <c>%APPDATA%\ModManagerBuilder\app-settings.json</c>.
/// Tolerant load — a missing or corrupt file resolves to defaults, never throws.
/// </summary>
public sealed class AppSettingsService
{
    public string Path { get; }

    private WindowBackdropKind _backdrop;

    private bool _autoUpdateDefinitions;

    private bool _autoCheckModUpdates;

    private bool _keepPluginsUpdated;

    /// <summary>Raised when any setting changes so the shell can re-apply (e.g. swap the backdrop
    /// on the live window).</summary>
    public event EventHandler? BackdropChanged;

    public WindowBackdropKind Backdrop => _backdrop;

    /// <summary>Whether the launcher fetches + applies remote game-definition updates (default on).
    /// When off, the embedded manifest is used and no manifest fetch occurs.</summary>
    public bool AutoUpdateDefinitions => _autoUpdateDefinitions;

    public void SetAutoUpdateDefinitions(bool enabled)
    {
        if (_autoUpdateDefinitions == enabled) return;
        _autoUpdateDefinitions = enabled;
        Save("autoUpdateDefinitions", enabled);
    }

    /// <summary>Whether the launcher polls Nexus by mod id on game load to flag mods with a newer
    /// version available (default on). When off, no auto-check runs — the manual "Refresh Nexus
    /// stats" action still works.</summary>
    public bool AutoCheckModUpdates => _autoCheckModUpdates;

    public void SetAutoCheckModUpdates(bool enabled)
    {
        if (_autoCheckModUpdates == enabled) return;
        _autoCheckModUpdates = enabled;
        Save("autoCheckModUpdates", enabled);
    }

    /// <summary>Whether the launcher auto-updates installed off-Store plugins on a 24h debounce
    /// (default on). The first install on Nexus connect happens regardless; this gates re-checks.</summary>
    public bool KeepPluginsUpdated => _keepPluginsUpdated;

    public void SetKeepPluginsUpdated(bool enabled)
    {
        if (_keepPluginsUpdated == enabled) return;
        _keepPluginsUpdated = enabled;
        Save("keepPluginsUpdated", enabled);
    }

    /// <summary>Whether closing the window keeps the launcher running in the notification area (B1,
    /// default off: closing means closing until the user says otherwise). The tray icon is shown
    /// for as long as this is on, so Quit is always one right-click away.</summary>
    public bool CloseToTray => _closeToTray;

    private bool _closeToTray;

    /// <summary>Raised when <see cref="CloseToTray"/> changes, so the shell can add or remove the
    /// tray icon on the live window.</summary>
    public event EventHandler? CloseToTrayChanged;

    public void SetCloseToTray(bool enabled)
    {
        if (_closeToTray == enabled) return;
        _closeToTray = enabled;
        Save("closeToTray", enabled);
        CloseToTrayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The last theme the user picked, restored at launch (F-080). Null means no pick
    /// has ever been saved — the shell falls back to ThemeService.Default (the flagship).</summary>
    public string? ThemeId => _themeId;

    private string? _themeId;

    public void SetThemeId(string id)
    {
        if (_themeId == id) return;
        _themeId = id;
        Save("themeId", id);
    }

    public AppSettingsService()
    {
        Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModManagerBuilder", "app-settings.json");

        // One read, one parse; each key then falls back to its own default on its own (a missing or
        // mistyped key never resets the others). A missing or corrupt file is all defaults.
        using var doc = TryParse(Path);
        var root = doc?.RootElement;
        _backdrop = ReadString(root, "backdrop")?.ToLowerInvariant() switch
        {
            "mica"    => WindowBackdropKind.Mica,
            "acrylic" => WindowBackdropKind.Acrylic,
            _         => WindowBackdropKind.Solid,
        };
        _autoUpdateDefinitions = ReadBool(root, "autoUpdateDefinitions", true);
        _autoCheckModUpdates = ReadBool(root, "autoCheckModUpdates", true);
        _keepPluginsUpdated = ReadBool(root, "keepPluginsUpdated", true);
        _closeToTray = ReadBool(root, "closeToTray", false);
        _themeId = ReadString(root, "themeId") is { } id && !string.IsNullOrWhiteSpace(id) ? id : null;   // no saved pick
    }

    public void SetBackdrop(WindowBackdropKind kind)
    {
        if (_backdrop == kind) return;
        _backdrop = kind;
        Save("backdrop", kind.ToString().ToLowerInvariant());
        BackdropChanged?.Invoke(this, EventArgs.Empty);
    }

    private static JsonDocument? TryParse(string path)
    {
        try { return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null; }
        catch { return null; }   // corrupt — defaults
    }

    private static bool ReadBool(JsonElement? root, string key, bool fallback)
        => root is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty(key, out var v)
           && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : fallback;

    private static string? ReadString(JsonElement? root, string key)
        => root is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty(key, out var v)
           && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private void Save(string key, JsonNode? value)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            // Merge the ONE changed key into what is on disk, never a dump of this instance's memory:
            // a second launcher window holds its own copy of every setting, and rewriting them all
            // would put back its stale values over the other window's changes (B1 review — the
            // startup redirect reads closeToTray from this file). Keys stay camelCase, written here.
            JsonObject root;
            try { root = (File.Exists(Path) ? JsonNode.Parse(File.ReadAllText(Path)) as JsonObject : null) ?? new JsonObject(); }
            catch { root = new JsonObject(); }   // corrupt — start over rather than refuse to save
            root[key] = value;
            var json = root.ToJsonString();
            // Atomic temp-write + rename (file-op law): theme picks made this write frequent,
            // and a kill mid-WriteAllText would truncate the file and silently reset every toggle.
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, Path, overwrite: true);
        }
        catch { /* best-effort persist; in-memory state still holds */ }
    }
}
