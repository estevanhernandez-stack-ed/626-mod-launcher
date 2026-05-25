namespace ModManager.Core;

/// <summary>One mod-file location under a game root (name/label/relative path + optional mirrors).</summary>
public sealed record ModLocation(string Name, string Label, string Path)
{
    public IReadOnlyList<string> Mirrors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// One way to start a game. <see cref="Kind"/> is "steam" (Target is a steam:// url) or "exe"
/// (Target is an executable path, optionally with <see cref="Args"/> + <see cref="WorkingDir"/>).
/// FromSoft games get a Mod Engine 2 target so mods actually load; alt-launcher mods (Seamless
/// Co-op) get their own. The default target is the primary Launch action.
/// </summary>
public sealed record LaunchTarget(string Label, string Kind, string Target)
{
    public string? Args { get; init; }
    public string? WorkingDir { get; init; }
    public bool IsDefault { get; init; }
}

/// <summary>A registered game: where it lives, how its mods are shaped, how to launch it.</summary>
public sealed class GameEntry
{
    public string Id { get; set; } = "";
    public string GameName { get; set; } = "";
    public string? Engine { get; set; }
    public string? WindowTitle { get; set; }
    public string GameRoot { get; set; } = "";
    public IReadOnlyList<string> FileExtensions { get; set; } = Array.Empty<string>();
    public string GroupingRule { get; set; } = "";
    public IReadOnlyList<ModLocation> ModLocations { get; set; } = Array.Empty<ModLocation>();
    public string? SteamAppId { get; set; }
    public string? LaunchUrl { get; set; }
    public string? LaunchExe { get; set; }

    // ways to start the game (vanilla + modded/alt launchers); empty falls back to LaunchUrl/exe
    public IReadOnlyList<LaunchTarget> LaunchTargets { get; set; } = Array.Empty<LaunchTarget>();
    // absolute path to the active Mod Engine 2 config toml, when one was detected (FromSoft)
    public string? ModEngineConfig { get; set; }

    // mod-data placement + metadata resolution
    public string? DataDir { get; set; }
    public int? CurseforgeGameId { get; set; }
    public string? ScanSubfolders { get; set; }

    // where this game's saves live (for the built-in save manager)
    public string? SaveDir { get; set; }

    // auto-backup-before-launch opt-in + how many auto snapshots to retain (null = unlimited)
    public bool AutoBackupOnLaunch { get; set; }
    public int? SaveAutoKeep { get; set; } = 25;
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
