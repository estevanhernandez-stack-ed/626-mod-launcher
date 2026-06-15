using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// Scans a game folder for engine signatures and asks Core to guess the engine. The IO lives
/// here; the decision (<see cref="EngineDetect.GuessEngine"/>) stays pure + tested. Unreal Content/Paks
/// discovery is delegated to <see cref="UeProjectScan"/> (root + up to 2 wrapper levels, denylist-skipped,
/// budget-bounded); the other signatures stay bounded to root + one subfolder level for speed.
/// </summary>
public static class EngineScan
{
    public static string? Detect(string? gameRoot) => EngineDetect.GuessEngine(Probe(gameRoot));

    public static EngineProbe Probe(string? root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return new EngineProbe();

        bool RootDir(string n) => Directory.Exists(Path.Combine(root, n));
        bool RootFile(string n) => File.Exists(Path.Combine(root, n));

        string[] subs;
        try { subs = Directory.GetDirectories(root); } catch { subs = Array.Empty<string>(); }

        var contentPaks = UeProjectScan.HasContentPaks(root);

        var dataPlugins = false;
        var data = Path.Combine(root, "Data");
        if (Directory.Exists(data))
        {
            try
            {
                dataPlugins = Directory.EnumerateFiles(data, "*.es*").Any(f =>
                {
                    var e = Path.GetExtension(f).ToLowerInvariant();
                    return e is ".esm" or ".esp" or ".esl";
                });
            }
            catch { /* unreadable Data */ }
        }

        var source = RootFile("gameinfo.txt")
            || subs.Any(s => File.Exists(Path.Combine(s, "gameinfo.txt")) || Directory.Exists(Path.Combine(s, "addons")));

        var unity = RootFile("UnityPlayer.dll")
            && subs.Any(s => Path.GetFileName(s).EndsWith("_Data", StringComparison.OrdinalIgnoreCase));

        return new EngineProbe
        {
            HasBepInEx = RootDir("BepInEx"),
            HasMelonLoader = RootDir("MelonLoader"),
            HasContentPaks = contentPaks,
            HasDataPlugins = dataPlugins,
            HasStardew = RootFile("Stardew Valley.exe") || RootFile("StardewValley.exe"),
            HasSourceAddons = source,
            HasUnityData = unity,
        };
    }
}
