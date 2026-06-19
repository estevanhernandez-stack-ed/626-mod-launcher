// src/ModManager.Core/Plugins/PluginGate.cs
namespace ModManager.Core.Plugins;

/// <summary>Decides which feed entries this binary should install: the schema must be in the known
/// range, the entry's minimum binary version must be satisfied, and it must not already be installed
/// at the listed version. Pure — no I/O. A schema outside [1, KnownSchemaVersion] rejects the whole
/// feed: too-high is forward-compat (a newer feed never breaks us), and 0/negative is a malformed feed
/// we refuse to interpret as schema 1.</summary>
public static class PluginGate
{
    public static IReadOnlyList<PluginIndexEntry> SelectInstallable(
        PluginIndex index, Version binaryVersion, IReadOnlyDictionary<string, string> installedVersions)
    {
        // Reject anything outside the known range. 0/negative is malformed (not "schema 1"); too-high is a
        // future schema we don't understand. Either way: refuse the whole feed.
        if (index.SchemaVersion < 1 || index.SchemaVersion > PluginIndex.KnownSchemaVersion)
            return Array.Empty<PluginIndexEntry>();

        var result = new List<PluginIndexEntry>();
        foreach (var e in index.Plugins)
        {
            if (!Version.TryParse(e.MinBinaryVersion, out var min)) continue; // unparseable → skip (safe)
            if (binaryVersion < min) continue;                                // needs a newer binary
            if (installedVersions.TryGetValue(e.Id, out var have) && have == e.Version) continue; // already current
            result.Add(e);
        }
        return result;
    }

    /// <summary>The lowest binary version that would let at least one currently-version-gated plugin
    /// install, or <see langword="null"/> if nothing in the feed is blocked solely by the binary being
    /// too old. Lets a caller turn a silent gate-out into an honest "update the launcher to vX" message
    /// instead of mis-reporting "up to date". Pure — no I/O.</summary>
    public static Version? MinimumBinaryToUnblock(
        PluginIndex index, Version binaryVersion, IReadOnlyDictionary<string, string> installedVersions)
    {
        // A schema we refuse isn't a version problem — don't suggest a launcher update for it.
        if (index.SchemaVersion < 1 || index.SchemaVersion > PluginIndex.KnownSchemaVersion)
            return null;

        Version? lowest = null;
        foreach (var e in index.Plugins)
        {
            if (!Version.TryParse(e.MinBinaryVersion, out var min)) continue; // unparseable → not a clear update ask
            if (binaryVersion >= min) continue;                              // not version-blocked
            if (installedVersions.TryGetValue(e.Id, out var have) && have == e.Version) continue; // already current
            if (lowest is null || min < lowest) lowest = min;                // the smallest bump that unblocks something
        }
        return lowest;
    }
}
