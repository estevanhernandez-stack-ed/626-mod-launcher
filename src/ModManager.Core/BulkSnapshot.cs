namespace ModManager.Core;

/// <summary>
/// Naming the profile a bulk operation saves before it runs.
///
/// <para><b>Why a bulk op saves anything at all.</b> Every file a bulk operation moves is reversible —
/// disabling moves to the holding folder and nothing is deleted. What is NOT reversible is the
/// KNOWLEDGE: press <c>Disable all</c> on a 200-mod install and every file lands safely in holding,
/// while the fact that 140 of those 200 were on is gone. <c>Enable all</c> does not undo it; it turns
/// on all 200, including the sixty the user had deliberately switched off.</para>
///
/// <para>The launcher already has the thing that fixes this and never connected it: a profile saves
/// exactly that set. So a bulk op saves one first, under a name that says what it was taken before —
/// and the status line says where it went, because an undo nobody is told about is not an undo.</para>
/// </summary>
public static class BulkSnapshot
{
    /// <summary>The prefix every automatic snapshot carries, so a person scanning the profiles list can
    /// tell what they made from what the launcher made for them.</summary>
    public const string Prefix = "Before ";

    /// <summary>Name the snapshot for the operation it precedes and the moment it was taken. The
    /// timestamp is the caller's, not <c>DateTime.Now</c>, so this stays pure and testable — and so a
    /// caller can hand it the same instant it stamps elsewhere.</summary>
    public static string NameFor(string operation, DateTime whenLocal)
    {
        var what = Clean(operation);
        if (what.Length == 0) what = "bulk change";
        // HH.mm, not HH:mm. A colon is illegal in a Windows filename, and this name becomes one.
        // Profile.SafeProfileName would sanitise it at the write, but a name that is only safe
        // because something downstream repairs it is a name waiting to be used somewhere else.
        return $"{Prefix}{what} {whenLocal:yyyy-MM-dd HH.mm}";
    }

    /// <summary>True when a profile name looks like one of ours. Lets a caller offer to tidy old
    /// automatic snapshots without touching a profile the user named and relies on.</summary>
    public static bool IsAutomatic(string? profileName)
        => (profileName ?? "").StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>What the status line says after one is taken. Names the profile, because a snapshot the
    /// user cannot find is the same as no snapshot.</summary>
    public static string Reassurance(string profileName)
        => $"Saved your current set as \"{profileName}\" — restore it from Profiles.";

    // Collapse whitespace and strip the characters a profile file name cannot carry. SafeProfileName
    // sanitises again at the write; this only keeps the DISPLAY sensible.
    private static string Clean(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length == 0) return "";
        var chars = t.Where(c => !char.IsControl(c) && c is not ('/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'));
        return string.Join(" ", new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
