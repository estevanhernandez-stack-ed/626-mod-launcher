using System.IO;
using ModManager.Core;

namespace ModManager.Tests.Loaders;

/// <summary>Shared builders for the loader tests: one place to say what a test registration and a
/// test play folder look like.</summary>
internal static class LoaderTestKit
{
    public static string TempPlayFolder(params string[] files)
    {
        var d = Path.Combine(Path.GetTempPath(), "mm-loaders-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        foreach (var f in files) File.WriteAllText(Path.Combine(d, f), "x");
        return d;
    }

    /// <summary>A registration as the scan sees it.</summary>
    public static GameEntry Game(string id, string? engine, string? steamAppId = null, string? eaContentId = null)
        => new() { Id = id, GameName = id, Engine = engine, SteamAppId = steamAppId, EaContentId = eaContentId };

    /// <summary>A Steam registration whose id does not matter to the case under test.</summary>
    public static GameEntry SteamGame(string engine, string steamAppId) => Game("game-" + steamAppId, engine, steamAppId);
}
