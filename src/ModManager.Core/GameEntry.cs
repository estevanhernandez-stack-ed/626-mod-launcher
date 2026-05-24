namespace ModManager.Core;

/// <summary>One mod-file location under a game root (name/label/relative path + optional mirrors).</summary>
public sealed record ModLocation(string Name, string Label, string Path)
{
    public IReadOnlyList<string> Mirrors { get; init; } = Array.Empty<string>();
}

/// <summary>A registered game: where it lives, how its mods are shaped, how to launch it.</summary>
public sealed class GameEntry
{
    public string Id { get; set; } = "";
    public string GameName { get; set; } = "";
    public string? WindowTitle { get; set; }
    public string GameRoot { get; set; } = "";
    public IReadOnlyList<string> FileExtensions { get; set; } = Array.Empty<string>();
    public string GroupingRule { get; set; } = "";
    public IReadOnlyList<ModLocation> ModLocations { get; set; } = Array.Empty<ModLocation>();
    public string? SteamAppId { get; set; }
    public string? LaunchUrl { get; set; }
    public string? LaunchExe { get; set; }

    // mod-data placement + metadata resolution
    public string? DataDir { get; set; }
    public int? CurseforgeGameId { get; set; }
    public string? ScanSubfolders { get; set; }
}

/// <summary>The persisted registry of games plus the active selection.</summary>
public sealed class GameRegistry
{
    public int Version { get; set; } = 1;
    public string? ActiveGameId { get; set; }
    public List<GameEntry> Games { get; set; } = new();
}

/// <summary>Wizard input for assembling a <see cref="GameEntry"/>.</summary>
public sealed class GameInput
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Engine { get; init; }
    public string? GameRoot { get; init; }
    public string? SteamAppId { get; init; }
    public string? LaunchExe { get; init; }
    public string? ModPath { get; init; }
    public string? WindowTitle { get; init; }
    public IReadOnlyList<string>? FileExtensions { get; init; }
    public string? GroupingRule { get; init; }
}

/// <summary>An engine preset: the mod-folder layout / extensions / grouping for an engine.</summary>
public sealed record EnginePreset(
    string Label,
    IReadOnlyList<string> FileExtensions,
    string GroupingRule,
    string ModPath,
    string Hint);
