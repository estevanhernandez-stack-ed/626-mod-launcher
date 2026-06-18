// src/ModManager.Core/Plugins/PluginGate.cs
namespace ModManager.Core.Plugins;

/// <summary>Decides which feed entries this binary should install: the schema must be known, the
/// entry's minimum binary version must be satisfied, and it must not already be installed at the
/// listed version. Pure — no I/O. An unknown schema rejects the whole feed (forward-compat).</summary>
public static class PluginGate
{
    public static IReadOnlyList<PluginIndexEntry> SelectInstallable(
        PluginIndex index, Version binaryVersion, IReadOnlyDictionary<string, string> installedVersions)
    {
        if (index.SchemaVersion > PluginIndex.KnownSchemaVersion) return Array.Empty<PluginIndexEntry>();

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
