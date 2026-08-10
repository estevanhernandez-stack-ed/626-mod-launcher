using System.Text.Json.Serialization;

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

    // Where this game came from ("steam"|"gog"|"epic"|"xbox"|"manual"); null for older registries.
    public string? StoreSource { get; set; }

    // The last time 626 (or a recency source) recorded this game being launched; null = unknown.
    public DateTime? LastLaunchedUtc { get; set; }

    /// <summary>
    /// Field names the user set DELIBERATELY, as their camelCase json names.
    ///
    /// <para>A stored value is ambiguous on its own: <c>["pak"]</c> might be a choice, or the engine
    /// preset's default frozen in on the day the game was added. <see cref="RegistrationRefresh"/>
    /// guesses between those with an untouched-preset-default heuristic, which is right for every
    /// registration measured so far but cannot distinguish a deliberate choice that HAPPENS to equal
    /// the default. This marker removes the guess for anything the user actually edits.</para>
    ///
    /// <para>Null means "not recorded", not "nothing is user-set" — every registration written before
    /// the edit surface existed has no key, and must keep behaving exactly as it does today. That
    /// back-compat is why this needed no migration, and why it was not worth adding during A1.</para>
    ///
    /// <para>Recorded for EVERY edited field; consulted today only for the two that self-heal. An
    /// entry no rule reads yet is inert — a fact waiting for a reader. Whoever adds the next
    /// self-healing field: check this before trusting the heuristic.</para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? UserSet { get; set; }

    /// <summary>
    /// A field-for-field copy of this entry.
    ///
    /// <para>WHY THIS EXISTS AND WHY IT IS <c>MemberwiseClone</c>. An editor that proposes a change
    /// has to hand the planner a WHOLE entry — the fields the user typed plus every field they did
    /// not — and the obvious way to build one is a hand-written object initialiser naming all 27
    /// properties. That is correct exactly once: the 28th property added later is silently dropped
    /// from every registration edit, so renaming a game would quietly clear its
    /// <c>nexusGameDomain</c> or its <c>autoBackupOnLaunch</c>, with no compiler error and no failing
    /// test. This branch already caught that bug once in a test helper. <c>MemberwiseClone</c> copies
    /// whatever the type has, so a new field cannot be forgotten.</para>
    ///
    /// <para>SHALLOW, and that is fine here: every collection property on this type is an
    /// <c>IReadOnlyList</c> that callers REPLACE rather than mutate in place, so the copy sharing a
    /// list instance with the original is not observable. Anything that starts mutating one in place
    /// has to revisit this.</para>
    /// </summary>
    public GameEntry CloneShallow() => (GameEntry)MemberwiseClone();

    // The json field names, as constants, so a marker is never a typo that silently matches nothing.
    public const string UserSetFileExtensions = "fileExtensions";
    public const string UserSetGroupingRule = "groupingRule";
    public const string UserSetModLocations = "modLocations";
    public const string UserSetGameRoot = "gameRoot";

    // Field names for changes that are real but carry no pin — see RegistrationChangePlan.OtherChanges.
    // The UserSet* constants above are field names too; they are the PINNABLE subset, and they carry
    // that prefix because they are also what gets written into UserSet.
    public const string FieldGameName = "gameName";
    public const string FieldEngine = "engine";
    public const string FieldSteamAppId = "steamAppId";
    public const string FieldRequiredLauncher = "requiredLauncher";
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
