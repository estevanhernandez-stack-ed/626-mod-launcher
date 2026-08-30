namespace ModManager.Core.Transport;

/// <summary>What one game in an archive means for THIS machine.</summary>
/// <param name="Game">What the archive holds for it.</param>
/// <param name="RegisteredHere">Whether this machine already knows the game. False is the normal
/// case on a fresh install and is <b>not</b> an error — it is the whole reason the feature exists.</param>
/// <param name="MissingMods">Mods the archive records that are not installed here. Empty when the
/// game is not registered at all, because "you are missing 194 mods for a game you have not installed
/// yet" is noise dressed as information.</param>
public sealed record ProfileGameReport(
    ProfileGame Game,
    bool RegisteredHere,
    IReadOnlyList<BundleMod> MissingMods);

/// <summary>
/// What an archive holds, read against what this machine already has — <b>before anything is
/// touched</b>.
///
/// <para>Step two of the profile archive, and deliberately shippable on its own: it answers
/// <i>"what is in this thing?"</i> with no restore button anywhere near it. It is also the screen a
/// restore will later hang off, which is why it exists first — the report is the part that has to be
/// right, and the acting is the part that can wait.</para>
/// </summary>
public sealed record ProfileReport
{
    public ProfileArchiveManifest Manifest { get; init; } = new();
    public IReadOnlyList<ProfileGameReport> Games { get; init; } = Array.Empty<ProfileGameReport>();

    /// <summary>Games this machine already knows.</summary>
    public IReadOnlyList<ProfileGameReport> Here => Games.Where(g => g.RegisteredHere).ToList();

    /// <summary>Games the archive has and this machine does not. Waiting, not failing.</summary>
    public IReadOnlyList<ProfileGameReport> NotHere => Games.Where(g => !g.RegisteredHere).ToList();

    /// <summary>Files left out of the archive on purpose, by reason.</summary>
    public IReadOnlyDictionary<string, int> ExcludedByReason =>
        Manifest.Excluded.GroupBy(x => x.Reason).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// The one-line summary. Says what is in the file and how much of it this machine can already use,
    /// because a count with no denominator tells nobody anything.
    /// </summary>
    public string Headline
    {
        get
        {
            var total = Games.Count;
            if (total == 0) return "This archive has no games in it.";

            var here = Here.Count;
            var files = Manifest.TotalFiles;
            var made = ProfileReportText.WhenMade(Manifest.CreatedUtc);

            return $"{total} game{(total == 1 ? "" : "s")}, {files:N0} file{(files == 1 ? "" : "s")}"
                 + $" ({ProfileReportText.Human(Manifest.TotalBytes)})"
                 + (made.Length == 0 ? "" : $", backed up {made}")
                 + ". "
                 + (here == total
                     ? "You already have all of them."
                     : here == 0
                         ? "None of them are set up on this machine yet."
                         : $"{here} of them {(here == 1 ? "is" : "are")} set up here; "
                           + $"{total - here} {(total - here == 1 ? "is" : "are")} waiting on the game.");
        }
    }
}

/// <summary>Reading an archive against this machine.</summary>
public static class ProfileInspector
{
    /// <summary>
    /// Compare an archive's manifest with what is set up here.
    /// </summary>
    /// <param name="installedModsByGameId">Mod names per game this machine knows. A game ABSENT from
    /// this map is not registered here; a game present with an empty list is registered with no mods.
    /// The distinction matters — one is "install the game first", the other is "you are missing
    /// everything".</param>
    public static ProfileReport Inspect(
        ProfileArchiveManifest? manifest,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? installedModsByGameId)
    {
        if (manifest is null) return new ProfileReport();
        var known = installedModsByGameId ?? new Dictionary<string, IReadOnlyCollection<string>>();

        var games = new List<ProfileGameReport>();
        foreach (var g in manifest.Games)
        {
            var registered = known.TryGetValue(g.Game.Id, out var installed);

            // Only worth computing where the game exists here. Telling somebody they are missing 194
            // mods for a game they have not installed is noise dressed as information.
            var missing = registered
                ? SaveBundle.MissingMods(new SaveBundleManifest { Mods = g.Mods }, installed)
                : Array.Empty<BundleMod>();

            games.Add(new ProfileGameReport(g, registered, missing));
        }

        return new ProfileReport { Manifest = manifest, Games = games };
    }
}

/// <summary>Phrasing shared by the report and whatever renders it.</summary>
public static class ProfileReportText
{
    public static string Human(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }

    /// <summary>When the archive was made, in local time, or empty when it does not say. Round-trip
    /// parsed — the manifest writes ISO 8601 and a locale-sensitive read would drift.</summary>
    public static string WhenMade(string? createdUtc)
        => DateTime.TryParse(createdUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
            ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "";

    /// <summary>One line per game, for the list.</summary>
    public static string DetailFor(ProfileGameReport g)
    {
        var parts = new List<string>();
        if (g.Game.SaveIncluded) parts.Add($"{g.Game.SaveFileCount:N0} save files ({Human(g.Game.SaveBytes)})");
        if (g.Game.Mods.Count > 0) parts.Add($"{g.Game.Mods.Count} mod{(g.Game.Mods.Count == 1 ? "" : "s")}");
        if (g.Game.DataFileCount > 0) parts.Add("settings");
        if (parts.Count == 0) parts.Add("nothing");

        var line = string.Join("  ·  ", parts);
        if (!g.RegisteredHere) return line + "  ·  not set up here yet";
        if (g.MissingMods.Count > 0)
            return line + $"  ·  {g.MissingMods.Count} mod{(g.MissingMods.Count == 1 ? "" : "s")} not installed here";
        return line;
    }
}
