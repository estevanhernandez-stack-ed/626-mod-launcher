namespace ModManager.Core;

/// <summary>
/// What a mod's row says about its files in the game's other mod folders (B4 stage two, "toggle"). Pure
/// text from the toggle's own selection (<see cref="ExtraTreeRows"/>), so the wording lives in Core under
/// test and the App only binds it.
///
/// <para>Inputs: <c>moving</c>, the trees whose entries turn off and on with the mod; <c>heldBack</c>, the
/// trees where an entry with its name stays put, each with the rule that kept it, worded per rule so the
/// row always gives the true reason; <c>heldWhileOff</c>, the trees held in <c>disabled-trees/&lt;Mod&gt;</c>
/// while the mod is off; <c>stillOn</c>, for an off mod, the trees whose entries are still live (turned
/// off before stage two, say); and <c>heldUnknownPath</c>, the holding folder when files are held there
/// but 626 can't say under which tree. A tree can be both moving and held back (one entry moves, another
/// is kept); the line lists it once and the tooltip says SOME of its files stay.</para>
/// </summary>
public sealed record ModTreesText(string Line, string Tooltip)
{
    public static readonly ModTreesText None = new("", "");

    public bool Visible => Line.Length > 0;

    public const string MovingTooltip = "626 turns these on and off with the mod.";
    public const string OffTooltip = "626 turned these off with the mod. Turning it on puts them back.";
    public const string RowNotMovedTooltip =
        "626 doesn't move this mod's files in these folders. They stay where they are, on or off.";
    public const string StillOnTooltip =
        "These files didn't move when the mod was turned off. Turn it on and off again to move them.";
    public const string UnknownHeldTooltip = "626 couldn't read which folders these came from.";

    public static ModTreesText For(
        IEnumerable<string> moving, IEnumerable<HeldTree> heldBack, IEnumerable<string> heldWhileOff,
        IEnumerable<string>? stillOn = null, string? heldUnknownPath = null)
    {
        var move = Distinct(moving);
        var back = DistinctHeld(heldBack);
        var off = Distinct(heldWhileOff);
        var still = Distinct(stillOn);
        var backSentences = HeldSentences(back, move);

        // Off: what is held, and what did not move, in one line.
        var heldPart = off.Count > 0 ? "Also turned off in " + string.Join(", ", off)
            : !string.IsNullOrEmpty(heldUnknownPath) ? $"Some files are held in {heldUnknownPath}."
            : null;
        if (heldPart is not null || still.Count > 0)
        {
            var stillLine = still.Count > 0 ? $"Files in {string.Join(", ", still)} are still on." : null;
            var line = heldPart is null ? stillLine!
                : stillLine is null ? heldPart
                : EndSentence(heldPart) + " " + stillLine;

            var heldTip = off.Count > 0 ? OffTooltip : heldPart is not null ? UnknownHeldTooltip : null;
            var stillTip = still.Count == 0 ? null
                : heldPart is null ? StillOnTooltip
                : $"Files in {string.Join(", ", still)} didn't move when the mod was turned off. "
                  + "Turn it on and off again to move them.";
            return new ModTreesText(line, Join(new[] { heldTip, stillTip }.Concat(backSentences)));
        }

        var trees = Distinct(move.Concat(back.Select(h => h.Tree)));
        if (trees.Count == 0) return None;
        var tooltip = back.Any(h => h.Reason == HeldReason.RowNotMoved) && move.Count == 0
            ? RowNotMovedTooltip
            : Join(new[] { move.Count > 0 ? MovingTooltip : null }.Concat(backSentences));
        return new ModTreesText("Also has files in " + string.Join(", ", trees), tooltip);
    }

    /// <summary>The status line after a turn-on that may have left files in <c>disabled-trees/&lt;Mod&gt;</c>.
    /// The mod is on; the user is told where the rest is, or that 626 couldn't look.</summary>
    public static string LeftoverStatus(string modName, TreeLeftover leftover)
        => leftover.Readable
            ? $"{modName} is on, but some of its files are still held in {leftover.Path}. 626 couldn't tell where they go."
            : $"{modName} is on, but 626 couldn't read {leftover.Path} to check for leftover files.";

    // One sentence per reason, in a fixed order. "Some files in" when any named tree also has an entry
    // that moves. RowNotMoved is the whole tooltip on its own, never a sentence here.
    private static IEnumerable<string> HeldSentences(IReadOnlyList<HeldTree> back, IReadOnlyList<string> move)
    {
        var cantTell = back.Where(h => h.Reason is HeldReason.Contested or HeldReason.Protected)
            .Select(h => h.Tree).ToList();
        if (cantTell.Count > 0)
            yield return Lead(cantTell, move) + " stay where they are: 626 can't tell they belong only to this mod.";

        var owned = back.Where(h => h.Reason == HeldReason.OwnedTree).Select(h => h.Tree).ToList();
        if (owned.Count > 0)
            yield return Lead(owned, move) + " stay where they are: another tool manages "
                + (owned.Count == 1 ? "that folder." : "those folders.");
    }

    private static string Lead(IReadOnlyList<string> trees, IReadOnlyList<string> move)
        => (trees.Any(t => move.Contains(t, StringComparer.OrdinalIgnoreCase)) ? "Some files in " : "Files in ")
           + string.Join(", ", trees);

    private static string EndSentence(string s) => s.EndsWith('.') ? s : s + ".";

    private static string Join(IEnumerable<string?> parts)
        => string.Join(" ", parts.Where(s => !string.IsNullOrEmpty(s)));

    private static List<string> Distinct(IEnumerable<string>? trees)
    {
        var result = new List<string>();
        foreach (var t in trees ?? Enumerable.Empty<string>())
            if (!string.IsNullOrWhiteSpace(t) && !result.Contains(t, StringComparer.OrdinalIgnoreCase))
                result.Add(t);
        return result;
    }

    private static List<HeldTree> DistinctHeld(IEnumerable<HeldTree>? held)
    {
        var result = new List<HeldTree>();
        foreach (var h in held ?? Enumerable.Empty<HeldTree>())
            if (!string.IsNullOrWhiteSpace(h.Tree)
                && !result.Any(r => string.Equals(r.Tree, h.Tree, StringComparison.OrdinalIgnoreCase)))
                result.Add(h);
        return result;
    }
}

/// <summary>What <see cref="Scanner.ExtraTreeLeftover"/> found after a turn-on: the mod's holding folder,
/// and whether 626 could read it. <c>Readable == false</c> means it could not look, NOT that files are
/// there.</summary>
public sealed record TreeLeftover(string Path, bool Readable);
