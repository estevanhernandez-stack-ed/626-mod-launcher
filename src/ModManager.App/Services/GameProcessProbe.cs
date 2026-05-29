using System.Diagnostics;
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
        var names = Process.GetProcesses()
            .Select(p => { try { return p.ProcessName; } catch { return ""; } })
            .Where(n => n.Length > 0)
            .ToArray();
        return ProcessNameMatch.AnyRunning(game, names);
    }
}
