using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>A mod location resolved to absolute paths (+ mirrors), with primary flag.</summary>
public sealed record ModLocationCtx(string Name, string Label, string Abs, IReadOnlyList<string> Mirrors, bool Primary)
{
    // Resolved form ("files" | "folders") and the managing tool ("vortex"/...), if any.
    public string Form { get; init; } = "files";
    public string? Managed { get; init; }
}

/// <summary>
/// A registry game entry resolved into an absolute working context: where mods live,
/// where the launcher's own data (disabled/profiles/classification/metadata) lives, and
/// how files group. Produced by Scanner.GameContext. Mirrors scanner.js gameContext.
/// </summary>
public sealed class GameContext
{
    public required GameEntry Game { get; init; }
    public required string GameRoot { get; init; }
    public required string DataDir { get; init; }
    public required string DisabledRoot { get; init; }
    public required string ProfilesDir { get; init; }
    public required string SavesDir { get; init; }
    public required string ClassificationPath { get; init; }
    public required string MetadataPath { get; init; }
    public required string LoadOrderPath { get; init; }
    public string? SaveDir { get; init; }
    /// <summary>
    /// The extensions this game is RESOLVED to scan with: the stored registration value with
    /// <see cref="RegistrationRefresh"/> applied (so a manifest correction has already won), then
    /// lowercased and stripped of leading dots, with anything that was nothing but dots dropped.
    /// <see cref="Exts"/> is derived from this, so this is the value <see cref="FileRe"/> is built
    /// from and therefore the one <c>Scanner.ModKeyFor</c> keys on — anything that must agree with
    /// the scanner reads THIS, and comparing against it needs no normalisation of its own.
    ///
    /// <para>The dot-stripping is not cosmetic. A hand-written registration stores <c>".archive"</c>
    /// as readily as <c>"archive"</c>, the regex build has always trimmed it, and
    /// <c>DiscoverySweep.Extension</c> yields a dot-less extension — so a raw list made the scanner
    /// list a game's mods perfectly while the sweep called none of them engine-shaped. Normalising
    /// once, into the value both readers take, is what keeps them from drifting apart again.</para>
    ///
    /// <para>It differs from both neighbours, and confusing the three is the whole reason it exists:</para>
    /// <list type="bullet">
    /// <item><description><see cref="GameEntry.FileExtensions"/> on <see cref="Game"/> is the RAW
    /// stored value, frozen the day the user clicked Add. It may be a stale preset snapshot the
    /// manifest has since corrected — a Cyberpunk 2077 registration stored <c>["pak"]</c> while the
    /// manifest said <c>["archive"]</c>. Reading it makes a caller disagree with the scanner about
    /// what a mod even is.</description></item>
    /// <item><description><see cref="Exts"/> is this list taken the last two steps to a regex: an
    /// empty list is replaced with <c>["pak"]</c>, and every entry is <c>Regex.Escape</c>d. Both
    /// steps make it the wrong answer to "does this game scan that extension" — the substitution
    /// claims a pak engine for the folder-based engines (smapi / fromsoft / decima) that genuinely
    /// have no extensions, and the escaping means a plain <c>Contains</c> misses any extension
    /// holding a regex metacharacter. The intake sites compare against it anyway, deliberately and
    /// with a known cost; see the note on <see cref="Exts"/>.</description></item>
    /// </list>
    ///
    /// <para>Empty is meaningful here and is preserved: it is how a folder-based, catalog-driven
    /// engine is told apart from an extension-based one.</para>
    /// </summary>
    public required IReadOnlyList<string> DeclaredExts { get; init; }

    /// <summary><see cref="DeclaredExts"/> taken the last two steps to <see cref="FileRe"/> — an
    /// empty list substituted with <c>["pak"]</c>, then every entry regex-escaped.
    ///
    /// <para>Prefer <see cref="DeclaredExts"/> for any comparison. The exception, and it is a real
    /// one: the intake sites pass this to <c>Intake.ClassifyDrop</c>, which DOES compare it by
    /// equality. They do that for the empty→<c>["pak"]</c> substitution, which they genuinely need
    /// and <see cref="DeclaredExts"/> does not carry — so the escaping rides along as a known,
    /// latent wrong answer for any extension holding a regex metacharacter (a game declaring
    /// <c>mod+pak</c> gets <c>["mod\+pak"]</c> here, and a real <c>foo.mod+pak</c> classifies as
    /// skip). Backlogged as A7; do not "fix" those sites by swapping in
    /// <see cref="DeclaredExts"/>, which would break the substitution they rely on.</para></summary>
    public required IReadOnlyList<string> Exts { get; init; }
    public required Regex FileRe { get; init; }
    public required IReadOnlyList<ModLocationCtx> Locations { get; init; }
    public required string GroupingRule { get; init; }
    public required string ScanSubfolders { get; init; }
    public bool HasGame { get; init; }

    /// <summary>Folders the user has taken over from another manager (loaded from taken-over.json).
    /// Posture reads this so a taken-over folder is managed despite a lingering marker. Case-insensitive.</summary>
    public IReadOnlySet<string> TakenOver { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>An item intake skipped, with why (already installed / not a mod file / error).</summary>
public sealed record SkippedItem(string Name, string Reason);

/// <summary>The outcome of an intake: what was placed and what was skipped (with reasons).</summary>
public sealed class IntakeResult
{
    public List<string> Added { get; } = new();
    public List<string> Updated { get; } = new();
    public List<SkippedItem> Skipped { get; } = new();
}

/// <summary>Result of a name-search metadata refresh.</summary>
public sealed record RefreshResult(int Matched, int Total, int? GameId);

/// <summary>Result of a fingerprint identification pass.</summary>
public sealed record IdentifyResult(int Matched);

/// <summary>Options for a metadata refresh (an explicit gameId overrides resolution).</summary>
public sealed class RefreshOptions
{
    public int? GameId { get; init; }
}
