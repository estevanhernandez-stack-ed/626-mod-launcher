namespace ModManager.Core.Discovery;

/// <summary>Why a path was claimed as a possible mod. Drives how it is matched later:
/// archives can be md5-identified; the rest fall to name matching.</summary>
public enum DiscoveryKind { Signature, EngineShaped, Archive }

/// <summary>One thing the sweep claims might be a mod. Paths are RELATIVE to the swept root,
/// so Core never sees an absolute path (pure-core law).</summary>
public sealed record DiscoveryCandidate(string RelativePath, string FileName, DiscoveryKind Kind);

/// <summary>A file the caller enumerated during the sweep, relative to the swept root, with its
/// size in bytes. Size is needed to tell a base-game pak from a mod pak
/// (<see cref="ModManager.Core.PakClassifier.IsBaseGamePak"/>) — Core never touches disk, so the
/// caller (<c>DiscoveryScanService</c>) supplies it up front alongside the path.</summary>
public sealed record SweptFile(string RelativePath, long Size);

/// <summary>One of a game's configured mod folders, relative to the swept root.
/// <paramref name="PaksRoot"/> marks a loader-less UE-pak location
/// (<c>ModLocation.Form == "paks-root"</c>, e.g. Witchfire) where the mod folder IS
/// <c>Content/Paks</c> itself — the game's own shipped paks live in the SAME folder as any mod.
/// Classification there must additionally clear <see cref="ModManager.Core.PakClassifier.IsBaseGamePak"/>
/// or it claims the base game. Every other form (a dedicated <c>~mods</c>/<c>LogicMods</c>/<c>Mods</c>
/// folder) never mixes base-game files in, so <paramref name="PaksRoot"/> is false there.</summary>
public sealed record DiscoverySweepModPath(string Path, bool PaksRoot);

/// <summary>What the classifier needs to know about this game. Supplied by the App from the
/// effective manifest plus the game's registered mod locations; Core never reads either here.
/// <paramref name="ModPaths"/> is EVERY configured mod location (a UE4SS game can have both
/// <c>~mods</c> and <c>LogicMods</c> at once) — a candidate is engine-shaped if it sits under ANY
/// of them.</summary>
public sealed record DiscoverySweepOptions(
    IReadOnlyList<DiscoverySweepModPath> ModPaths,
    IReadOnlyList<string> EngineExtensions,
    IReadOnlyList<string> SkipFolders);
