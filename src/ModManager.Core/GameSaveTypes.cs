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
public sealed record GameSaveTypes(string Engine, IReadOnlyList<SaveType> SaveTypes);

/// <summary>
/// Resolves a <see cref="GameSaveTypes"/> for a game — engine-level defaults, with a per-App-ID
/// override hook for future game-specifics. Repeatable: adding a game/engine's save types is a
/// one-line catalog entry. Unknown games resolve to no declared save types — the save manager's
/// whole-folder backup/restore still works (baseline floor); only the gated extras (clone,
/// per-type restore) light up when a profile declares types.
/// </summary>
public static class GameSaveTypesCatalog
{
    public static GameSaveTypes Resolve(string? engine, string? steamAppId)
        => new(engine ?? "", SaveTypesFor(engine));

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
