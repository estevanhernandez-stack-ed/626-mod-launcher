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
