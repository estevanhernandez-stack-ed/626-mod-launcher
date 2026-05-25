namespace ModManager.Core;

/// <summary>
/// One curated quick-pick game. Picking it in the Add Game wizard pre-fills the engine, mod
/// folder, and Steam App ID. <see cref="Engine"/> is an <see cref="EnginePresets.Presets"/> key.
/// <see cref="FileExtensions"/> is an optional override for games whose engine preset's default
/// extensions don't match (e.g. Cyberpunk's "custom" engine ships .pak, but its mods are .archive).
/// </summary>
public sealed record PopularGame(
    string Id,
    string Name,
    string Engine,
    string ModPath,
    string SteamAppId)
{
    public IReadOnlyList<string>? FileExtensions { get; init; }
}

/// <summary>
/// Curated catalog of popular moddable games for the Add Game wizard's quick-pick. Ports
/// popular-games.js. The list order is intentional and asserted by tests.
/// </summary>
public static class PopularGames
{
    public static IReadOnlyList<PopularGame> All { get; } = new[]
    {
        new PopularGame("skyrim-se", "Skyrim Special Edition", "bethesda", "Data", "489830"),
        new PopularGame("fallout-4", "Fallout 4", "bethesda", "Data", "377160"),
        new PopularGame("starfield", "Starfield", "bethesda", "Data", "1716740"),
        new PopularGame("stardew-valley", "Stardew Valley", "smapi", "Mods", "413150"),
        new PopularGame("rimworld", "RimWorld", "smapi", "Mods", "294100"),
        new PopularGame("valheim", "Valheim", "bepinex", "BepInEx/plugins", "892970"),
        new PopularGame("lethal-company", "Lethal Company", "bepinex", "BepInEx/plugins", "1966720"),
        new PopularGame("palworld", "Palworld", "ue-pak", "Pal/Content/Paks/~mods", "1623730"),
        new PopularGame("hogwarts-legacy", "Hogwarts Legacy", "ue-pak", "Phoenix/Content/Paks/~mods", "990080"),
        new PopularGame("cyberpunk-2077", "Cyberpunk 2077", "custom", "archive/pc/mod", "1091500")
        {
            FileExtensions = new[] { "archive" },
        },
    };

    /// <summary>Look up a game by id; null when unknown (or the id is null/empty).</summary>
    public static PopularGame? Find(string? id)
        => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(g => g.Id == id);
}
