namespace ModManager.Core;

/// <summary>What we can say about one loader after a launch.</summary>
public enum LoadVerdict
{
    /// <summary>It ran. Either its module is loaded in the game process, or its log advanced past the
    /// launch.</summary>
    Ran,

    /// <summary>It did not run, and we can say so: we read the process's modules and it was not among
    /// them, or its log is older than the launch we just made.</summary>
    DidNotRun,

    /// <summary>We could not tell. NOT the same as DidNotRun, and the distinction is the whole point —
    /// a first launch after a fresh install has no log yet and nothing loaded to read.</summary>
    Unknown,
}

/// <summary>What was observed about one loader around a launch. Gathering this touches the process
/// list and the filesystem, which is why it arrives as data rather than being fetched here.</summary>
/// <param name="Name">What the user calls it — "UE4SS", "ReShade".</param>
/// <param name="ModuleFileNames">Filenames that would prove it loaded, e.g. <c>UE4SS.dll</c>,
/// <c>dwmapi.dll</c>. Any one being present in <paramref name="LoadedModules"/> is proof.</param>
/// <param name="LoadedModules">Module filenames the running game process has loaded. EMPTY means we
/// could not read them (game not running, access denied) — never "it loaded nothing".</param>
/// <param name="LogWrittenUtc">Last-write time of the loader's own log, or null when it has none or
/// we could not read it.</param>
public sealed record LoaderEvidence(
    string Name,
    IReadOnlyList<string> ModuleFileNames,
    IReadOnlyList<string> LoadedModules,
    DateTime? LogWrittenUtc);

/// <summary>One loader's verdict, and the sentence that says it.</summary>
public sealed record LoaderOutcome(string Name, LoadVerdict Verdict, string Detail);

/// <summary>
/// Did the mods actually load?
///
/// <para><b>Why this exists.</b> 626 launches the game and stops looking. It knows exactly what it
/// enabled and never finds out whether any of it ran — which is the whole A13 class of bug, where the
/// launcher read <c>27 of 27 enabled</c> while the game refused to start its mods and the only witness
/// was the user, at the crash.</para>
///
/// <para>The evidence is already on disk and in memory, and neither signal needs a person: a loader's
/// own log advancing past the launch 626 already stamps, and the module list of the running game
/// process. The second is the strong one — a proxy DLL present in memory has definitely run, where a
/// file present on disk has only definitely been installed.</para>
///
/// <para><b>Three answers, never two.</b> "We could not tell" is a real answer and must not be reported
/// as "it did not load": the first launch after a fresh install has no log and nothing to read, and
/// crying wolf there teaches the user to ignore the one time it matters.</para>
///
/// <para>This reports. It never re-enables, reinstalls or repairs — a launcher that reacts to its own
/// diagnosis is one that surprises people mid-session.</para>
/// </summary>
public static class LaunchVerification
{
    /// <summary>How far before the launch stamp a log write still counts as "this launch". A loader
    /// writes its first line as the game starts, which can land marginally before the stamp 626
    /// records, and clocks are not perfect. Generous enough to avoid a false alarm, far too short to
    /// make a two-week-old log look fresh.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(90);

    public static LoaderOutcome For(DateTime launchedUtc, LoaderEvidence evidence)
    {
        var name = string.IsNullOrWhiteSpace(evidence.Name) ? "This loader" : evidence.Name;

        // Strongest first: loaded in the process. A module that is mapped has run.
        var loaded = evidence.LoadedModules ?? Array.Empty<string>();
        var wanted = evidence.ModuleFileNames ?? Array.Empty<string>();
        var hit = wanted.FirstOrDefault(w => loaded.Any(l => SameFile(l, w)));
        if (hit is not null)
            return new LoaderOutcome(name, LoadVerdict.Ran, $"{name} is loaded in the game ({hit}).");

        // Its own log, second. Written since the launch means it ran this time.
        if (evidence.LogWrittenUtc is { } written)
        {
            if (written >= launchedUtc - Tolerance)
                return new LoaderOutcome(name, LoadVerdict.Ran, $"{name} wrote its log during this launch.");

            // A stale log is only evidence when we know it writes one every run. It does.
            return new LoaderOutcome(name, LoadVerdict.DidNotRun,
                $"{name} did not load — the last time it ran was {written.ToLocalTime():d MMMM}.");
        }

        // No log to read. If we DID read the process and it is not there, that is an answer.
        if (loaded.Count > 0 && wanted.Count > 0)
            return new LoaderOutcome(name, LoadVerdict.DidNotRun,
                $"{name} did not load — it is not among the files the game has open.");

        return new LoaderOutcome(name, LoadVerdict.Unknown,
            $"Whether {name} loaded could not be determined.");
    }

    public static IReadOnlyList<LoaderOutcome> ForAll(DateTime launchedUtc, IEnumerable<LoaderEvidence>? evidence)
        => (evidence ?? Array.Empty<LoaderEvidence>()).Select(e => For(launchedUtc, e)).ToList();

    /// <summary>One line for the status bar. Leads with the bad news, because that is the only part
    /// worth interrupting someone for; says nothing at all when everything ran, because a launcher
    /// congratulating itself after every launch is noise.</summary>
    public static string Summarize(string gameName, IReadOnlyList<LoaderOutcome>? outcomes)
    {
        var all = outcomes ?? Array.Empty<LoaderOutcome>();
        var failed = all.Where(o => o.Verdict == LoadVerdict.DidNotRun).ToList();
        if (failed.Count > 0)
            return $"{gameName} started. " + string.Join(" ", failed.Select(f => f.Detail));

        var ran = all.Count(o => o.Verdict == LoadVerdict.Ran);
        if (ran > 0 && all.All(o => o.Verdict == LoadVerdict.Ran))
            return $"{gameName} started, and {(ran == 1 ? "its loader" : $"all {ran} loaders")} loaded.";

        return "";   // nothing certain to report, so nothing said
    }

    // Modules come back as full paths or bare names depending on where they were read from.
    private static bool SameFile(string a, string b)
        => string.Equals(Path.GetFileName(a ?? ""), Path.GetFileName(b ?? ""), StringComparison.OrdinalIgnoreCase);
}
