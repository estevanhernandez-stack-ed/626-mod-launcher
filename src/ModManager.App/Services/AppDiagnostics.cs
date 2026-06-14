using System.IO;

namespace ModManager.App.Services;

/// <summary>
/// Best-effort, app-wide exception log. The app otherwise has no unhandled-exception sink, and WinUI
/// can swallow exceptions thrown from input-event handlers — leaving a dead-feeling UI with no trace
/// (the popular-pick engine-select bug was exactly that). This appends a timestamped line to a single
/// on-disk log next to the manifest cache so those failures are diagnosable. Never throws —
/// diagnostics must never be the thing that breaks the app.
/// </summary>
public static class AppDiagnostics
{
    /// <summary>The on-disk error log: <c>%LOCALAPPDATA%\ModManagerBuilder\app-errors.log</c>.</summary>
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModManagerBuilder", "app-errors.log");

    /// <summary>Log an exception under a short source tag (e.g. "ui", "popular-pick").</summary>
    public static void Log(string source, Exception ex) => Write(source, ex.ToString());

    /// <summary>Log a plain diagnostic message under a short source tag.</summary>
    public static void Log(string source, string message) => Write(source, message);

    private static void Write(string source, string detail)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.UtcNow:O}\t{source}\t{detail}\n");
        }
        catch { /* best-effort; never let logging break the app */ }
    }
}
