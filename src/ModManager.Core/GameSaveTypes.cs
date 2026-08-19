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
    private const string PalworldAppId = "1623730";

    public static GameSaveTypes Resolve(string? engine, string? steamAppId)
        => new(engine ?? "", SaveTypesFor(engine), LayoutFor(steamAppId));

    private static SaveLayout LayoutFor(string? steamAppId)
        => steamAppId == PalworldAppId ? SaveLayout.Worlds : SaveLayout.TypedFiles;

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
