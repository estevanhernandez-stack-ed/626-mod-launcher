using System.Diagnostics;
using ModManager.Core;
using ModManager.Core.LooseMods;

namespace ModManager.App.Services;

/// <summary>
/// Gathers the evidence for "did the mods actually load", and lets <see cref="LaunchVerification"/>
/// decide. The decision is pure and lives in Core; everything here is the untestable half — finding
/// the game's process, reading what it has mapped, and stat-ing a log file.
///
/// <para>All of it is best-effort by design. A process we cannot open, a log we cannot read and a game
/// that has not appeared yet all produce the SAME thing: no evidence, which Core turns into
/// <see cref="LoadVerdict.Unknown"/> rather than into a false alarm.</para>
///
/// <para>Read-only, always. This opens a handle to enumerate modules and never touches the game while
/// it runs — the diagnosis is worth having; acting on it mid-session is not.</para>
/// </summary>
public sealed class LaunchVerifier
{
    /// <summary>How long to wait for the game's process to appear. Steam games in particular take a
    /// while to get from "we started something" to "the game is up".</summary>
    private static readonly TimeSpan ProcessWait = TimeSpan.FromSeconds(45);

    /// <summary>Grace after the process appears before reading its modules. A proxy loader maps early
    /// but not instantly, and reading too soon would report a loader missing that was moments away —
    /// the exact false alarm Core's three-answer rule exists to avoid.</summary>
    private static readonly TimeSpan MapGrace = TimeSpan.FromSeconds(6);

    /// <summary>Watch the launch and report what loaded. Returns an empty string when there is nothing
    /// worth saying, which is the common case on a healthy launch of a game with no loaders.</summary>
    public async Task<string> WatchAsync(GameContext ctx, DateTime launchedUtc, CancellationToken ct = default)
    {
        try
        {
            var gameRoot = Path.GetFullPath(ctx.GameRoot);
            var proc = await Task.Run(() => WaitForGameProcess(gameRoot, ct), ct);
            if (proc is not null)
            {
                try { await Task.Delay(MapGrace, ct); } catch (OperationCanceledException) { return ""; }
            }

            var modules = proc is null ? Array.Empty<string>() : ReadModules(proc);
            var evidence = BuildEvidence(ctx, modules);
            if (evidence.Count == 0) return "";

            var outcomes = LaunchVerification.ForAll(launchedUtc, evidence);
            return LaunchVerification.Summarize(ctx.Game.GameName ?? "The game", outcomes);
        }
        catch (OperationCanceledException) { return ""; }
        catch { return ""; }   // diagnosis is a nicety; it never breaks a launch
    }

    /// <summary>The running process whose image lives inside the game folder. Identified by LOCATION
    /// rather than by an expected exe name: a game launched through Seamless Co-op, Mod Engine 2 or a
    /// vendor launcher ends up as a different executable than the one 626 started, and all of them sit
    /// under the game root.</summary>
    private static Process? WaitForGameProcess(string gameRoot, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + ProcessWait;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (Path.GetFullPath(path).StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase)) return p;
                }
                catch { /* most processes refuse MainModule; that is normal and not an error here */ }
            }
            Thread.Sleep(1500);
        }
        return null;
    }

    private static IReadOnlyList<string> ReadModules(Process proc)
    {
        try
        {
            var names = new List<string>();
            foreach (ProcessModule m in proc.Modules)
            {
                try { names.Add(m.ModuleName ?? ""); } catch { }
            }
            return names;
        }
        catch
        {
            // Access denied, or the game exited between finding it and reading it. Empty means
            // "could not tell", never "nothing loaded".
            return Array.Empty<string>();
        }
    }

    /// <summary>One evidence bundle per loader this game is known to use — the framework catalog's
    /// components, plus the proxy loaders a loose-root game keeps in its own root.</summary>
    private static IReadOnlyList<LoaderEvidence> BuildEvidence(GameContext ctx, IReadOnlyList<string> modules)
    {
        var list = new List<LoaderEvidence>();

        foreach (var presence in FrameworkDeps.Check(ctx))
        {
            // Only ask about a framework that is actually installed. "Did the thing you do not have
            // load" is not a question, and answering it would fill the status line with noise.
            if (!presence.IsPresent) continue;

            var files = presence.Dep.Components
                .SelectMany(c => c.AnyOf)
                .Select(Path.GetFileName)
                .Where(f => !string.IsNullOrEmpty(f))
                .Select(f => f!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            list.Add(new LoaderEvidence(presence.Dep.Name, files, modules,
                NewestLog(ctx, presence.Dep.LogRelativePaths)));
        }

        // Loose-root games keep their loaders as bare proxy DLLs in the game root, and the listing
        // already recognises them. Name each by what the FILE says it is, not by the filename - that
        // is A24's lesson, and "version.dll did not load" would be a worse sentence than "DLSS Enabler
        // did not load".
        var playFolder = LooseRootListing.PlayFolder(ctx.GameRoot);
        if (playFolder is not null)
        {
            foreach (var mod in LooseRootListing.Enabled(playFolder).Where(m => m.Kind == "loader"))
            {
                var file = mod.Entries.FirstOrDefault();
                if (string.IsNullOrEmpty(file)) continue;
                var (product, version) = LoaderIdentity.ReadProduct(Path.Combine(playFolder, file));
                var name = LoaderIdentity.Identified(product)
                    ? LoaderIdentity.Label(file, product, version)
                    : file;
                list.Add(new LoaderEvidence(name, new[] { file }, modules, null));
            }
        }

        return list;
    }

    /// <summary>The most recent write time among a framework's declared logs, probed under every root
    /// the framework itself is probed under. Null when it declares none or none exists — which reads as
    /// "no log evidence", never as failure.</summary>
    private static DateTime? NewestLog(GameContext ctx, IReadOnlyList<string>? relPaths)
    {
        if (relPaths is null || relPaths.Count == 0) return null;
        DateTime? newest = null;
        foreach (var rel in relPaths)
        {
            foreach (var root in ProbeRoots(ctx))
            {
                try
                {
                    var abs = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(abs)) continue;
                    var t = File.GetLastWriteTimeUtc(abs);
                    if (newest is null || t > newest) newest = t;
                }
                catch { }
            }
        }
        return newest;
    }

    // Mirrors the framework probe's own roots closely enough for a log: the UE project subfolder a
    // mod location names, then the bare game root.
    private static IEnumerable<string> ProbeRoots(GameContext ctx)
    {
        foreach (var loc in ctx.Game.ModLocations ?? Array.Empty<ModLocation>())
        {
            var first = (loc.Path ?? "").Replace('\\', '/').TrimStart('/').Split('/').FirstOrDefault();
            if (!string.IsNullOrEmpty(first) && !first.Equals("Content", StringComparison.OrdinalIgnoreCase))
                yield return Path.Combine(ctx.GameRoot, first);
        }
        yield return ctx.GameRoot;
    }
}
