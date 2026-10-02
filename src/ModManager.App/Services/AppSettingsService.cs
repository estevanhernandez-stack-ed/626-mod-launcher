using System.IO;
using System.Text.Json.Nodes;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>Which Windows backdrop the main window uses. Solid keeps the navy fill (default);
/// Mica is a subtle Windows 11 effect; Acrylic is more translucent.</summary>
public enum WindowBackdropKind { Solid, Mica, Acrylic }

/// <summary>
/// App-level user preferences (the ones that aren't per-game and aren't covered by ThemeService /
/// NexusService / AvatarService). Persisted to <c>%APPDATA%\ModManagerBuilder\app-settings.json</c>.
/// Tolerant load — a missing or corrupt file resolves to defaults, never throws.
/// </summary>
public sealed class AppSettingsService : IDisposable
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
    public string? ThemeId { get { lock (_themeGate) return _themeId; } }

    private string? _themeId;

    // The watcher thread and the UI thread both touch _themeId.
    private readonly object _themeGate = new();

    public void SetThemeId(string id)
    {
        // Saved under the same gate the watcher reads under, and _themeId moves only once the save
        // has landed. So the watcher never sees this window's own pick as an outside change (it reads
        // either the old file with the old id or the new file with the new id), and a save that
        // failed doesn't later look like someone else putting the old theme back.
        lock (_themeGate)
        {
            if (_themeId == id) return;
            try { AppSettingsFile.WriteKey(Path, "themeId", id); }
            catch { return; /* best-effort persist, as every setting: the window still shows the pick */ }
            _themeId = id;
        }
    }

    /// <summary>Raised, on a background thread, when app-settings.json starts naming a different theme
    /// from this window's: an agent's apply_theme, or a pick in another launcher window. The shell
    /// switches to <see cref="ThemeId"/> (read when it handles this, not when it was raised, so a pick
    /// made in between wins) so the user sees the change as it happens (agent-access law 10).</summary>
    public event EventHandler? ThemeSavedElsewhere;

    private FileSystemWatcher? _watcher;

    /// <summary>Watch app-settings.json for theme picks made outside this window. Called once by the
    /// shell, not by the constructor, so short-lived instances never leave watchers behind.</summary>
    public void WatchForOutsideChanges()
    {
        if (_watcher is not null) return;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, System.IO.Path.GetFileName(Path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            // Writers rename a temp file over it, which raises Renamed; an editor raises Changed.
            _watcher.Changed += OnSettingsFileEvent;
            _watcher.Created += OnSettingsFileEvent;
            _watcher.Renamed += OnSettingsFileEvent;
            _watcher.EnableRaisingEvents = true;
        }
        catch { _watcher = null; /* best-effort: without it, an outside pick shows at next start */ }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnSettingsFileEvent(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Core decides (and tests) what counts: a clean read naming a theme other than this
            // window's. Read and compared under the gate SetThemeId saves under (see there).
            lock (_themeGate)
            {
                var changed = AppSettingsFile.ThemeChangedOnDisk(AppSettingsFile.Read(Path), _themeId);
                if (changed is null) return;
                _themeId = changed;
            }
            ThemeSavedElsewhere?.Invoke(this, EventArgs.Empty);
        }
        catch { /* a watcher callback must never take the app down */ }
    }

    public AppSettingsService()
    {
        Path = AppSettingsFile.PathIn(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModManagerBuilder"));

        // Core reads the file (one parse, per-key defaults), so the agent's get_app_settings reports
        // exactly what this instance starts from.
        var snapshot = AppSettingsFile.Read(Path);
        _backdrop = snapshot.Backdrop switch
        {
            "mica"    => WindowBackdropKind.Mica,
            "acrylic" => WindowBackdropKind.Acrylic,
            _         => WindowBackdropKind.Solid,
        };
        _autoUpdateDefinitions = snapshot.AutoUpdateDefinitions;
        _autoCheckModUpdates = snapshot.AutoCheckModUpdates;
        _keepPluginsUpdated = snapshot.KeepPluginsUpdated;
        _closeToTray = snapshot.CloseToTray;
        _themeId = snapshot.ThemeId;
    }

    public void SetBackdrop(WindowBackdropKind kind)
    {
        if (_backdrop == kind) return;
        _backdrop = kind;
        Save("backdrop", kind.ToString().ToLowerInvariant());
        BackdropChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Save(string key, JsonNode? value)
    {
        // Core merges the ONE changed key into what is on disk (never this instance's memory, which a
        // second window or the agent's server may have moved past) with an atomic temp+rename: theme
        // picks made this write frequent, and a kill mid-write would reset every toggle. Keys stay
        // camelCase, named by each setter.
        try { AppSettingsFile.WriteKey(Path, key, value); }
        catch { /* best-effort persist (a busy lock or an unwritable file); in-memory state still holds */ }
    }
}
