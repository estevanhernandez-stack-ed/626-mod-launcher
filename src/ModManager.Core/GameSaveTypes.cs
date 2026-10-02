namespace ModManager.Core;

/// <summary>One save kind a game uses: file extension + plain-English label.</summary>
public sealed record SaveType(string Extension, string Label);

/// <summary>
/// Declarable per-game knowledge the app consults to decide which features apply to a game. This
/// round only <see cref="SaveTypes"/> is populated/used; launch options, anti-cheat, and mod layout
/// converge onto this profile later (their catalogs stay where they are for now).
/// </summary>
/// <summary>
/// What kinds of save a game's engine declares. Was <c>GameProfile</c> until wave 10 — a name that
/// collided head-on with the app's other, much more visible profile: a saved set of enabled mods,
/// which is what a profile means here and in every other mod manager. Two things sharing one word is
/// the exact failure item 7 is about, and this one was invisible from the UI, which is worse.
/// </summary>
/// <summary>How a game arranges its saves on disk. Measured on real installs, not assumed - see
/// docs/2026-08-19-saves-are-three-shapes.md.</summary>
public enum SaveLayout
{
    /// <summary>Several formats of the same save, side by side in one folder: Elden Ring's
    /// .sl2 / .co2 / .err. The only shape where "clone to another type" means anything.</summary>
    TypedFiles,

    /// <summary>One folder per world, each holding that world's files. Palworld: two worlds here, 74
    /// .sav files, 72 of them nested a level below the folder the panel reads. Listing files would
    /// find one top-level .sav and imply it was your save; the unit a player thinks in is the world.</summary>
    Worlds,
}

public sealed record GameSaveTypes(string Engine, IReadOnlyList<SaveType> SaveTypes,
    SaveLayout Layout = SaveLayout.TypedFiles);

/// <summary>
/// One kind of save a game names rather than types: every file whose name starts with
/// <see cref="Prefix"/>. EA's football saves carry no extension at all (<c>RTG-E</c>,
/// <c>ROSTER-Official</c>, <c>PROFILE-COLLEGE</c>), so an extension-keyed <see cref="SaveType"/> can
/// never see them.
///
/// <para><b>For listing only.</b> A <see cref="SaveType"/> also switches on clone, per-type restore and
/// the FromSoft character reader, all of which assume one save in several formats. A named kind is a
/// label on a file, nothing more.</para>
/// </summary>
public sealed record SaveFileKind(string Prefix, string Label)
{
    public bool Matches(string fileName) => fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Which named save kinds a game declares. Per GAME, not per engine: every EA app import is stamped
/// <c>frostbite</c>, and a Battlefield whose folder holds none of these would be told its correct save
/// folder looks wrong (<see cref="SaveListingEmptyState"/>). Resolved by manifest id or EA content id,
/// so a second copy (<c>-2</c>) and a copy added by hand both find their game.
/// </summary>
public static class SaveFileKindsCatalog
{
    // As each game writes them, directly in Documents\<title>\saves, with no extension (VERIFIED on the
    // owner's machine 2026-10-02). Careers first, then the league, then the profile that indexes them.
    private static readonly SaveFileKind[] CollegeFootball27 =
    {
        new("RTG-", "Road to Glory career"),
        new("ROSTER-", "Roster"),
        new("PROFILE-", "Profile"),
    };

    private static readonly SaveFileKind[] Madden27 =
    {
        new("CAREER-", "Franchise career"),
        new("ROSTER-", "Roster"),
        new("PROFILE-", "Profile"),
    };

    private static readonly IReadOnlyDictionary<string, SaveFileKind[]> ById =
        new Dictionary<string, SaveFileKind[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ea-sports-college-football-27"] = CollegeFootball27,
            ["madden-nfl-27"] = Madden27,
        };

    private static readonly IReadOnlyDictionary<string, SaveFileKind[]> ByEaContentId =
        new Dictionary<string, SaveFileKind[]>(StringComparer.Ordinal)
        {
            ["16425899"] = CollegeFootball27,   // same ids as BanRiskCatalog's floors
            ["16425895"] = Madden27,
        };

    public static IReadOnlyList<SaveFileKind> For(GameEntry game)
    {
        var entry = ManifestIdLookup.ConfirmedEntryFor(game);
        // The confirmed entry first. The raw named id only when the game carries no store id at all:
        // with one, the confirmed join has already said whether that id is to be believed.
        var noStoreId = string.IsNullOrEmpty(game.SteamAppId) && string.IsNullOrEmpty(game.EaContentId);
        foreach (var id in new[] { entry?.Id, noStoreId ? ManifestIdLookup.NamedId(game) : null })
            if (!string.IsNullOrEmpty(id) && ById.TryGetValue(id, out var byId)) return byId;
        foreach (var ea in new[] { game.EaContentId, entry?.Stores.EaContentId })
            if (!string.IsNullOrEmpty(ea) && ByEaContentId.TryGetValue(ea, out var byEa)) return byEa;
        return Array.Empty<SaveFileKind>();
    }
}

/// <summary>
/// Resolves a <see cref="GameSaveTypes"/> for a game — engine-level defaults, with a per-App-ID
/// override hook for future game-specifics. Repeatable: adding a game/engine's save types is a
/// one-line catalog entry. Unknown games resolve to no declared save types — the save manager's
/// whole-folder backup/restore still works (baseline floor); only the gated extras (clone,
/// per-type restore) light up when a profile declares types.
/// </summary>
public static class GameSaveTypesCatalog
{
    // Palworld. The per-App-ID hook this method has always carried, finally used: layout is a
    // per-GAME fact, not a per-engine one. Palworld and Windrose are both ue-pak and arrange saves
    // completely differently - worlds in folders versus a RocksDB database - so keying this on engine
    // would have been wrong for one of them whichever way it went.
    /// <summary>Layout comes from the signed game manifest, so a folder-per-save game is a data PR
    /// rather than an app release. See <see cref="SaveLayoutCatalog"/>. Resolved through every identity
    /// the game carries, so an EA app game (no Steam id) and a second store copy get their entry's.</summary>
    public static GameSaveTypes Resolve(GameEntry game)
        => new(game.Engine ?? "", SaveTypesFor(game.Engine), SaveLayoutCatalog.For(game));

    private static IReadOnlyList<SaveType> SaveTypesFor(string? engine) => engine switch
    {
        // FromSoftware (Elden Ring et al.): vanilla .sl2, Seamless Co-op .co2, Reforged .err.
        "fromsoft" => new[]
        {
            new SaveType(".sl2", "Vanilla"),
            new SaveType(".co2", "Seamless Co-op"),
            new SaveType(".err", "Reforged"),
        },
        _ => Array.Empty<SaveType>(),
    };
}

/// <summary>
/// What the saves panel says when it can list nothing.
///
/// <para><b>Two different situations, one sentence.</b> The panel said <i>"No save files of this
/// game's known types here. Check the save folder above"</i> in both, and for Palworld that is advice
/// pointing at the one thing that is not wrong: the folder is right, the saves are in it, and the app
/// simply does not know this game's layout. <c>GameSaveTypesCatalog</c> declares types for
/// <c>fromsoft</c> and nothing else, so every other game gets told to go re-check a correct setting.</para>
///
/// <para>Sending someone to verify a setting that is already correct is worse than saying nothing:
/// they change it, and then the snapshots that WERE covering everything start covering the wrong
/// folder.</para>
/// </summary>
public static class SaveListingEmptyState
{
    /// <param name="declaresTypes">Whether this game declares any save types at all.</param>
    /// <param name="folderSet">Whether a save folder has been resolved.</param>
    public static string MessageFor(bool folderSet, bool declaresTypes)
    {
        if (!folderSet)
            return "No save folder set for this game yet. Set one above and snapshots can start covering it.";

        // The app's own gap, not the user's. Say so, and say what still protects them - the snapshot
        // is a whole-folder zip and it recurses, which is exactly what this case needs them to trust.
        if (!declaresTypes)
            return "This game's save format isn't itemized yet, so there's nothing to list here. "
                 + "Snapshots still back up the whole folder, subfolders and all.";

        // Types ARE declared and none were found. Here the folder genuinely is the thing to look at.
        return "No save files of this game's known types in this folder. Check the folder above — "
             + "snapshots still cover everything in it.";
    }
}
