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

    /// <summary>
    /// What to write to the proposed entry's <see cref="GameEntry.DataDir"/> when the user declines
    /// the move — or null when there is no move to decline.
    ///
    /// <para>It is the STORED entry's data dir, not the proposed one's, because the point of a pin is
    /// to name where the data already is. Getting that one word wrong writes a <c>dataDir</c> pointing
    /// at a folder with nothing in it: the registration then looks for this game's disabled mods,
    /// profiles and tools in an empty place, and the real copy sits unreferenced beside the old game
    /// folder. That is why the rule lives here behind a test rather than at the call site.</para>
    /// </summary>
    public string? PinDataDirTo { get; init; }

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
    /// <param name="walks">Optional, for previews only: lets repeated plans in one editing session reuse
    /// the data-dir walk. The plan a save acts on must be taken without it — see
    /// <see cref="DataDirWalkCache"/>.</param>
    public static RegistrationChangePlan Plan(GameEntry stored, GameEntry proposed, DataDirWalkCache? walks = null)
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

        // A BLANK MOD FOLDER IS NEVER A CHOICE. An empty relative path resolves to the game root
        // (Scanner.LocationAbs is a Path.Combine), so clearing the box would quietly make the whole
        // install the mod folder — every file with a matching extension listed and togglable as a mod —
        // and pin that, opting the game out of mod-path corrections. Mods that genuinely live in the
        // game folder are spelled "." (the loose-root presets do exactly that), so blank is only ever
        // a cleared box. Only a NEWLY blank path blocks: a registration already carrying one keeps
        // working for an unrelated edit, the way it did before.
        for (var i = 0; i < proposed.ModLocations.Count; i++)
            if (string.IsNullOrWhiteSpace(proposed.ModLocations[i].Path)
                && !(i < stored.ModLocations.Count && string.IsNullOrWhiteSpace(stored.ModLocations[i].Path)))
            {
                blockers.Add("A mod folder can't be blank. Type the folder your mods go in, relative to "
                             + "the game folder — or \".\" if they sit in the game folder itself.");
                break;
            }

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

        // A game folder is not the only place the old folder is written down. ModEngineConfig is an
        // ABSOLUTE path and ModEngine2Listing.Applies gates on File.Exists of it, so correcting a
        // FromSoft game's folder can silently drop it out of the Mod Engine 2 lane; a LaunchTarget of
        // kind "exe" whose Target is already rooted is fired as-is, so Launch would start the game in
        // the old location. Neither is rewritten here ON PURPOSE — the obvious repair, re-running
        // detection, overwrites ModLocations and would clobber the mod folder the user just typed,
        // re-opening the false-pin hazard this whole surface exists to close. So: say it, and let the
        // user reach for Re-detect deliberately.
        if (changed.Contains(GameEntry.UserSetGameRoot))
            notes.Add("Changing the game folder does not re-check how this game launches. Use "
                      + "Re-detect afterwards if the Launch button stops working.");

        DataDirMovePlan? move = null;
        if (rootChanged && blockers.Count == 0)
        {
            move = DataDirMove.Plan(Scanner.DataDirForGame(stored), Scanner.DataDirForGame(proposed), walks);
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
            PinDataDirTo = move is null ? null : Scanner.DataDirForGame(stored),
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
                // PATH ONLY, never the name. SameLocations compares Name as well, and a registration's
                // first location is named whatever the manifest or ModLocator called it — "~mods",
                // "paks", "loc0". Comparing the name here meant any game not using the literal name
                // "mods" could never be recognised as sitting on the preset default, so an engine
                // change always pinned modLocations and permanently opted that game out of future
                // mod-path corrections — while the panel truthfully promised exactly that.
                GameEntry.UserSetModLocations => !IsPresetDefaultPath(proposed.ModLocations, preset.ModPath),
                _ => true,   // gameRoot has no preset default to be mistaken for
            };
        }
    }

    /// <summary>
    /// The location list an edit to ONE mod location proposes: that location with its path replaced,
    /// every other location carried through exactly as stored.
    ///
    /// <para>WHY THIS IS NOT LEFT TO THE CALLER. <see cref="Plan"/> compares location lists by count
    /// first, and it is right to — a list that really grew or shrank is a real edit. So a caller that
    /// RESHAPES the list while meaning to edit one path is read as having stated a new list, and that
    /// lands <c>modLocations</c> in <see cref="RegistrationChangePlan.FieldsToPin"/>, permanently opting
    /// the game out of every future manifest correction to its mod folders. The setup dialog did it
    /// twice before this existed: rebuilding a one-element list for a three-location game (3 → 1), and
    /// inventing an empty location for a game with none (0 → 1), each time from a rename.</para>
    ///
    /// <para>So the shape-preserving rule lives here, behind tests that assert on the reshape:</para>
    /// <list type="bullet">
    /// <item>No stored location and a blank path proposes NO location — not an invented one at the game
    /// root.</item>
    /// <item>No stored location and a typed path proposes one, legitimately: the user stated it.</item>
    /// <item>Otherwise the list keeps its length; only the location at <paramref name="index"/> changes,
    /// and only its path — its name keys the per-location disable metadata.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<ModLocation> EditLocation(
        IReadOnlyList<ModLocation> stored, int index, string typedPath)
    {
        var path = typedPath.Trim();

        if (stored.Count == 0)
        {
            if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
            return path.Length == 0
                ? Array.Empty<ModLocation>()
                : new[] { new ModLocation("mods", "mods", path) };
        }

        if (index < 0 || index >= stored.Count) throw new ArgumentOutOfRangeException(nameof(index));

        var edited = stored.ToArray();
        edited[index] = stored[index] with { Path = path };
        return edited;
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
    /// <summary>Whether a proposed location list is exactly the engine preset's own default folder —
    /// one location, at the preset's path. Compared on PATH ALONE: the name is whatever the manifest
    /// or <c>ModLocator</c> assigned, and has nothing to do with whether the user chose this folder.</summary>
    private static bool IsPresetDefaultPath(IReadOnlyList<ModLocation> proposed, string presetModPath)
        => proposed.Count == 1
           && string.Equals(NormRelative(proposed[0].Path), NormRelative(presetModPath),
               StringComparison.OrdinalIgnoreCase);

    private static bool SameLocations(IReadOnlyList<ModLocation> a, IReadOnlyList<ModLocation> b)
        => a.Count == b.Count
           && a.Zip(b).All(p =>
               string.Equals(p.First.Name, p.Second.Name, StringComparison.OrdinalIgnoreCase)
               && string.Equals(NormRelative(p.First.Path), NormRelative(p.Second.Path),
                   StringComparison.OrdinalIgnoreCase));
}
