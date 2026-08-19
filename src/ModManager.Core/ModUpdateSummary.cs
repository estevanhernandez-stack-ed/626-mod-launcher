using System.Text.Json;

namespace ModManager.Core;

/// <summary>Why a mod is listed as pending. The row renders differently for each, because only one
/// of them supports a version comparison (A10).</summary>
public enum PendingReason
{
    /// <summary>We know both versions and they differ. A pair can honestly be shown.</summary>
    VersionDiffers,
    /// <summary>Nexus's own per-user flag says the user is behind. It rides in on a search hit and
    /// carries no version, so there is no left-hand side and no pair to draw — rendering one anyway is
    /// how "unknown -> 1.2.1" filled twenty consecutive rows.</summary>
    NexusFlag,
}

/// <summary>One mod with a newer version available on Nexus than what's installed.</summary>
public sealed record PendingUpdate(string GameId, string GameName, string ModKey, string ModName,
    string? InstalledVersion, string? LatestVersion, int? NexusModId, string? NexusDomain,
    PendingReason Reason = PendingReason.VersionDiffers)
{
    /// <summary>True when the two version strings mean the same release — "1" and "1.0", "1.0.0" and
    /// "1.0". A pair like that is not an update to show; Nexus may still be right that a newer FILE
    /// exists, so the row stays and only its wording changes.</summary>
    public bool VersionsLookEquivalent =>
        !string.IsNullOrWhiteSpace(InstalledVersion)
        && !string.IsNullOrWhiteSpace(LatestVersion)
        && NormalizeVersion(InstalledVersion!) == NormalizeVersion(LatestVersion!);

    /// <summary>True only when the latest version is PROVABLY newer than the installed one: both
    /// parse as plain dotted numbers and the comparison ascends. Anything else — a suffix like
    /// "1.0.0.1-hotfix", a descending pair, an unknown side — is false.
    ///
    /// <para>The arrow in the row is a claim about DIRECTION, and it may only be drawn where that claim
    /// holds. A10 declined to order arbitrary version strings, correctly, and then drew the arrow
    /// whenever two known versions differed — so the launcher kept inviting the user to "update" from
    /// 1.0.1 to 1.0.0, which is one of the two examples A10 was filed about (A27).</para>
    ///
    /// <para>Refusing to guess costs the suffix case: "1.0.0.1-hotfix" probably IS newer, and this
    /// says nothing about it. Saying less is the point — the row names both versions either way.</para>
    /// </summary>
    public bool LatestIsProvablyNewer => Compare(InstalledVersion, LatestVersion) is int c && c < 0;

    /// <summary>True when the listed version is PROVABLY older than what is installed — both parse as
    /// plain dotted numbers and the comparison descends.
    ///
    /// <para>This is the inclusion rule A10 never got to. A10 fixed what a row SAYS: it stopped
    /// drawing "1.0.1 → 1.0.0", an arrow pointing backwards. It did not change whether the row
    /// EXISTS, because the pending decision was string inequality — <c>latest != installed</c> —
    /// which never asks which is newer. So both of A10's own named examples were still listed a wave
    /// later, reading "1.0.1 installed · Nexus lists 1.0.0". Carefully worded, still an invitation to
    /// update to an older release.</para>
    ///
    /// <para>Only the PROVABLE case is dropped. An unorderable pair like "1.0.0.1" against
    /// "1.0.0.1-hotfix" stays listed and says nothing about direction: the hotfix probably is newer,
    /// and refusing to guess has to cut both ways or it is not a principle.</para></summary>
    public static bool LatestIsProvablyOlder(string? installed, string? latest)
        => Compare(installed, latest) is int c && c > 0;

    /// <summary>Numeric-only ordering, or null when either side is not a plain dotted number. Null is
    /// the answer that matters: it means "not comparable", never "equal".</summary>
    internal static int? Compare(string? a, string? b)
    {
        var left = Numeric(a);
        var right = Numeric(b);
        if (left is null || right is null) return null;
        for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
        {
            var x = i < left.Count ? left[i] : 0;
            var y = i < right.Count ? right[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    // Every segment must be digits. A single non-numeric part makes the whole string unorderable —
    // deliberately strict, because a lenient parse is the guessing this exists to avoid.
    private static List<int>? Numeric(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var parts = v.Trim().TrimStart('v', 'V').Split('.');
        var nums = new List<int>(parts.Length);
        foreach (var p in parts)
        {
            if (p.Length == 0 || !p.All(char.IsAsciiDigit) || !int.TryParse(p, out var n)) return null;
            nums.Add(n);
        }
        return nums.Count == 0 ? null : nums;
    }

    /// <summary>Trailing zero segments dropped, so 1.0.0 == 1.0 == 1. Deliberately NOT an ordering:
    /// mod versions are not reliably semver, and a comparator that guessed which side is newer would
    /// produce a confidently wrong DIRECTION — the fault this is here to remove.</summary>
    internal static string NormalizeVersion(string v)
    {
        var parts = v.Trim().TrimStart('v', 'V').Split('.', StringSplitOptions.TrimEntries).ToList();
        while (parts.Count > 1 && (parts[^1] == "0" || parts[^1].Length == 0)) parts.RemoveAt(parts.Count - 1);
        return string.Join('.', parts);
    }
}

/// <summary>What we know about one game's updates, from its already-persisted metadata.json alone.
/// <see cref="Checked"/> = false means the game has never had a Nexus refresh — that is NOT the same
/// as "up to date" (which is Checked = true, Count = 0), and callers must render it as "unknown"
/// (no badge, distinct empty state), never as "0 updates". Conflating the two states is the one bug
/// this type exists to make impossible.</summary>
public sealed record GameUpdateSummary(string GameId, string GameName, bool Checked,
    IReadOnlyList<PendingUpdate> Pending)
{
    private IReadOnlyList<PendingUpdateGroup>? _groups;

    /// <summary>The pending updates collapsed to one entry per mod (A11).</summary>
    public IReadOnlyList<PendingUpdateGroup> Groups => _groups ??= PendingUpdateGroups.Group(Pending);

    /// <summary>How many MODS are pending — the number a badge shows and a person counts. It follows
    /// the rows rather than the files, because "115 updates" on an install with a fraction of that
    /// many mods is the same overstatement in badge form. Zero here still means zero pending, so the
    /// checked / never-checked distinction is untouched.</summary>
    public int Count => Groups.Count;
}

/// <summary>
/// Pure reader: "what needs updating" aggregated from per-game metadata.json files that a prior,
/// user-initiated Nexus refresh already wrote (<see cref="Mod.NexusLatestVersion"/> via
/// <c>Scanner.SaveMetadata</c> / <c>WriteManyMeta</c>). Deliberately does NOT call Nexus and does
/// NOT re-scan the game folder — those are the existing, explicit user actions (open the game, hit
/// Refresh). This type only reports what's already on disk, so the Library badge and the cross-game
/// Updates view can answer "what needs updating" without any new network traffic or file-system walk.
///
/// The pending rule mirrors <see cref="Mod.UpdateAvailable"/> exactly — Nexus's own per-user flag
/// when we have it, the installed-vs-latest compare when we do not, silence when neither is
/// known — with one addition: a blank (whitespace-only) <c>NexusLatestVersion</c> is treated
/// the same as null — never pending, and never enough on its own to flip <see cref="GameUpdateSummary.Checked"/>
/// to true. That keeps a stray empty string from ever being misread as "this game was checked."
/// </summary>
public static class ModUpdateSummary
{
    // Matches Scanner's private metadata JsonSerializerOptions field-for-field (case-insensitive
    // reads, camelCase on write) so this reader deserializes the exact same on-disk shape. Scanner's
    // options object isn't public, and this task must not modify Scanner.cs to expose it — the values
    // below are the source of truth to keep in sync with Scanner.cs if that ever changes.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Reads one game's metadata.json (if any) and reports its pending updates. Never
    /// throws: a missing file, an empty file, malformed JSON, or an unreadable directory all yield
    /// <c>Checked = false</c> with an empty list — the same "unknown" state as "never refreshed."</summary>
    public static GameUpdateSummary ForGame(GameEntry game)
    {
        var gameId = game.Id ?? "";
        var gameName = game.GameName ?? "";

        Dictionary<string, ModMeta>? meta;
        try
        {
            var path = Path.Combine(Scanner.DataDirForGame(game), "metadata.json");
            if (!File.Exists(path)) return new GameUpdateSummary(gameId, gameName, false, Array.Empty<PendingUpdate>());
            meta = JsonSerializer.Deserialize<Dictionary<string, ModMeta>>(File.ReadAllText(path), JsonOpts);
        }
        catch
        {
            // Malformed JSON, unreadable file/directory, etc. — "unknown", not a crash.
            return new GameUpdateSummary(gameId, gameName, false, Array.Empty<PendingUpdate>());
        }

        if (meta is null || meta.Count == 0)
            return new GameUpdateSummary(gameId, gameName, false, Array.Empty<PendingUpdate>());

        var checkedAny = false;
        var pending = new List<PendingUpdate>();
        foreach (var (key, m) in meta)
        {
            var latest = m.NexusLatestVersion;
            var polled = !string.IsNullOrWhiteSpace(latest);
            // Being TOLD is a form of having looked. A row carrying Nexus's own per-user flag counts
            // the game as checked even when no version was ever fetched — otherwise "0 updates"
            // renders as "never checked", which is the exact honesty distinction this type exists for.
            if (!polled && m.NexusUpdateAvailable is null) continue;
            checkedAny = true;

            // MIRRORS Mod.UpdateAvailable EXACTLY — three inputs, same precedence. The chip and this
            // badge read the same persisted fields, so a divergence shows the user an UPDATE chip and
            // then an empty Updates view. Nexus's flag outranks the compare because Nexus knows which
            // FILE the user downloaded and we often do not; the compare governs only when we were
            // never told; and an unknown installed version can never be "behind", because not knowing
            // what you have is a reason to stay quiet rather than a difference.
            var behind = m.NexusUpdateAvailable
                ?? (polled && !string.IsNullOrWhiteSpace(m.Version) && latest != m.Version
                    // ...and not a difference that points BACKWARDS. Inequality alone listed both of
                    // A10's named examples for another two waves.
                    && !PendingUpdate.LatestIsProvablyOlder(m.Version, latest));
            if (!behind) continue;

            var modName = string.IsNullOrWhiteSpace(m.Title) ? key : m.Title;
            // Carry WHY. A row listed on Nexus's flag has no version comparison behind it, and the
            // view must not draw an arrow between two things it was never told (A10).
            var reason = m.NexusUpdateAvailable == true
                ? PendingReason.NexusFlag
                : PendingReason.VersionDiffers;
            pending.Add(new PendingUpdate(gameId, gameName, key, modName, m.Version, latest,
                m.NexusModId, game.NexusGameDomain, reason));
        }

        return new GameUpdateSummary(gameId, gameName, checkedAny, pending);
    }

    /// <summary>Runs <see cref="ForGame"/> across every registered game, preserving each game's own
    /// identity and Checked state — one game's "never refreshed" never contaminates another's count.</summary>
    public static IReadOnlyList<GameUpdateSummary> ForGames(IEnumerable<GameEntry> games)
        => games.Select(ForGame).ToList();
}
