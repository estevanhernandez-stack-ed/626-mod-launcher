namespace ModManager.Core;

/// <summary>
/// Lets a corrected game definition reach a game the user already added.
///
/// <para>A registration copies its engine preset's values ONCE, when the game is added, and freezes
/// them into <c>games.json</c>. The manifest feed keeps improving those definitions — and nothing
/// ever re-applied a correction to an existing registration. That silently voids the feed's central
/// promise, <i>"a new game on a known engine needs no app release"</i>, for precisely the users who
/// have had the launcher longest: every definition fix reaches only people who had NOT yet added
/// that game.</para>
///
/// <para>Measured: a Cyberpunk 2077 registration carried <c>fileExtensions ["pak"]</c> — the custom
/// preset's default — while the manifest said <c>["archive"]</c>. <c>Scanner</c> builds its
/// file-matching regex from the stored value, so 194 <c>.archive</c> mods on disk showed as ZERO
/// mods in the launcher. Nothing was broken; the launcher was looking for the wrong thing.</para>
///
/// <para>THE RULE: prefer the manifest when the stored value is still the untouched preset default.
/// A value the user chose is evidence about THEIR install that the manifest cannot have, so it
/// always wins — overriding it would be the launcher deciding it knows better. A value the user
/// never touched is just a snapshot of what we believed on the day they clicked Add, and there is no
/// reason to prefer a stale belief to a current one.</para>
///
/// <para>Read at context-build time and never written back: this changes what the scanner LOOKS FOR,
/// not what is on disk. A correction that reached into the user's file would need their consent;
/// one that only changes a search does not — and keeping <c>games.json</c> untouched means the user
/// can still see, and still edit, exactly what they chose.</para>
/// </summary>
public static class RegistrationRefresh
{
    /// <summary>The extensions to actually scan with. <paramref name="userSet"/> is checked FIRST and
    /// wins outright: it is the one signal here that is not an inference. The untouched-default test
    /// stays underneath as the fallback for registrations that predate the marker.</summary>
    public static IReadOnlyList<string> Extensions(
        IReadOnlyList<string> stored, IReadOnlyList<string> presetDefault,
        IReadOnlyList<string>? manifest, bool userSet = false)
        => userSet ? stored
         : manifest is { Count: > 0 } && IsUntouched(stored, presetDefault) ? manifest
         : stored;

    /// <summary>The grouping rule to actually group with. Same freeze, same rule, same precedence.</summary>
    public static string? Grouping(
        string? stored, string? presetDefault, string? manifest, bool userSet = false)
        => userSet ? stored
         : !string.IsNullOrWhiteSpace(manifest)
           && string.Equals(stored?.Trim() ?? "", presetDefault?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
            ? manifest
            : stored;

    /// <summary>
    /// Whether the stored list is still exactly the preset's, ignoring order and case.
    ///
    /// <para>Neither order nor case is a choice a user makes — <c>["ucas","pak"]</c> is the same
    /// snapshot as <c>["pak","ucas"]</c>, and a stored <c>"PAK"</c> is the preset's <c>"pak"</c>.
    /// Counting either as customisation would strand a registration forever on a difference that
    /// means nothing. A stored list that merely CONTAINS the preset's values is a different matter:
    /// the extra entry is a real choice, so the whole list is treated as the user's.</para>
    /// </summary>
    private static bool IsUntouched(IReadOnlyList<string> stored, IReadOnlyList<string> presetDefault)
        => ExtensionSet(stored).SetEquals(ExtensionSet(presetDefault));

    /// <summary>
    /// One spelling of "the same extension list", shared with <c>RegistrationChange.SameExtensions</c>.
    ///
    /// <para>These two used to normalise the same concept differently — the planner trimmed, this file
    /// did not — and the disagreement ended self-healing without a trace. An edit dialog round-trips a
    /// text field into <c>[" pak"]</c>; the planner reports NO change and pins NOTHING, yet the
    /// untouched-default test above now reads the list as customised, the manifest stops reaching the
    /// game, and the 194-mods-showing-as-zero failure comes back with no marker to explain it. Two
    /// callers, one helper.</para>
    ///
    /// <para>Leading dots are deliberately NOT stripped: the list is compared as the user stores it,
    /// and <c>Scanner</c> normalises the dot case separately on its way to a regex.</para>
    /// </summary>
    internal static HashSet<string> ExtensionSet(IEnumerable<string> exts)
        => new(exts.Select(e => e.Trim()), StringComparer.OrdinalIgnoreCase);
}
