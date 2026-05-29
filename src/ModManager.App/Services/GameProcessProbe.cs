using System.Diagnostics;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.App.Services;

/// <summary>App-side IGameRunningProbe: feeds the live running-process names into the pure matcher.
/// A probe failure never blocks the user — it degrades to "not running" so the Safe Clear pre-flight
/// errs toward allowing the clear rather than crashing.</summary>
public sealed class GameProcessProbe : IGameRunningProbe
{
    public bool AnyRunning(GameEntry game)
    {
        string[] names;
        try
        {
            names = Process.GetProcesses()
                .Select(p => { try { return p.ProcessName; } catch { return ""; } })
                .Where(n => n.Length > 0)
                .ToArray();
        }
        catch { return false; }
        return ProcessNameMatch.AnyRunning(game, names);
    }
}
