namespace ModManager.Core.Plugins;

/// <summary>What tidying up a retired plugin should touch, decided before anything is deleted.</summary>
/// <param name="FilesToDelete">File names, relative to the plugins folder.</param>
/// <param name="RecordToWrite">The record as it should be left, or null when the record file itself
/// should go because nothing is recorded any more.</param>
/// <param name="RemoveDirectory">True only when the folder ends up holding nothing at all.</param>
public sealed record StalePluginPlan(
    IReadOnlyList<string> FilesToDelete,
    IReadOnlyDictionary<string, string>? RecordToWrite,
    bool RemoveDirectory);

/// <summary>
/// Deciding what to remove when a plugin stops being delivered as a plugin.
///
/// <para>Nexus was fetched from our own feed as a signed assembly; it is compiled into every build
/// now, so the downloaded copy is never consulted. Removing it is tidying up after ourselves.</para>
///
/// <para><b>Pure, and separate from the deleting, because it deletes.</b> The App half is a few
/// <c>File.Delete</c> calls it would be easy to get subtly wrong — taking a folder that still holds
/// somebody else's plugin, or a record that still names one. Those are decisions, and decisions
/// belong where they can be tested.</para>
/// </summary>
public static class StalePluginCleanupPlan
{
    /// <summary>
    /// Work out what to remove.
    /// </summary>
    /// <param name="pluginId">The retired plugin's id — also the name of its dll.</param>
    /// <param name="record">What <c>installed-plugins.json</c> currently says.</param>
    /// <param name="dirEntries">Every file name currently in the plugins folder.</param>
    public static StalePluginPlan For(
        string pluginId,
        IReadOnlyDictionary<string, string>? record,
        IReadOnlyCollection<string>? dirEntries)
    {
        var entries = dirEntries ?? Array.Empty<string>();
        var recorded = record ?? new Dictionary<string, string>();

        // BOTH files the feed installer writes. The detached signature was missed on the first cut
        // because the test fixture was invented rather than taken from a real machine - and a real
        // machine had nexus.dll.sig sitting beside the dll, which would have been left behind and kept
        // the folder alive for no reason.
        var ours = new[] { pluginId + ".dll", pluginId + ".dll.sig" };
        var remove = new List<string>();

        // Only OUR files, matched by name. Anything else in that folder is somebody's and stays -
        // including a hand-dropped dll, which the loader already refuses to load anyway.
        foreach (var name in ours)
            if (entries.Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase)))
                remove.Add(name);

        var left = new Dictionary<string, string>(recorded, StringComparer.OrdinalIgnoreCase);
        var wasRecorded = left.Remove(pluginId);

        // The record survives whenever it still names a plugin: a second plugin's entry is the whole
        // reason this rewrites the file rather than deleting it.
        IReadOnlyDictionary<string, string>? write = null;
        if (wasRecorded)
        {
            if (left.Count > 0) write = left;
            else if (entries.Any(e => string.Equals(e, RecordFileName, StringComparison.OrdinalIgnoreCase)))
                remove.Add(RecordFileName);
        }

        var remaining = entries.Count(e => !remove.Contains(e, StringComparer.OrdinalIgnoreCase));
        return new StalePluginPlan(remove, write, RemoveDirectory: remaining == 0 && entries.Count > 0);
    }

    public const string RecordFileName = "installed-plugins.json";
}
