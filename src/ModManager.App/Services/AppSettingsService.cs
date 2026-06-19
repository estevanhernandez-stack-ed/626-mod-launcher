using System.IO;
using System.Text.Json;

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
        Save();
    }

    /// <summary>Whether the launcher polls Nexus by mod id on game load to flag mods with a newer
    /// version available (default on). When off, no auto-check runs — the manual "Refresh Nexus
    /// stats" action still works.</summary>
    public bool AutoCheckModUpdates => _autoCheckModUpdates;

    public void SetAutoCheckModUpdates(bool enabled)
    {
        if (_autoCheckModUpdates == enabled) return;
        _autoCheckModUpdates = enabled;
        Save();
    }

    /// <summary>Whether the launcher auto-updates installed off-Store plugins on a 24h debounce
    /// (default on). The first install on Nexus connect happens regardless; this gates re-checks.</summary>
    public bool KeepPluginsUpdated => _keepPluginsUpdated;

    public void SetKeepPluginsUpdated(bool enabled)
    {
        if (_keepPluginsUpdated == enabled) return;
        _keepPluginsUpdated = enabled;
        Save();
    }

    public AppSettingsService()
    {
        Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModManagerBuilder", "app-settings.json");
        _backdrop = Load();
        _autoUpdateDefinitions = LoadAutoUpdate();
        _autoCheckModUpdates = LoadAutoCheckModUpdates();
        _keepPluginsUpdated = LoadKeepPluginsUpdated();
    }

    public void SetBackdrop(WindowBackdropKind kind)
    {
        if (_backdrop == kind) return;
        _backdrop = kind;
        Save();
        BackdropChanged?.Invoke(this, EventArgs.Empty);
    }

    private WindowBackdropKind Load()
    {
        try
        {
            if (!File.Exists(Path)) return WindowBackdropKind.Solid;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path));
            if (doc.RootElement.TryGetProperty("backdrop", out var b) && b.ValueKind == JsonValueKind.String)
            {
                return b.GetString()?.ToLowerInvariant() switch
                {
                    "mica"    => WindowBackdropKind.Mica,
                    "acrylic" => WindowBackdropKind.Acrylic,
                    _         => WindowBackdropKind.Solid,
                };
            }
        }
        catch { /* missing / corrupt — default */ }
        return WindowBackdropKind.Solid;
    }

    private bool LoadAutoUpdate()
    {
        try
        {
            if (!File.Exists(Path)) return true;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path));
            if (doc.RootElement.TryGetProperty("autoUpdateDefinitions", out var v)
                && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
                return v.GetBoolean();
        }
        catch { /* missing / corrupt — default on */ }
        return true;
    }

    private bool LoadAutoCheckModUpdates()
    {
        try
        {
            if (!File.Exists(Path)) return true;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path));
            if (doc.RootElement.TryGetProperty("autoCheckModUpdates", out var v)
                && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
                return v.GetBoolean();
        }
        catch { /* missing / corrupt — default on */ }
        return true;
    }

    private bool LoadKeepPluginsUpdated()
    {
        try
        {
            if (!File.Exists(Path)) return true;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path));
            if (doc.RootElement.TryGetProperty("keepPluginsUpdated", out var v)
                && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
                return v.GetBoolean();
        }
        catch { /* missing / corrupt — default on */ }
        return true;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var json =
                $"{{\"backdrop\":\"{_backdrop.ToString().ToLowerInvariant()}\","
                + $"\"autoUpdateDefinitions\":{(_autoUpdateDefinitions ? "true" : "false")},"
                + $"\"autoCheckModUpdates\":{(_autoCheckModUpdates ? "true" : "false")},"
                + $"\"keepPluginsUpdated\":{(_keepPluginsUpdated ? "true" : "false")}}}";
            File.WriteAllText(Path, json);
        }
        catch { /* best-effort persist; in-memory state still holds */ }
    }
}
