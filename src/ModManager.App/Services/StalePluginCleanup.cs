#if FULL
using System.IO;
using ModManager.Core.Plugins;

namespace ModManager.App.Services;

/// <summary>
/// Removes the downloaded Nexus plugin, which no longer has a job.
///
/// <para>Nexus used to arrive as a signed assembly fetched from our own feed. It is compiled into
/// every build now, so an installed copy is never consulted — <see cref="ModSourceRegistry.Add"/>
/// ignores a second source with an id that is already registered, and the compiled-in one registers
/// first. The file would sit there looking authoritative and doing nothing.</para>
///
/// <para><b>Why remove it rather than leave it.</b> The ordering already makes it harmless, so this is
/// not a correctness fix — it is about not leaving a thing on someone's disk that reads as in charge
/// and is not. It also closes a quiet trap: if a NEWER Nexus had reached the feed before this change
/// shipped, a user could hold a newer plugin than the one compiled in, and see it silently ignored
/// with no way to tell why.</para>
///
/// <para><b>Only what we put there.</b> This deletes the plugin the feed installer wrote and the
/// record naming it, and nothing else. Any other file someone has placed in that folder is theirs and
/// is left alone — which is also why the directory itself is only removed when it ends up empty.</para>
///
/// <para>Every step is best-effort. A file held open by a virus scanner is not worth a crash on
/// startup, and the next run will try again.</para>
/// </summary>
internal static class StalePluginCleanup
{
    /// <summary>The id the Nexus plugin was installed under, and therefore its dll name.</summary>
    private const string NexusPluginId = "nexus";

    public static void Run()
    {
        try
        {
            var dir = PluginHost.PluginsDir;
            if (!Directory.Exists(dir)) return;

            var recordPath = Path.Combine(dir, StalePluginCleanupPlan.RecordFileName);
            var record = File.Exists(recordPath) ? InstalledPluginsStore.Read(recordPath) : null;
            var entries = Directory.EnumerateFileSystemEntries(dir)
                                   .Select(Path.GetFileName)
                                   .Where(n => n is not null)
                                   .Select(n => n!)
                                   .ToList();

            // Decide first, in Core, under test. This half only carries the decision out.
            var plan = StalePluginCleanupPlan.For(NexusPluginId, record, entries);

            if (plan.RecordToWrite is not null) InstalledPluginsStore.Write(recordPath, plan.RecordToWrite);
            foreach (var name in plan.FilesToDelete) TryDelete(Path.Combine(dir, name));
            if (plan.RemoveDirectory) try { Directory.Delete(dir); } catch { }
        }
        catch { /* tidying up must never be the reason the app fails to start */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
#endif
