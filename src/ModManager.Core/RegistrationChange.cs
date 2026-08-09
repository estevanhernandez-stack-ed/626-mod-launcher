namespace ModManager.Core;

/// <summary>What saving an edit would actually do. Produced by <see cref="RegistrationChange.Plan"/>.</summary>
public sealed record RegistrationChangePlan
{
    /// <summary>Field names (camelCase, matching the <c>GameEntry.UserSet*</c> constants) that differ.</summary>
    public required IReadOnlyList<string> FieldsChanged { get; init; }

    /// <summary>What the caller should write to <see cref="GameEntry.UserSet"/> on save — the fields
    /// changed here, plus everything already marked. Marks are never dropped by an unrelated edit.</summary>
    public required IReadOnlyList<string> FieldsToPin { get; init; }

    /// <summary>The data-dir move this edit implies, or null when it implies none.</summary>
    public DataDirMovePlan? DataDir { get; init; }

    /// <summary>Reasons this edit must not be saved as-is.</summary>
    public required IReadOnlyList<string> Blockers { get; init; }

    /// <summary>Consequences worth showing that are not blockers.</summary>
    public required IReadOnlyList<string> Notes { get; init; }

    public bool CanSave => Blockers.Count == 0;
}

/// <summary>
/// Works out what an edit to a game registration will actually do, before anything is saved.
///
/// <para>The UI renders this and never computes consequences itself. The move-or-pin prompt has to
/// name a real folder, a real byte count, and a real refusal reason — that is a decision, not a
/// rendering detail, and this repo has repeatedly learned that decisions parked in
/// <c>MainViewModel</c> (14 concrete service deps, unconstructible in tests) accumulate defects
/// until someone extracts them.</para>
///
/// <para>Pure, and does no IO of its own beyond delegating to <see cref="DataDirMove.Plan"/>.</para>
/// </summary>
public static class RegistrationChange
{
    public static RegistrationChangePlan Plan(GameEntry stored, GameEntry proposed)
    {
        var changed = new List<string>();
        var blockers = new List<string>();
        var notes = new List<string>();

        // THE IDENTITY RULE. Id is half the data-dir key (Scanner.DataDirForGame), so changing it
        // orphans every disabled mod, profile, and installed tool — silently, from what may have
        // looked like a cosmetic rename. An edit may never do this.
        if (!string.Equals(stored.Id, proposed.Id, StringComparison.Ordinal))
            blockers.Add("A game's id cannot change once it is registered — it is how the launcher "
                         + "finds this game's disabled mods, profiles, and installed tools. Rename the "
                         + "game instead; the id stays as it is.");

        if (!SameExtensions(stored.FileExtensions, proposed.FileExtensions))
            changed.Add(GameEntry.UserSetFileExtensions);

        // GroupingRule is a non-nullable string on GameEntry (defaults to ""), so no null guard here —
        // TreatWarningsAsErrors is on, and a dead ?? would not survive the build.
        if (!string.Equals(stored.GroupingRule.Trim(), proposed.GroupingRule.Trim(),
                StringComparison.OrdinalIgnoreCase))
            changed.Add(GameEntry.UserSetGroupingRule);

        if (!SameLocations(stored.ModLocations, proposed.ModLocations))
            changed.Add(GameEntry.UserSetModLocations);

        var rootChanged = !string.Equals(
            DataDirMove.Norm(stored.GameRoot), DataDirMove.Norm(proposed.GameRoot), StringComparison.OrdinalIgnoreCase);
        if (rootChanged) changed.Add(GameEntry.UserSetGameRoot);

        // Changing the engine changes which preset defaults apply, so a field that reads as
        // "untouched" under one engine may read as customised under another — quietly altering
        // whether future manifest corrections reach this game. Report it; the user decides.
        if (!string.Equals(stored.Engine ?? "", proposed.Engine ?? "", StringComparison.OrdinalIgnoreCase))
            notes.Add($"Changing the engine from '{stored.Engine}' to '{proposed.Engine}' changes which "
                      + "defaults this game is compared against, so it can change whether future "
                      + "definition updates reach it.");

        DataDirMovePlan? move = null;
        if (rootChanged && blockers.Count == 0)
        {
            move = DataDirMove.Plan(Scanner.DataDirForGame(stored), Scanner.DataDirForGame(proposed));
            if (move.Refusal is not null) blockers.Add(move.Refusal);
            if (move.Kind == DataDirMoveKind.Nothing && move.Refusal is null) move = null;
        }

        // Marks are additive. An edit to one field must never drop the mark on another — that would
        // silently re-expose a deliberate choice to being overwritten by a manifest correction.
        var pin = new List<string>(stored.UserSet ?? Array.Empty<string>());
        foreach (var f in changed)
            if (!pin.Contains(f, StringComparer.OrdinalIgnoreCase)) pin.Add(f);

        return new RegistrationChangePlan
        {
            FieldsChanged = changed,
            FieldsToPin = changed.Count == 0 ? (stored.UserSet ?? Array.Empty<string>()).ToList() : pin,
            DataDir = move,
            Blockers = blockers,
            Notes = notes,
        };
    }

    private static bool SameExtensions(IReadOnlyList<string> a, IReadOnlyList<string> b)
        => new HashSet<string>(a, StringComparer.OrdinalIgnoreCase)
            .SetEquals(new HashSet<string>(b, StringComparer.OrdinalIgnoreCase));

    // ModLocation is a positional record — ModLocation(string Name, string Label, string Path) — so
    // Name and Path are non-nullable. No ?? guards: TreatWarningsAsErrors is on and dead null-coalesce
    // on a non-nullable operand is exactly the kind of thing that breaks a build at the worst moment.
    // Label is deliberately not compared: it is display text, not part of where the mods are.
    private static bool SameLocations(IReadOnlyList<ModLocation> a, IReadOnlyList<ModLocation> b)
        => a.Count == b.Count
           && a.Zip(b).All(p =>
               string.Equals(p.First.Name, p.Second.Name, StringComparison.OrdinalIgnoreCase)
               && string.Equals(p.First.Path, p.Second.Path, StringComparison.OrdinalIgnoreCase));
}
