namespace ModManager.Core;

/// <summary>What saving an edit would actually do. Produced by <see cref="RegistrationChange.Plan"/>.</summary>
public sealed record RegistrationChangePlan
{
    /// <summary>
    /// Field names (camelCase, matching the <c>GameEntry.UserSet*</c> constants) that differ.
    ///
    /// <para>The four pinnable fields only. This is NOT a full diff of the two entries: <c>gameName</c>,
    /// <c>dataDir</c>, <c>saveDir</c>, and <c>ModLocation.Form</c> / <c>.Managed</c> / <c>.Mirrors</c>
    /// are deliberately outside it because nothing self-heals them. A UI showing "what will change"
    /// must render those itself — a rename yields an EMPTY list here, and that means "nothing gets
    /// pinned", not "nothing happens".</para>
    /// </summary>
    public required IReadOnlyList<string> FieldsChanged { get; init; }

    /// <summary>
    /// Field names that changed but carry no pin and no data-dir move — they simply save.
    ///
    /// <para>Exists because <see cref="FieldsChanged"/> is deliberately the four PINNABLE fields, so a
    /// rename or a Steam-id correction would otherwise save a real change while a UI bound to
    /// <see cref="FieldsChanged"/> showed nothing. A field appears in one list or the other, never
    /// both.</para>
    /// </summary>
    public required IReadOnlyList<string> OtherChanges { get; init; }

    /// <summary>
    /// What the caller should write to <see cref="GameEntry.UserSet"/> on save: everything already
    /// marked, plus the fields changed here, MINUS any the engine change merely auto-filled.
    ///
    /// <para>THIS LIST CAN BE SHORTER THAN <see cref="FieldsChanged"/>. Do not assume the two nest.
    /// On an engine change, a changed field whose proposed value equals the NEW preset's own default
    /// is the preset speaking, not the user, and is deliberately not pinned — otherwise picking an
    /// engine from a dropdown would silently opt the game out of every future manifest correction.
    /// So a UI must bind its "what changed" list to <see cref="FieldsChanged"/> and its
    /// "what gets locked in" list to this one; they are different questions.</para>
    ///
    /// <para>Marks are never dropped by an unrelated edit: an existing mark survives regardless of
    /// which field this edit touched.</para>
    /// </summary>
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
/// <para>READS the filesystem — it checks that a proposed game folder exists, and delegates to
/// <see cref="DataDirMove.Plan"/> to size the move — and WRITES nothing. Planning must never change
/// an install: someone who reads the consequences and then clicks Cancel has to end up exactly where
/// they started. <c>Planning_writes_nothing</c> holds that line.</para>
///
/// <para>THE CALLER'S CONTRACT: <c>proposed</c> must carry only values the user actually stated. A
/// field this reports as changed normally lands in <see cref="RegistrationChangePlan.FieldsToPin"/>
/// (the preset-default drop below is the one exception, so the two lists can differ), becomes
/// <c>userSet</c> on save, and from then on permanently outranks manifest corrections for that game
/// (see <c>Scanner.GameContext</c>) — so a false pin silently opts the game out of every future fix,
/// which is the exact failure this feature exists to prevent. An entry rebuilt through
/// <c>EnginePresets.BuildGameEntry</c> (which fills <c>FileExtensions</c> and <c>GroupingRule</c> from
/// the preset whenever the input's are null) or auto-filled from <c>preset.ModPath</c> the way
/// <c>AddGameDialog.OnEngineChanged</c> rewrites its mod-path box will pin every auto-filled field.
/// Pass what the user typed, not what a preset filled in for them. On an engine change this class
/// defends itself as well — see the preset-default drop in <see cref="Plan"/> — but the contract is
/// what keeps the other three-quarters of the surface honest.</para>
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
        if (!SameGrouping(stored.GroupingRule, proposed.GroupingRule))
            changed.Add(GameEntry.UserSetGroupingRule);

        if (!SameLocations(stored.ModLocations, proposed.ModLocations))
            changed.Add(GameEntry.UserSetModLocations);

        var rootChanged = !string.Equals(
            DataDirMove.Norm(stored.GameRoot), DataDirMove.Norm(proposed.GameRoot), StringComparison.OrdinalIgnoreCase);
        if (rootChanged) changed.Add(GameEntry.UserSetGameRoot);

        // A blank root makes Scanner.DataDirForGame fall back to ".", which yields a RELATIVE
        // _626mods\<id> that DataDirMove.Norm then resolves against the process working directory —
        // so an unvalidated blank would produce a plan to move the user's ONLY copy of their disabled
        // mods into the launcher's install folder, with CanSave true and no blocker. A pasted or
        // half-typed folder is the likeliest error a repair surface will ever see.
        if (string.IsNullOrWhiteSpace(proposed.GameRoot))
            blockers.Add("A game folder is required — the launcher keeps this game's disabled mods "
                         + "and installed tools next to it.");
        else if (rootChanged && !Directory.Exists(proposed.GameRoot))
            blockers.Add($"There is no folder at {proposed.GameRoot}.");

        // Real changes that carry no pin and no move. Kept separate from `changed` so the two lists
        // stay disjoint: a UI renders both, and a field appearing twice would imply two consequences.
        var other = new List<string>();
        if (!string.Equals(stored.GameName, proposed.GameName, StringComparison.Ordinal))
            other.Add(GameEntry.FieldGameName);
        if (!string.Equals(stored.Engine ?? "", proposed.Engine ?? "", StringComparison.OrdinalIgnoreCase))
            other.Add(GameEntry.FieldEngine);
        if (!string.Equals(stored.SteamAppId ?? "", proposed.SteamAppId ?? "", StringComparison.Ordinal))
            other.Add(GameEntry.FieldSteamAppId);
        if (!string.Equals(stored.RequiredLauncher ?? "", proposed.RequiredLauncher ?? "", StringComparison.OrdinalIgnoreCase))
            other.Add(GameEntry.FieldRequiredLauncher);

        // Changing the engine changes which preset defaults apply, so a field that reads as
        // "untouched" under one engine may read as customised under another — quietly altering
        // whether future manifest corrections reach this game. Report it; the user decides.
        var engineChanged = !string.Equals(stored.Engine ?? "", proposed.Engine ?? "", StringComparison.OrdinalIgnoreCase);
        if (engineChanged)
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
        // silently re-expose a deliberate choice to being overwritten by a manifest correction. Every
        // changed field is a candidate on top of what is already marked; the engine-change filter below
        // is the only thing that may keep a candidate out.
        var pin = new List<string>(stored.UserSet ?? Array.Empty<string>());
        foreach (var f in changed)
            if (IsUserChoice(f) && !pin.Contains(f, StringComparer.OrdinalIgnoreCase)) pin.Add(f);

        return new RegistrationChangePlan
        {
            FieldsChanged = changed,
            OtherChanges = other,
            FieldsToPin = pin,
            DataDir = move,
            Blockers = blockers,
            Notes = notes,
        };

        // A field whose proposed value is exactly the NEW engine preset's default, on an edit that
        // changed the engine, is the PRESET speaking, not the user. Every path that produces such an
        // entry auto-fills it — EnginePresets.BuildGameEntry fills FileExtensions and GroupingRule
        // whenever the input's are null, and AddGameDialog.OnEngineChanged rewrites the mod-path box
        // outright — so pinning it would permanently opt the game out of manifest corrections because
        // someone touched a dropdown. A value that differs from the new preset's default is still the
        // user's and is still pinned; over-pinning and under-pinning are equally damaging here.
        bool IsUserChoice(string field)
        {
            if (!engineChanged
                || proposed.Engine is null
                || !EnginePresets.Presets.TryGetValue(proposed.Engine, out var preset))
                return true;

            return field switch
            {
                GameEntry.UserSetFileExtensions => !SameExtensions(proposed.FileExtensions, preset.FileExtensions),
                GameEntry.UserSetGroupingRule => !SameGrouping(proposed.GroupingRule, preset.GroupingRule),
                GameEntry.UserSetModLocations => !SameLocations(
                    proposed.ModLocations, new[] { new ModLocation("mods", "mods", preset.ModPath) }),
                _ => true,   // gameRoot has no preset default to be mistaken for
            };
        }
    }

    // One spelling for a grouping-rule comparison, shared by the change test and the preset-default
    // test so the two can never drift apart the way the extension sets once did.
    private static bool SameGrouping(string a, string b)
        => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // Trimmed, because " pak" and "pak" are the same extension and reading them as an edit would pin
    // the field for good. RegistrationRefresh.ExtensionSet is the ONE spelling of that normalisation:
    // when this file trimmed and RegistrationRefresh.IsUntouched did not, a cosmetic round-trip through
    // a text field ended self-healing for the game while this planner reported nothing had changed.
    private static bool SameExtensions(IReadOnlyList<string> a, IReadOnlyList<string> b)
        => RegistrationRefresh.ExtensionSet(a).SetEquals(RegistrationRefresh.ExtensionSet(b));

    /// <summary>
    /// One spelling for a relative mod path, so two ways of writing the same folder compare equal.
    ///
    /// <para>This repo genuinely produces both: <see cref="EnginePresets"/> and the shipped manifest
    /// use forward slashes ("Content/Paks/~mods"), while <c>ModLocations.UePakModLocation</c> builds
    /// the same location with <c>Path.Combine</c>, which yields backslashes on Windows.</para>
    ///
    /// <para><c>DataDirMove.Norm</c> cannot be reused here: it calls <c>GetFullPath</c>, which would
    /// resolve a RELATIVE mod path against the process working directory.</para>
    /// </summary>
    private static string NormRelative(string p)
        => p.Replace('/', System.IO.Path.DirectorySeparatorChar)
            .Replace('\\', System.IO.Path.DirectorySeparatorChar)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

    // ModLocation is a positional record — ModLocation(string Name, string Label, string Path) — so
    // Name and Path are non-nullable. No ?? guards: TreatWarningsAsErrors is on and dead null-coalesce
    // on a non-nullable operand is exactly the kind of thing that breaks a build at the worst moment.
    // Label is deliberately not compared: it is display text, not part of where the mods are.
    //
    // The Path comparison is normalised for the reason gameRoot's is (see NormRelative). A cosmetic
    // difference that reads as "changed" lands modLocations in FieldsToPin, and a pinned field
    // permanently outranks manifest corrections for that game (Scanner.GameContext) — silently opting
    // it out of the very fix this spec exists to deliver. Over-pinning is as damaging as under-pinning.
    private static bool SameLocations(IReadOnlyList<ModLocation> a, IReadOnlyList<ModLocation> b)
        => a.Count == b.Count
           && a.Zip(b).All(p =>
               string.Equals(p.First.Name, p.Second.Name, StringComparison.OrdinalIgnoreCase)
               && string.Equals(NormRelative(p.First.Path), NormRelative(p.Second.Path),
                   StringComparison.OrdinalIgnoreCase));
}
