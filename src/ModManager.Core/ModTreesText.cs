namespace ModManager.Core;

/// <summary>
/// What a mod's row says about its files in the game's other mod folders (B4 stage two, "toggle"). Pure
/// text from three lists, so the wording lives in Core under test and the App only binds it.
///
/// <para>The three lists come from the toggle's own selection (<see cref="ExtraTreeRows"/>), never from a
/// second rule in the App: <paramref name="moving"/> is the trees whose entries turn off and on with the
/// mod, <paramref name="heldBack"/> the trees where an entry with its name stays put because a safety rule
/// kept it there, and <paramref name="heldWhileOff"/> the trees held in <c>disabled-trees/&lt;Mod&gt;</c>
/// while the mod is off. A tree can be in both <paramref name="moving"/> and <paramref name="heldBack"/>
/// (one entry moves, another is kept); the line lists it once and the tooltip says SOME of its files
/// stay.</para>
/// </summary>
public sealed record ModTreesText(string Line, string Tooltip)
{
    public static readonly ModTreesText None = new("", "");

    public bool Visible => Line.Length > 0;

    public const string MovingTooltip = "626 turns these on and off with the mod.";
    public const string OffTooltip = "626 turned these off with the mod. Turning it on puts them back.";

    public static ModTreesText For(
        IEnumerable<string> moving, IEnumerable<string> heldBack, IEnumerable<string> heldWhileOff)
    {
        var move = Distinct(moving);
        var back = Distinct(heldBack);
        var off = Distinct(heldWhileOff);

        string? backSentence = null;
        if (back.Count > 0)
        {
            var partly = back.Any(t => move.Contains(t, StringComparer.OrdinalIgnoreCase));
            backSentence = (partly ? "Some files in " : "Files in ") + string.Join(", ", back)
                + " stay where they are: 626 can't tell they belong only to this mod.";
        }

        if (off.Count > 0)
            return new ModTreesText("Also turned off in " + string.Join(", ", off),
                Join(OffTooltip, backSentence));

        var line = Distinct(move.Concat(back));
        if (line.Count == 0) return None;
        return new ModTreesText("Also has files in " + string.Join(", ", line),
            Join(move.Count > 0 ? MovingTooltip : null, backSentence));
    }

    /// <summary>The status line after a turn-on that left files in <c>disabled-trees/&lt;Mod&gt;</c>: the mod
    /// is on, and the user is told where the rest went. One sentence plus the path.</summary>
    public static string LeftoverStatus(string modName, string heldPath)
        => $"{modName} is on, but some of its files are still held in {heldPath}. 626 couldn't tell where they go.";

    private static string Join(string? a, string? b)
        => string.Join(" ", new[] { a, b }.Where(s => !string.IsNullOrEmpty(s)));

    private static List<string> Distinct(IEnumerable<string>? trees)
    {
        var result = new List<string>();
        foreach (var t in trees ?? Enumerable.Empty<string>())
            if (!string.IsNullOrWhiteSpace(t) && !result.Contains(t, StringComparer.OrdinalIgnoreCase))
                result.Add(t);
        return result;
    }
}
