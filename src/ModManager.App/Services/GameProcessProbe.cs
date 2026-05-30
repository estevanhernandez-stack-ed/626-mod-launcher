using System.Diagnostics;
using System.IO;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.App.Services;

/// <summary>App-side IGameRunningProbe: feeds the live running-process names into the pure matcher.
/// A single inaccessible process (zombie / elevated PID) is skipped; but a WHOLE-enumeration failure
/// is NOT swallowed — it propagates so the Safe Clear pre-flight fails CLOSED (refuse) rather than
/// green-lighting a destructive reset against a "not running" it could not actually verify.</summary>
public sealed class GameProcessProbe : IGameRunningProbe
{
    public bool AnyRunning(GameEntry game)
    {
        // Running-process enumeration: a whole-enumeration failure MUST propagate (fail closed).
        var names = Process.GetProcesses()
            .Select(p => { try { return p.ProcessName; } catch { return ""; } })
            .Where(n => n.Length > 0)
            .ToArray();
        // Install-dir executables: covers the common steam-launch case (no exe target, engine not in
        // the runtime map). Best-effort — if the install dir is unreadable we pass none rather than
        // throw; the real fail-closed safety net is the process-enumeration above, and the target +
        // runtime-map checks still run. (We never widen a not-running into a false negative here.)
        return ProcessNameMatch.AnyRunning(game, names, InstallExeNames(game));
    }

    private static IReadOnlyCollection<string> InstallExeNames(GameEntry game)
    {
        try
        {
            var root = game.GameRoot;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return System.Array.Empty<string>();
            // Top-level + one common nesting (FromSoft + many launchers keep the real exe under Game\).
            var names = new List<string>();
            foreach (var dir in new[] { root, Path.Combine(root, "Game") })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.exe"))
                    names.Add(Path.GetFileNameWithoutExtension(f));
            }
            return names;
        }
        catch { return System.Array.Empty<string>(); }
    }
}
