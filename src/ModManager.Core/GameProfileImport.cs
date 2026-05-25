using System.Text.Json;

namespace ModManager.Core;

/// <summary>An agent-authored game profile, parsed. All paths are relative/enum; the App resolves them.</summary>
public sealed record GameProfileDraft(
    string? Name, string? Engine, string? WindowTitle, string? SteamAppId,
    string? ModPath, IReadOnlyList<string>? FileExtensions, string? GroupingRule,
    string? SaveRoot, string? SaveSubPath, string? RequiredLauncher, int? CurseforgeGameId);

/// <summary>Result of loading a profile: a Draft (non-null only when Errors is empty) plus any errors.</summary>
public sealed record ProfileImportResult(GameProfileDraft? Draft, IReadOnlyList<string> Errors);

/// <summary>
/// Parses + validates an agent-authored game profile. Mirrors the Themes.NormalizeTheme contract:
/// bad input is rejected with reasons, never half-applied.
/// </summary>
public static class GameProfileImport
{
    public static readonly IReadOnlyList<string> SaveRoots =
        new[] { "DocumentsMyGames", "AppData", "LocalAppData", "SteamUserData", "GameInstall" };

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static ProfileImportResult Load(string json)
    {
        GameProfileDraft? d;
        try { d = JsonSerializer.Deserialize<GameProfileDraft>(json, Opts); }
        catch (JsonException e) { return new ProfileImportResult(null, new[] { "Not valid JSON: " + e.Message }); }
        if (d is null) return new ProfileImportResult(null, new[] { "Empty profile." });

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Name)) errors.Add("Missing required field: name.");
        if (string.IsNullOrWhiteSpace(d.Engine)) errors.Add("Missing required field: engine.");
        else if (!EnginePresets.Presets.ContainsKey(d.Engine))
            errors.Add($"Unknown engine '{d.Engine}'. Allowed: {string.Join(", ", EnginePresets.Presets.Keys)}.");
        if (string.IsNullOrWhiteSpace(d.SaveRoot)) errors.Add("Missing required field: saveRoot.");
        else if (!SaveRoots.Contains(d.SaveRoot))
            errors.Add($"Unknown saveRoot '{d.SaveRoot}'. Allowed: {string.Join(", ", SaveRoots)}.");
        if (string.IsNullOrWhiteSpace(d.SaveSubPath)) errors.Add("Missing required field: saveSubPath.");

        foreach (var (label, path) in new[] { ("modPath", d.ModPath), ("saveSubPath", d.SaveSubPath), ("requiredLauncher", d.RequiredLauncher) })
            if (!string.IsNullOrEmpty(path) && !IsSafeRelative(path!))
                errors.Add($"The {label} path must be relative (no absolute path, drive root, or '..'): {path}");

        if (!string.IsNullOrEmpty(d.SteamAppId) && !d.SteamAppId.All(char.IsDigit))
            errors.Add($"steamAppId must be digits only: {d.SteamAppId}");

        return new ProfileImportResult(errors.Count == 0 ? d : null, errors);
    }

    // Relative + safe: no drive root (C:\), no rooted slash, no '..' or empty segment.
    private static bool IsSafeRelative(string p)
    {
        var n = p.Replace('\\', '/').Trim();
        if (n.Length == 0) return false;
        if (n.StartsWith('/')) return false;                 // rooted
        if (n.Length > 1 && n[1] == ':') return false;       // drive-rooted (C:...)
        return !n.Split('/').Any(s => s is "" or "." or "..");
    }
}
