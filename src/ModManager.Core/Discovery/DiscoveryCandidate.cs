namespace ModManager.Core.Discovery;

/// <summary>Why a path was claimed as a possible mod. Drives how it is matched later:
/// archives can be md5-identified; the rest fall to name matching.
///
/// <see cref="ProxyLoader"/> is split out from <see cref="Signature"/> because the two are
/// different KINDS OF THING, not different confidence levels. A bare proxy DLL (version.dll,
/// winmm.dll, dinput8.dll) is a mod LOADER — infrastructure other mods ride on — while a
/// <c>.asi</c> plugin is an actual mod that rides on one. Presenting a loader as a mod in an
/// "already installed mods" list mis-describes it. It is also unnameable in principle: the same
/// version.dll ships as ASI Loader, Ultimate ASI Loader, and Cyber Engine Tweaks, so the filename
/// cannot disambiguate which one it is. Naming it would be exactly the false accusation the
/// classifier's safety line forbids — so a loader is surfaced as found, described as a loader,
/// and never guessed at.</summary>
public enum DiscoveryKind { Signature, EngineShaped, Archive, ProxyLoader }

/// <summary>One thing the sweep claims might be a mod. For anything <see cref="DiscoverySweep"/>
/// produced, <paramref name="RelativePath"/> is RELATIVE to the swept root — Core resolves nothing
/// against the filesystem, and the sweep's own skip / mod-path matching REQUIRES the relative form.
///
/// <para>One caller legitimately supplies an ABSOLUTE path: the App's downloads-folder pass, where
/// the user points at a folder that normally lives outside the game root (often on another drive),
/// so no relative form exists. Those candidates are constructed in the App and never fed back
/// through <see cref="DiscoverySweep"/>; the two places that resolve a candidate to disk are both
/// App-side and both go through <c>Path.Combine(root, RelativePath)</c>, which returns an
/// already-rooted second argument unchanged. Core still resolves nothing — it only carries the
/// string and hands it to the UI to display. If you add a Core consumer that JOINS this path to a
/// root or assumes it stays inside one, handle the rooted case explicitly.</para></summary>
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
