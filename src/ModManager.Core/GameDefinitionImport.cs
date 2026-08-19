using System.Text.Json;

namespace ModManager.Core;

/// <summary>An agent-authored game DEFINITION, parsed. All paths are relative/enum; the App resolves
/// them. Was GameProfileDraft until wave 10, when "profile" was found naming three different things:
/// this, a saved set of enabled mods, and an engine's save types. The launcher already had a word
/// for this one - the game-definition layer under Manifest/ and the signed feed it fetches.</summary>
public sealed record GameDefinitionDraft(
    string? Name, string? Engine, string? WindowTitle, string? SteamAppId,
    string? ModPath, IReadOnlyList<string>? FileExtensions, string? GroupingRule,
    string? SaveRoot, string? SaveSubPath, string? RequiredLauncher, int? CurseforgeGameId,
    string? SaveModPath = null, IReadOnlyList<string>? SaveModForbidden = null,
    string? NexusGameDomain = null);

/// <summary>Result of loading a definition: a Draft (non-null only when Errors is empty) plus any errors.</summary>
public sealed record GameDefinitionImportResult(GameDefinitionDraft? Draft, IReadOnlyList<string> Errors);

/// <summary>
/// Parses + validates an agent-authored game definition. Mirrors the Themes.NormalizeTheme contract:
/// bad input is rejected with reasons, never half-applied.
/// </summary>
public static class GameDefinitionImport
{
    public static readonly IReadOnlyList<string> SaveRoots =
        new[] { "DocumentsMyGames", "AppData", "LocalAppData", "SteamUserData", "GameInstall" };

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Parses + validates a JSON array of profiles. One result per element (good or bad);
    /// a single bad row doesn't poison the rest. Returns a single error result when the root is not
    /// a valid JSON array.</summary>
    public static IReadOnlyList<GameDefinitionImportResult> LoadMany(string json)
    {
        List<string> elements;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new[] { new GameDefinitionImportResult(null, new[] { "Expected a JSON array of profiles." }) };
            // Clone each element to its own owned JSON string before the doc is disposed.
            elements = doc.RootElement.EnumerateArray().Select(e => e.GetRawText()).ToList();
        }
        catch (JsonException e)
        {
            return new[] { new GameDefinitionImportResult(null, new[] { "Not valid JSON: " + e.Message }) };
        }
        return elements.Select(Load).ToList();
    }

    public static GameDefinitionImportResult Load(string json)
    {
        GameDefinitionDraft? d;
        try { d = JsonSerializer.Deserialize<GameDefinitionDraft>(json, Opts); }
        catch (JsonException e) { return new GameDefinitionImportResult(null, new[] { "Not valid JSON: " + e.Message }); }
        if (d is null) return new GameDefinitionImportResult(null, new[] { "Empty profile." });

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

        return new GameDefinitionImportResult(errors.Count == 0 ? d : null, errors);
    }

    // Relative + safe: no drive root (C:\), no rooted slash, no '..' or empty segment.
    //
    // Distinct from PathGate.SafeRelative — kept separate intentionally.
    // PathGate.SafeRelative is designed for archive entry names: it strips a leading '/' because
    // some zip tools emit entries with a leading slash, making "/foo" safe (it becomes "foo").
    // Here we validate user-typed profile path strings — a leading '/' means the user wrote an
    // absolute path, which should be rejected, not silently accepted. Delegating to
    // PathGate.SafeRelative would flip the behavior for that edge case. The segment rules
    // (no '..', no empty, no drive-root) are identical to PathGate.SafeRelative's.
    private static bool IsSafeRelative(string p)
    {
        var n = p.Replace('\\', '/').Trim();
        if (n.Length == 0) return false;
        if (n.StartsWith('/')) return false;                 // rooted — intentionally rejected here (see above)
        if (n.Length > 1 && n[1] == ':') return false;       // drive-rooted (C:...)
        return !n.Split('/').Any(s => s is "" or "." or "..");
    }
}
