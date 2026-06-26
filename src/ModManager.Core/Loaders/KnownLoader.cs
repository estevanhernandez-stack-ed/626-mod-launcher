namespace ModManager.Core.Loaders;

/// <summary>A mod loader with a DISTINCT launcher exe the launcher can detect in the game's play
/// folder and surface as a one-click "Launch via X" button. <see cref="BanSafe"/> marks loaders whose
/// modding path avoids the game's anti-cheat (Mod Engine 2 loads mods without touching the EAC surface;
/// Seamless Co-op runs its own multiplayer). Metadata + a Get-it-here URL only — the binary is never
/// bundled.</summary>
public sealed record KnownLoader(
    string LoaderId,
    string DisplayName,
    string Engine,
    string? SteamAppId,
    IReadOnlyList<string> LauncherExeNames,
    string GetUrl,
    string Author,
    bool BanSafe,
    bool EditsSaves = false);

public static class KnownLoaderCatalog
{
    public static IReadOnlyList<KnownLoader> Catalog { get; } = new[]
    {
        new KnownLoader(
            LoaderId: "mod-engine-2",
            DisplayName: "Mod Engine 2",
            Engine: "fromsoft",
            SteamAppId: null,                               // engine-wide: ME2 is the standard FromSoft loader
                                                            // (ER, DS3, Sekiro, AC6, Nightreign) — detection keys
                                                            // off modengine2_launcher.exe being present
            LauncherExeNames: new[] { "modengine2_launcher.exe" },
            GetUrl: "https://github.com/soulsmods/ModEngine2/releases",
            Author: "soulsmods (ModEngine2)",
            BanSafe: true),                                  // loads mods without touching EAC
        new KnownLoader(
            LoaderId: "seamless-coop",
            DisplayName: "Seamless Co-op",
            Engine: "fromsoft",
            SteamAppId: "1245620",
            LauncherExeNames: new[] { "launch_elden_ring_seamlesscoop.exe", "ersc_launcher.exe" },
            GetUrl: "https://www.nexusmods.com/eldenring/mods/510",
            Author: "LukeYui",
            BanSafe: true),                                  // ships its own MP, bypasses EAC
    };
}
