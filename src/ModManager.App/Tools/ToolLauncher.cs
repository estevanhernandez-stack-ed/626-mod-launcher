using System.Diagnostics;
using System.IO;
using ModManager.Core.Tools;

namespace ModManager.App.Tools;

/// <summary>
/// Fire-and-continue process launcher for registered tools. The pre-launch snapshot runs
/// SYNCHRONOUSLY before <see cref="Process.Start(ProcessStartInfo)"/> — if it throws (out of
/// disk, save folder missing, etc.) the process never starts. The caller catches and surfaces.
///
/// Exit notification is delivered via <c>Process.Exited</c>, which fires on a thread-pool
/// thread; the caller is responsible for marshaling to the UI thread before touching VM state.
/// </summary>
public static class ToolLauncher
{
    /// <summary>
    /// Launch a tool. Performs the pre-launch snapshot if <c>entry.EditsSaves</c>, then starts
    /// the process. Returns once the process has STARTED — not when it exits. Exit notification
    /// fires via <paramref name="onExit"/> on a thread-pool thread; the caller must marshal to
    /// the UI thread.
    /// </summary>
    /// <param name="entry">The registered tool to launch.</param>
    /// <param name="snapshot">
    /// Optional pre-launch snapshot delegate. Invoked only when <c>entry.EditsSaves</c> is true.
    /// Returns the snapshot label on success; throws on failure (caller surfaces the message).
    /// </param>
    /// <param name="onExit">
    /// Called with the snapshot label (or null when no snapshot was taken) when the process
    /// exits. Fires on a thread-pool thread — marshal to the UI thread before mutating UI state.
    /// </param>
    public static void Launch(
        ToolEntry entry,
        Func<string>? snapshot,
        Action<string?> onExit)
    {
        // Snapshot FIRST so a snapshot failure prevents the launch — we never edit saves under a
        // tool that started before we could back them up. If snapshot throws, propagate.
        string? snapshotLabel = null;
        if (entry.EditsSaves && snapshot is not null)
        {
            snapshotLabel = snapshot();
        }

        var exePath = Path.Combine(entry.InstallDir, entry.Runnable);
        if (!File.Exists(exePath))
            throw new FileNotFoundException(
                $"Runnable not found: {exePath}. Use 'Open install folder' to reach the tool manually.");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true, // honors .bat / .ps1 / .cmd via shell
            WorkingDirectory = entry.InstallDir,
        };

        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {exePath}");

        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) => onExit(snapshotLabel);
    }
}
