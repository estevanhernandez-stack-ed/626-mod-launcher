namespace ModManager.Core.Stores;

/// <summary>
/// Steam installs that are not games: runtimes, compatibility layers and redistributables every Steam
/// library carries. They have an appmanifest like any game, so a list of "games on this system" built
/// from appmanifests shows them with a Play button unless told otherwise (B6 review).
///
/// <para>Deliberately narrow. Dedicated servers stay: people mod them, and adding one is a real thing
/// to do. Only what can never be a mod target is left out.</para>
/// </summary>
public static class SteamNonGames
{
    // The ones every machine has, by id, so a localised or renamed manifest still matches.
    private static readonly HashSet<string> Ids = new(StringComparer.Ordinal)
    {
        "228980",   // Steamworks Common Redistributables
        "250820",   // SteamVR
        "1070560",  // Steam Linux Runtime 1.0 (scout)
        "1391110",  // Steam Linux Runtime 2.0 (soldier)
        "1628350",  // Steam Linux Runtime 3.0 (sniper)
        "1493710",  // Proton Experimental
    };

    // And by name, for the families that gain a new id with every release.
    private static readonly string[] NamePrefixes =
    {
        "Proton ",
        "Steam Linux Runtime",
        "Steamworks Common Redistributables",
        "SteamVR",
    };

    public static bool Is(InstalledGame game)
        => game.StoreKind == "steam"
           && (Ids.Contains(game.AppId)
               || string.Equals(game.Name, "Proton", StringComparison.OrdinalIgnoreCase)
               || NamePrefixes.Any(p => game.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
}
