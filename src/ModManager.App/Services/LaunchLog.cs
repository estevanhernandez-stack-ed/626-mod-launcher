using System.IO;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Recency;

namespace ModManager.App.Services;

/// <summary>
/// Append-only log of 626-observed launch sessions, persisted at
/// <c>%LOCALAPPDATA%\ModManagerBuilder\launch-log.json</c> via <see cref="AtomicJson"/> (camelCase).
/// Backs <see cref="OwnLaunchLastPlayedSource"/>'s summed-playtime read. Tolerant load — a missing
/// or corrupt file resolves to an empty log, never throws.
/// </summary>
public static class LaunchLog
{
    public const string FileName = "launch-log.json";

    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    public static string DataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManagerBuilder");

    public static string PathFor(string dataRoot) => Path.Combine(dataRoot, FileName);

    public static IReadOnlyList<LaunchLogEntry> LoadAll(string? dataRoot = null)
    {
        dataRoot ??= DataRoot;
        try
        {
            var json = File.ReadAllText(PathFor(dataRoot));
            return JsonSerializer.Deserialize<List<LaunchLogEntry>>(json, ReadOpts) ?? [];
        }
        catch { return []; }
    }

    /// <summary>Append one entry and atomically rewrite the log.</summary>
    public static void Append(LaunchLogEntry entry, string? dataRoot = null)
    {
        dataRoot ??= DataRoot;
        var entries = new List<LaunchLogEntry>(LoadAll(dataRoot)) { entry };
        AtomicJson.WriteJsonAtomic(PathFor(dataRoot), entries);
    }

    /// <summary>All logged launch sessions for one game, in the order they were recorded.</summary>
    public static IReadOnlyList<LaunchLogEntry> ForGame(string id, string? dataRoot = null)
        => LoadAll(dataRoot).Where(e => e.GameId == id).ToList();
}
