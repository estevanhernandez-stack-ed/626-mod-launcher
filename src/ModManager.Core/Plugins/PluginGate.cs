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
}
