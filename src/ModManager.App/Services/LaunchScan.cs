using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>What a folder scan found: the ways to launch, plus the ME2 config path (for Phase B).</summary>
public sealed record LaunchDetection(IReadOnlyList<LaunchTarget> Targets, string? ModEngineConfig);

/// <summary>
/// Finds how a game should be launched by scanning the game folder for mod launchers — Mod
/// Engine 2 (FromSoft) and Seamless Co-op — and pairing them with the vanilla Steam launch.
/// The IO lives here; the game-specific ME2 facts (target codes, args) stay pure in
/// <see cref="ModEngine2"/>.
/// </summary>
public static class LaunchScan
{
    // Mod launchers nest a level or two deep (e.g. <game>\Game\, <game>\ModEngine-2.x\). Bounded.
    private const int MaxDepth = 3;

    public static LaunchDetection Detect(string? gameRoot, string? engine, string? steamAppId)
    {
        var targets = new List<LaunchTarget>();
        string? me2Config = null;

        if (!string.IsNullOrEmpty(gameRoot) && Directory.Exists(gameRoot))
        {
            // Mod Engine 2 — the only path that actually loads FromSoft mods.
            var me2 = FindFile(gameRoot, ModEngine2.LauncherExe);
            if (me2 is not null)
            {
                var me2Dir = Path.GetDirectoryName(me2)!;
                var target = ModEngine2.TargetForAppId(steamAppId) ?? "er"; // default to Elden Ring's code
                var configName = ResolveConfigName(me2Dir, target);
                var configPath = Path.Combine(me2Dir, configName);
                if (File.Exists(configPath)) me2Config = configPath;
                targets.Add(new LaunchTarget("Play with mods (Mod Engine 2)", "exe", me2)
                {
                    Args = ModEngine2.LaunchArgs(target, configName),
                    WorkingDir = me2Dir,
                    IsDefault = true,
                });
            }

            // Verified internal launch options from the catalog (e.g. Elden Ring's offline-with-mods
            // launch — run the real exe directly, no anti-cheat). Only added when the exe is present.
            foreach (var opt in LaunchOptions.For(steamAppId).Where(o => o.Kind == LaunchOptionKind.Internal && o.Exe is not null))
            {
                var exeAbs = Path.Combine(gameRoot, opt.Exe!);
                if (!File.Exists(exeAbs)) continue;
                targets.Add(new LaunchTarget(opt.Title, "exe", exeAbs)
                {
                    Args = opt.Args,
                    WorkingDir = opt.WorkingSubdir is null ? gameRoot : Path.Combine(gameRoot, opt.WorkingSubdir),
                    IsDefault = targets.Count == 0,
                });
            }

            // Seamless Co-op (Elden Ring) ships its own launcher — vanilla play won't run it.
            var seamless = FindSeamless(gameRoot);
            if (seamless is not null)
                targets.Add(new LaunchTarget("Play (Seamless Co-op)", "exe", seamless)
                {
                    WorkingDir = Path.GetDirectoryName(seamless),
                    IsDefault = targets.Count == 0,
                });
        }

        // Vanilla Steam launch is always offered as the fallback / "play unmodded" choice.
        if (!string.IsNullOrEmpty(steamAppId))
            targets.Add(new LaunchTarget("Play vanilla (Steam)", "steam", $"steam://rungameid/{steamAppId}")
            {
                IsDefault = targets.Count == 0,
            });

        return new LaunchDetection(targets, me2Config);
    }

    // Prefer the target's named config (config_eldenring.toml), else any config_*.toml in the folder.
    private static string ResolveConfigName(string me2Dir, string target)
    {
        var named = ModEngine2.ConfigNameForTarget(target);
        if (named is not null && File.Exists(Path.Combine(me2Dir, named))) return named;
        try
        {
            var any = Directory.GetFiles(me2Dir, "config_*.toml").FirstOrDefault();
            if (any is not null) return Path.GetFileName(any);
        }
        catch { /* unreadable */ }
        return named ?? "config_eldenring.toml";
    }

    public static string? FindSeamless(string gameRoot)
    {
        // Known launcher names across Seamless Co-op versions, then any *seamless*.exe.
        foreach (var name in new[] { "launch_elden_ring_seamlesscoop.exe", "ersc_launcher.exe" })
        {
            var hit = FindFile(gameRoot, name);
            if (hit is not null) return hit;
        }
        return FindMatch(gameRoot, f => f.Contains("seamless", StringComparison.OrdinalIgnoreCase)
                                        && f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                        && f.Contains("launch", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindFile(string root, string fileName)
        => FindMatch(root, f => string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase));

    // Bounded breadth-first search for the first file whose name matches the predicate.
    private static string? FindMatch(string root, Func<string, bool> nameMatches)
    {
        var queue = new Queue<(string dir, int depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                    if (nameMatches(Path.GetFileName(f))) return f;
                if (depth < MaxDepth)
                    foreach (var d in Directory.GetDirectories(dir))
                        queue.Enqueue((d, depth + 1));
            }
            catch { /* unreadable dir — skip */ }
        }
        return null;
    }
}
