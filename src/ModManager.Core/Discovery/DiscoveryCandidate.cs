namespace ModManager.Core.Discovery;

/// <summary>Why a path was claimed as a possible mod. Drives how it is matched later:
/// archives can be md5-identified; the rest fall to name matching.</summary>
public enum DiscoveryKind { Signature, EngineShaped, Archive }

/// <summary>One thing the sweep claims might be a mod. Paths are RELATIVE to the swept root,
/// so Core never sees an absolute path (pure-core law).</summary>
public sealed record DiscoveryCandidate(string RelativePath, string FileName, DiscoveryKind Kind);

/// <summary>What the classifier needs to know about this game. Supplied by the App from the
/// effective manifest; Core never reads the manifest here.</summary>
public sealed record DiscoverySweepOptions(
    string? ModPath,
    IReadOnlyList<string> EngineExtensions,
    IReadOnlyList<string> SkipFolders);
