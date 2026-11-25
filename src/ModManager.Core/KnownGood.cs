using System.Globalization;
using System.Text.Json;

namespace ModManager.Core;

/// <summary>One mod that was on when mods last loaded cleanly. Location plus name, because the same name in two mod
/// folders is two mods.</summary>
public sealed record KnownGoodMod(string Location, string Name);

/// <summary>The setup of the last modded launch where every loader verifiably ran: which mods were on, and the game
/// executable's date then.</summary>
public sealed record KnownGoodSetup(DateTime StampedUtc, DateTime? GameExeUtc, IReadOnlyList<KnownGoodMod> Enabled);

/// <summary>What changed between the last known good setup and now.</summary>
public sealed record KnownGoodDiff(
    DateTime StampedUtc,
    bool GameUpdated,
    IReadOnlyList<string> TurnedOn,
    IReadOnlyList<string> TurnedOff,
    IReadOnlyList<string> Removed)
{
    /// <summary>One plain sentence, or null when nothing changed. Leads with the update, because a game patch is the
    /// likeliest cause and the one the user did not do.</summary>
    public string? Describe()
    {
        var parts = new List<string>();
        if (GameUpdated) parts.Add("the game updated");
        if (TurnedOn.Count > 0) parts.Add("you turned on " + KnownGood.List(TurnedOn));
        if (TurnedOff.Count > 0) parts.Add("turned off " + KnownGood.List(TurnedOff));
        if (Removed.Count > 0) parts.Add("removed " + KnownGood.List(Removed));
        if (parts.Count == 0) return null;
        var when = StampedUtc.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture);
        return $"Since mods last loaded cleanly on {when}: {string.Join("; ", parts)}.";
    }
}

/// <summary>
/// "What broke", first slice (research backlog 2026-10-16, bet 3). <see cref="LaunchVerification"/> already knows
/// whether a launch's loaders ran. When every one did, on a modded launch, the setup is stamped as last known good. When
/// a later launch finds one did not, the status line it already writes gains the difference since: the game updated,
/// these mods were turned on, these turned off or removed. That answers "what did I change?", the question a broken
/// launch leaves.
///
/// <para>Read-only. It records and compares; it never toggles. Going back to the known good setup is the next slice,
/// and will go through the same reversible toggles as a row. Stored per game at <c>&lt;dataDir&gt;/known-good.json</c>,
/// camelCase, atomic; a missing or torn file reads as "no known good yet", never as an error.</para>
/// </summary>
public static class KnownGood
{
    private const string FileName = "known-good.json";
    private const int ListMax = 3;
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Stamp only a MODDED launch where at least one loader was checked and every one ran. "Could not tell" is
    /// not proof, and a vanilla launch is not the setup a broken modded launch should be compared with.</summary>
    public static bool ShouldStamp(IReadOnlyList<LoaderOutcome>? outcomes, LaunchMode mode)
        => mode == LaunchMode.Modded && outcomes is { Count: > 0 } && outcomes.All(o => o.Verdict == LoadVerdict.Ran);

    /// <summary>The setup as it stands: the mods that are on, leaving out the game's own files (they are always on and
    /// say nothing about what the user changed).</summary>
    public static KnownGoodSetup Snapshot(IEnumerable<Mod> mods, DateTime? gameExeUtc, DateTime stampedUtc)
        => new(stampedUtc, gameExeUtc,
            mods.Where(m => m.Enabled && !m.IsBase).Select(m => new KnownGoodMod(m.Location ?? "", m.Name))
                .Distinct().ToList());

    /// <summary>What changed since <paramref name="then"/>. A mod counts as turned on when it is on now and was not on
    /// then (installed since, or turned on); turned off when it was on and is now present but off; removed when it was
    /// on and is not there at all. The game counts as updated only when both executable dates are known.</summary>
    public static KnownGoodDiff Since(KnownGoodSetup then, IEnumerable<Mod> now, DateTime? gameExeUtcNow)
    {
        var current = now.Where(m => !m.IsBase).ToList();
        var wasOn = then.Enabled.ToHashSet();
        var isOn = current.Where(m => m.Enabled).Select(m => new KnownGoodMod(m.Location ?? "", m.Name)).ToHashSet();
        var present = current.Select(m => new KnownGoodMod(m.Location ?? "", m.Name)).ToHashSet();
        static List<string> Names(IEnumerable<KnownGoodMod> xs)
            => xs.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        return new KnownGoodDiff(
            then.StampedUtc,
            GameUpdated: then.GameExeUtc is { } a && gameExeUtcNow is { } b && b > a,
            TurnedOn: Names(isOn.Where(m => !wasOn.Contains(m))),
            TurnedOff: Names(wasOn.Where(m => present.Contains(m) && !isOn.Contains(m))),
            Removed: Names(wasOn.Where(m => !present.Contains(m))));
    }

    /// <summary>The status line for a finished launch: the verifier's own line, plus what changed since the last known
    /// good setup when some loader did not run. Unchanged otherwise, and unchanged when there is no known good yet.</summary>
    public static string Explain(string line, IReadOnlyList<LoaderOutcome>? outcomes, KnownGoodSetup? then,
        IEnumerable<Mod> now, DateTime? gameExeUtcNow)
    {
        if (then is null || outcomes is null || !outcomes.Any(o => o.Verdict == LoadVerdict.DidNotRun)) return line;
        return Since(then, now, gameExeUtcNow).Describe() is { } changed ? $"{line} {changed}".Trim() : line;
    }

    public static KnownGoodSetup? Load(string dataDir)
    {
        try
        {
            var path = Path.Combine(dataDir, FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<KnownGoodSetup>(File.ReadAllText(path), ReadOptions) : null;
        }
        catch { return null; }
    }

    public static void Save(string dataDir, KnownGoodSetup setup)
    {
        Directory.CreateDirectory(dataDir);
        AtomicJson.WriteJsonAtomic(Path.Combine(dataDir, FileName), setup);
    }

    internal static string List(IReadOnlyList<string> names)
    {
        if (names.Count <= ListMax)
            return names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
        return string.Join(", ", names.Take(ListMax)) + $" and {names.Count - ListMax} more";
    }
}
