using ModManager.Core.LooseMods;

namespace ModManager.Core.Discovery;

/// <summary>
/// What a registration CLAIMS about a game, versus where its mods actually are.
///
/// <para>The launcher has always known both halves and never put them side by side. An agent (or a
/// user reading a dialog) could ask "what mods are installed?" and "what does the registration say?"
/// but had no way to ask the only question that matters when something looks wrong: <i>do those two
/// agree?</i></para>
///
/// <para>Measured, and the reason this type exists: an Elden Ring registration declares a Mod Engine
/// 2 <c>mod</c> folder that does not exist on disk, while all eleven of its mods resolve through
/// direct-inject under <c>Game\</c>. Both facts were already reachable — <c>get_mod_context</c>
/// reported the declared path, <c>list_mods</c> reported the mods — and put together they read as a
/// broken install. Nothing was broken. The mods load fine; the registration simply describes a
/// mechanism this install does not use. Reconstructing that by hand took a dozen filesystem probes
/// and still landed on the wrong conclusion, because <c>mod</c> and <c>mods</c> are different
/// folders and a truncated directory listing hid the second one.</para>
///
/// <para>WHAT "ORGANIZED" IS NOT. A direct-inject mod is DEFINED by living loose at the play folder
/// root — <c>dinput8.dll</c> is a loader, <c>reshade-shaders</c> is read from the root by name.
/// Flagging those as untidy would push a user to "fix" an install by breaking it. So this type never
/// judges tidiness. It reports <see cref="Alignment"/>: whether the mods were found where the
/// registration said to look. Drift is a description, not a defect — a drifted game can be perfectly
/// healthy, and usually is.</para>
///
/// <para>Read-only, like every discovery surface: computes from the same <see cref="ModListing"/>
/// the app and the MCP already use, writes nothing, and never repairs what it finds.</para>
/// </summary>
public sealed record GameShape
{
    public required string GameId { get; init; }

    /// <summary>Is the launcher finding anything at all for this game.</summary>
    public required bool Managed { get; init; }

    /// <summary>How many mod KEYS the launcher tracks. Windrose reports 30 here and shows 27 rows,
    /// because four Faster Ships files are one variant family. Neither number is wrong; reporting
    /// only one of them is (A12) - an agent said "you have 30 mods", the user counted 27 on screen,
    /// and concluded the agent was broken.</summary>
    public required int ModCount { get; init; }

    /// <summary>How many ROWS a person sees, after variant families collapse. Equal to
    /// <see cref="ModCount"/> when the game has no families.</summary>
    public required int RowCount { get; init; }

    /// <summary>The families that make the two differ, largest first. Empty when they agree, so a
    /// caller can say WHY rather than reporting a bare discrepancy.</summary>
    public required IReadOnlyList<VariantFamilySummary> VariantFamilies { get; init; }

    /// <summary>How the mods were actually listed. The single most clarifying field: it names the
    /// lane, so a declared-path mismatch reads as "different mechanism" rather than "missing files."</summary>
    public required ListingMechanism Mechanism { get; init; }

    /// <summary>The registration's game root, as resolved.</summary>
    public required string GameRoot { get; init; }

    /// <summary>Where loose files actually resolve. For a game with a <c>Game\</c> subfolder (Elden
    /// Ring) this is NOT the game root, which is exactly the trap: mod paths reported relative to it
    /// look wrong when measured from the root. Null when the mechanism does not use one.</summary>
    public string? PlayFolder { get; init; }

    public required IReadOnlyList<DeclaredLocation> DeclaredLocations { get; init; }

    /// <summary>Where the mods really live, as directories relative to <see cref="PlayFolder"/> (or
    /// the game root), with a count each. The empty string means "loose at that root."</summary>
    public required IReadOnlyList<ContentRoot> ContentRoots { get; init; }

    /// <summary>Mods that are themselves loaders (Elden Mod Loader, script extenders). Worth calling
    /// out separately: a loader explains why sibling files load from a folder the registration never
    /// mentions, which otherwise looks like drift with no cause.</summary>
    public required IReadOnlyList<string> Loaders { get; init; }

    public required LocationAlignment Alignment { get; init; }

    /// <summary>Plain-language findings, safe to show a user or hand an agent verbatim.</summary>
    public required IReadOnlyList<string> Notes { get; init; }

    /// <summary>
    /// Whether this registration's drift is provably costing the user something.
    ///
    /// <para>Nothing found, AND at least one declared location is not on disk. That pair is the shape
    /// that actually hurt: a Cyberpunk registration looking for <c>pak</c> in a folder of
    /// <c>.archive</c> files reported 194 mods as zero.</para>
    ///
    /// <para>Deliberately NOT "is it drifted". Drift is common and usually harmless — Elden Ring is
    /// drifted and perfectly healthy, as is any loader-based install. A banner on every drift would
    /// flag working games and train the user to dismiss the one case worth reading.</para>
    ///
    /// <para>DECLARED entries only. A derived location (the launcher's own <c>ue4ss\Mods</c>) is not
    /// something the registration says, so its absence is not a registration defect — and the banner's
    /// only action is a dialog that edits the registration, which could not fix it.</para>
    /// </summary>
    public bool NeedsAttention => Attention(ModCount, DeclaredLocations);

    /// <summary>
    /// The same predicate as <see cref="NeedsAttention"/>, for a caller that already has the context
    /// and the mod count.
    ///
    /// <para>Exists so the banner does not pay for a whole second listing. <c>ReloadModsAsync</c> has
    /// already resolved the mod list; calling <see cref="Of"/> there rebuilt the context twice more,
    /// re-resolved every mod, and computed content roots and alignment — a doubled scan on every
    /// toggle, game switch and drop — all to read a two-term boolean. The predicate lives in ONE
    /// place so the banner and the dialog can never disagree; <c>Banner_and_dialog_agree</c> holds
    /// that line.</para>
    /// </summary>
    public static bool NeedsAttentionFor(GameContext ctx, int modCount)
        => Attention(modCount, DeclaredFor(ctx));

    private static bool Attention(int modCount, IReadOnlyList<DeclaredLocation> declared)
        => modCount == 0 && declared.Any(d => d.Declared && !d.Exists);

    /// <summary>
    /// The locations the scanner will actually look in, each tagged with whether the REGISTRATION
    /// said so.
    ///
    /// <para><c>Scanner.GameContext</c> appends a synthetic <c>ue4ss-mods</c> location when the
    /// launcher owns a UE4SS install. It has no entry in <c>game.ModLocations</c>, so attributing it
    /// to the registration puts a label ("ue4ss-mods") in a path slot under a heading that says the
    /// game is "set to look in" it. It is not: the launcher added it. Derived entries carry their
    /// absolute path instead, and every reader renders them as what they are.</para>
    ///
    /// <para>MATCHED ON PATH, NOT NAME. <c>Scanner.GameContext</c> substitutes "loc0" for a stored
    /// location whose <c>Name</c> is empty — a fallback that exists because hand-edited registries do
    /// exactly that, and hand-edited registries are this feature's whole audience. Matching by name
    /// missed every one of them, so the location read as launcher-derived and the banner went silent
    /// on the one shape it exists for: nothing found, and the folder the registration names is not on
    /// disk. Both sides resolve through <c>Scanner.LocationAbs</c> so they cannot disagree.</para>
    /// </summary>
    private static List<DeclaredLocation> DeclaredFor(GameContext ctx)
        => ctx.Locations.Select(l =>
        {
            var stored = ctx.Game.ModLocations.FirstOrDefault(
                m => PathEquals(Scanner.LocationAbs(ctx.GameRoot, m.Path), l.Abs));
            return new DeclaredLocation
            {
                Name = l.Name,
                Path = stored?.Path ?? l.Abs,
                Absolute = l.Abs,
                Exists = !string.IsNullOrEmpty(l.Abs) && Directory.Exists(l.Abs),
                Declared = stored is not null,
            };
        }).ToList();

    public static GameShape Of(GameEntry game)
    {
        var ctx = Scanner.GameContext(game);
        var mechanism = ModListing.MechanismFor(game, ctx);
        var mods = ModListing.Resolve(game);

        // Loose lanes resolve against the play folder; the scanner resolves against its declared
        // locations. Reporting content paths against the wrong root is the whole failure this fixes.
        var playFolder = mechanism is ListingMechanism.DirectInject or ListingMechanism.LooseRoot
            ? DirectInjectListing.PlayFolder(game.GameRoot)
            : null;
        var contentBase = playFolder ?? ctx.GameRoot;

        var declared = DeclaredFor(ctx);

        // A mod's files are relative to the lane that found it: the play folder for the loose lanes,
        // but the mod's own declared LOCATION for the scanner. Resolving every file against one base
        // is precisely the mistake this type exists to stop — it puts a correctly-placed pak at the
        // game root and reports a tidy install as drifted.
        // A content root is a claim about DISK, so it is verified against disk. Building it from the
        // rows alone counted files that are not there: a disabled mod's files live in the holding
        // folder while its row still names the location it would occupy, so six disabled mods
        // manufactured a root at a path that does not exist - and Alignment then read Aligned off it
        // (A21). Counting only what is present is what makes this report falsifiable.
        var roots = mods
            .SelectMany(m =>
            {
                var b = BaseFor(m, playFolder, declared, ctx.GameRoot);
                var files = m.Files.Count > 0 ? m.Files : new List<string> { m.Name };
                return files.Select(f => new { Dir = AbsoluteDirOf(b, f), Abs = Norm(Path.Combine(b, (f ?? "").Replace('/', Path.DirectorySeparatorChar))) });
            })
            .Where(x => Exists(x.Abs))
            .GroupBy(x => x.Dir, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ContentRoot
            {
                RelativePath = RelativeOrEmpty(contentBase, g.Key),
                Absolute = g.Key,
                FileCount = g.Count(),
                InsideDeclared = declared.Any(d => IsInside(g.Key, d.Absolute)),
            })
            .OrderByDescending(r => r.FileCount)
            .ToList();

        // What a person counts on screen, and why it differs. Variant families collapse to one row -
        // four Faster Ships files are one mod with four options - so the key count and the row count
        // are both true and mean different things. Reporting both, with the families named, is what
        // stops an agent and a user contradicting each other over the same install (A12).
        var families = VariantGroups.Group(mods)
            .Where(f => f.Members.Count > 1)
            .Select(f => new VariantFamilySummary
            {
                Title = f.Members[0].DisplayName is { Length: > 0 } t ? t : f.Members[0].Name,
                Keys = f.Members.Select(m => m.Name).ToList(),
            })
            .OrderByDescending(f => f.Keys.Count)
            .ToList();
        var rowCount = mods.Count - families.Sum(f => f.Keys.Count - 1);

        var loaders = mods.Where(m => m.IsLoader).Select(m => m.DisplayName is { Length: > 0 } d ? d : m.Name).ToList();

        // Alignment falls out of what actually survived the disk check. A game with rows but no
        // roots has its mods stepped aside rather than missing, and saying so is a different answer
        // from "no mods" - which is the distinction A21 was reporting wrongly.
        var alignment =
            mods.Count == 0                     ? LocationAlignment.NoMods
            : roots.Count == 0                  ? LocationAlignment.AllDisabled
            : roots.All(r => r.InsideDeclared)  ? LocationAlignment.Aligned
            : roots.Any(r => r.InsideDeclared)  ? LocationAlignment.Partial
            :                                     LocationAlignment.Drifted;

        return new GameShape
        {
            GameId = game.Id,
            Managed = mods.Count > 0,
            ModCount = mods.Count,
            RowCount = rowCount,
            VariantFamilies = families,
            Mechanism = mechanism,
            GameRoot = ctx.GameRoot,
            PlayFolder = playFolder,
            DeclaredLocations = declared,
            ContentRoots = roots,
            Loaders = loaders,
            Alignment = alignment,
            Notes = BuildNotes(mechanism, declared, roots, loaders, alignment, playFolder, ctx.GameRoot, mods.Count),
        };
    }

    // Plain sentences, not codes. These get read by a person as often as by an agent, and the whole
    // point is to end the guessing — so each note says what is true AND whether it is a problem.
    private static List<string> BuildNotes(
        ListingMechanism mechanism, List<DeclaredLocation> declared, List<ContentRoot> roots,
        List<string> loaders, LocationAlignment alignment, string? playFolder, string gameRoot,
        int modCount)
    {
        var notes = new List<string>();

        // Two sentences, because they mean different things to whoever reads them. A declared folder
        // that is missing is a registration the user can correct; a derived one is the launcher's own
        // folder, and telling them their registration declares it would send them editing a field
        // that does not exist.
        foreach (var d in declared.Where(d => !d.Exists))
            notes.Add(d.Declared
                ? $"Declared mod location '{d.Path}' does not exist on disk ({d.Absolute})."
                : $"The launcher's own '{d.Name}' folder is not on disk ({d.Absolute}) — the "
                  + "registration does not declare it and does not need to.");

        if (playFolder is not null && !PathEquals(playFolder, gameRoot))
            notes.Add($"Mods resolve under the play folder '{playFolder}', not the game root — paths "
                      + "reported relative to the root will look wrong.");

        if (loaders.Count > 0)
            notes.Add($"Loader present: {string.Join(", ", loaders)} — sibling mods load from its folder, "
                      + "which the registration does not need to declare.");

        notes.Add(alignment switch
        {
            LocationAlignment.NoMods  => "No mods detected for this game.",
            // Without this case the switch fell through to the drift arm and reported "all 0 file(s)
            // were found by the scanner lane" - a sentence about drift, for a game with no drift.
            LocationAlignment.AllDisabled =>
                $"{modCount} mod(s) are registered and none is currently placed on disk — every one is "
                + "switched off and held aside. Not the same as having no mods, and nothing here is "
                + "missing: enabling any of them puts its files back.",
            LocationAlignment.Aligned => "Mods are where the registration says they are.",
            LocationAlignment.Partial => "Some mods are outside every declared location.",
            _ => $"No mod is inside a declared location — all {roots.Sum(r => r.FileCount)} file(s) were "
                 + $"found by the {Describe(mechanism)} lane instead. This is drift, not damage: the mods "
                 + "load normally; the registration simply describes a mechanism this install does not use.",
        });

        return notes;
    }

    private static string Describe(ListingMechanism m) => m switch
    {
        ListingMechanism.ModEngine2   => "Mod Engine 2 config",
        ListingMechanism.DirectInject => "direct-inject",
        ListingMechanism.LooseRoot    => "loose-root",
        _                             => "scanner",
    };

    /// <summary>Which root a mod's file paths are relative to — the lane that found it decides.</summary>
    private static string BaseFor(Mod m, string? playFolder, List<DeclaredLocation> declared, string gameRoot)
    {
        if (playFolder is not null) return playFolder;
        var loc = declared.FirstOrDefault(d => string.Equals(d.Name, m.Location, StringComparison.OrdinalIgnoreCase));
        return loc is not null && loc.Absolute.Length > 0 ? loc.Absolute : gameRoot;
    }

    private static string AbsoluteDirOf(string baseDir, string relPath)
    {
        var rel = (relPath ?? "").Replace('/', Path.DirectorySeparatorChar);
        var full = Norm(Path.Combine(baseDir, rel));
        var dir = Path.GetDirectoryName(full);
        return string.IsNullOrEmpty(dir) ? Norm(baseDir) : dir;
    }

    /// <summary>The content dir as the caller would say it — relative to the base they think in,
    /// empty for "loose at that root", and absolute if it somehow escapes the base entirely.</summary>
    private static string RelativeOrEmpty(string baseDir, string absDir)
    {
        if (PathEquals(baseDir, absDir)) return "";
        try
        {
            var rel = Path.GetRelativePath(Norm(baseDir), absDir);
            return rel == "." ? "" : rel.StartsWith("..", StringComparison.Ordinal) ? absDir : rel;
        }
        catch { return absDir; }
    }

    private static bool PathEquals(string? a, string? b)
        => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Containment with a separator boundary, so <c>...\mods</c> is never read as being
    /// inside <c>...\mod</c> — the exact pair that makes this game hard to reason about by hand.</summary>
    private static bool IsInside(string? child, string? parent)
    {
        var c = Norm(child);
        var p = Norm(parent);
        if (c.Length == 0 || p.Length == 0) return false;
        if (string.Equals(c, p, StringComparison.OrdinalIgnoreCase)) return true;
        return c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Is this path actually there? File OR directory, because a folder-shaped mod's single
    /// "file" IS a directory (UE4SS Lua mods, REFramework script folders) and checking only for a
    /// file would erase every one of them from the shape report. Any IO failure counts as absent:
    /// a path we cannot read is not a path we can claim content for.</summary>
    private static bool Exists(string? abs)
    {
        if (string.IsNullOrEmpty(abs)) return false;
        try { return File.Exists(abs) || Directory.Exists(abs); }
        catch { return false; }
    }

    private static string Norm(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
    }
}

/// <summary>A mod location the scanner will look in, and whether it is actually there.</summary>
public sealed record DeclaredLocation
{
    public required string Name { get; init; }

    /// <summary>The relative path as stored in the registration (e.g. <c>mod</c>) when
    /// <see cref="Declared"/>; the absolute path otherwise, since a derived location has no stored
    /// path and rendering its NAME in a path slot presents a label as a folder.</summary>
    public required string Path { get; init; }

    public required string Absolute { get; init; }
    public required bool Exists { get; init; }

    /// <summary>True when the registration actually names this location. False for one the launcher
    /// derived — today the <c>ue4ss-mods</c> folder <c>Scanner.GameContext</c> appends when the
    /// launcher owns a UE4SS install. A reader must not attribute a derived entry to the user's
    /// registration: there is no field for it, and no edit that could change it.</summary>
    public bool Declared { get; init; } = true;
}

/// <summary>A directory mods were actually found in.</summary>
public sealed record ContentRoot
{
    /// <summary>Relative to the play folder (or game root). Empty string means loose at that root.</summary>
    public required string RelativePath { get; init; }
    public required string Absolute { get; init; }
    public required int FileCount { get; init; }
    public required bool InsideDeclared { get; init; }
}

/// <summary>Whether mods were found where the registration said to look. Drift is a description, not
/// a defect — see <see cref="GameShape"/>.</summary>
/// <summary>One variant family: several mod keys a person sees as a single row with options.</summary>
public sealed class VariantFamilySummary
{
    /// <summary>What the row is called on screen.</summary>
    public required string Title { get; init; }
    /// <summary>The mod keys that collapse into it.</summary>
    public required IReadOnlyList<string> Keys { get; init; }
}

public enum LocationAlignment
{
    NoMods,
    /// <summary>Mods ARE registered and none of them is currently placed on disk — every one is
    /// stepped aside in the holding folder. Distinct from <see cref="NoMods"/> on purpose: "you have
    /// no mods" and "your mods are all switched off" are different answers to "is this install
    /// healthy", and reporting the first for the second is how a shape report claimed Aligned for a
    /// folder that does not exist (A21).</summary>
    AllDisabled,
    /// <summary>Every content root sits inside a declared location.</summary>
    Aligned,
    /// <summary>Some content is inside a declared location, some is not.</summary>
    Partial,
    /// <summary>Nothing is inside a declared location — a different mechanism is doing the work.</summary>
    Drifted,
}
