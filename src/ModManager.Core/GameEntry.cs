namespace ModManager.Core;

/// <summary>One mod-file location under a game root (name/label/relative path + optional mirrors).</summary>
public sealed record ModLocation(string Name, string Label, string Path)
{
    public IReadOnlyList<string> Mirrors { get; init; } = Array.Empty<string>();

    // How mods are shaped in this folder: "files" (pak files grouped by filename — the default) or
    // "folders" (one folder per mod, e.g. UE4SS Lua/script mods). Null falls back to the game's
    // grouping rule ("by_folder" -> folders, else files).
    public string? Form { get; init; }

    // When set (e.g. "vortex" / "mo2"), another tool owns this folder: surface its mods read-only
    // and never toggle/uninstall/move them. Honors the "don't touch Vortex-managed folders" law.
    public string? Managed { get; init; }
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

    // relative path (under GameRoot) to the launcher that must be used when modded (e.g. Seamless Co-op)
    public string? RequiredLauncher { get; set; }

    // Save/world-mod install layout, relative to <saveDir>\<profile> (e.g. "RocksDB/{version}/Worlds");
    // null falls back to the built-in RocksDB default. SaveModForbidden lists save subfolders the
    // installer must NEVER write (game-managed, e.g. RocksDB_v2) — merged with built-in defaults.
    public string? SaveModPath { get; set; }
    public IReadOnlyList<string> SaveModForbidden { get; set; } = Array.Empty<string>();

    // Nexus Mods game key — a domain NAME ("windrose"), not a numeric id (how Nexus keys games)
    public string? NexusGameDomain { get; set; }

    // auto-backup-before-launch opt-in + how many auto snapshots to retain (null = unlimited)
    public bool AutoBackupOnLaunch { get; set; }
    public int? SaveAutoKeep { get; set; } = 25;

    // Steam buildid recorded the last time we saw this game (the "modded against" baseline). When the
    // live Steam build later differs, the launcher warns that an update may have broken installed mods.
    // Null = no baseline yet (e.g. an old registry) = no warning.
    public string? LastKnownSteamBuildId { get; set; }
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
    public string? SaveRoot { get; init; }
    public string? SaveSubPath { get; init; }
    public string? RequiredLauncher { get; init; }
    public int? CurseforgeGameId { get; init; }
    public string? SaveModPath { get; init; }
    public IReadOnlyList<string>? SaveModForbidden { get; init; }
    public string? NexusGameDomain { get; init; }
}

/// <summary>An engine preset: the mod-folder layout / extensions / grouping for an engine.</summary>
public sealed record EnginePreset(
    string Label,
    IReadOnlyList<string> FileExtensions,
    string GroupingRule,
    string ModPath,
    string Hint);
