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

    /// <summary>Raised when any setting changes so the shell can re-apply (e.g. swap the backdrop
    /// on the live window).</summary>
    public event EventHandler? BackdropChanged;

    public WindowBackdropKind Backdrop => _backdrop;

    public AppSettingsService()
    {
        Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModManagerBuilder", "app-settings.json");
        _backdrop = Load();
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

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var json = $"{{\"backdrop\":\"{_backdrop.ToString().ToLowerInvariant()}\"}}";
            File.WriteAllText(Path, json);
        }
        catch { /* best-effort persist; in-memory state still holds */ }
    }
}
