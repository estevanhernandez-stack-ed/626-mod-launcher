namespace ModManager.Core.Catalog;

/// <summary>
/// A direct-inject mod the launcher knows about. Phase 1 of the unified mod/tool/framework
/// catalog — the <see cref="Kind"/> field is "directInjectMod" today; later phases will fold
/// Tools ("tool") and Frameworks ("framework") into the same schema.
///
/// Replaces the old private <c>DirectInject.Signature</c> record. Detection field names rename
/// one-to-one (<c>Files</c> -> <see cref="InstallSignatureFiles"/>, etc.). New fields:
/// <see cref="ConfigPaths"/> drives the pencil icon (catalog-known INI/TOML paths);
/// <see cref="ForbiddenOverridePaths"/> + <see cref="InstallRoot"/> support user-configurable
/// install/config locations with safety gating later (Phase 1b).
///
/// Pure data — detection logic stays in <see cref="DirectInject"/>; config resolution lives in
/// <see cref="DirectInjectModConfigResolver"/>.
/// </summary>
public sealed record KnownDirectInjectMod(
    string Kind,
    string ModId,
    string DisplayName,
    string ChipKind,
    string Author,
    string Engine,
    string? SteamAppId,
    string? GetUrl,
    IReadOnlyList<string> InstallSignatureFiles,
    IReadOnlyList<string> InstallSignatureDirs,
    IReadOnlyList<string> InstallSignatureContains,
    string InstallRoot,
    IReadOnlyList<string> ConfigPaths,
    IReadOnlyList<string> ForbiddenOverridePaths)
{
    /// <summary>
    /// Day-one catalog. Migrated from <c>DirectInject.Signature</c> array; same detection
    /// behavior. New: <see cref="ConfigPaths"/> for the pencil icon (Seamless Co-op's
    /// seamlesscoopsettings.ini is the load-bearing entry).
    /// </summary>
    public static IReadOnlyList<KnownDirectInjectMod> Catalog { get; } = new[]
    {
        Mk(modId: "reshade", display: "ReShade", chip: "graphics", author: "crosire",
           files: new[] { "reshadepreset.ini", "reshade.ini" },
           dirs: new[] { "reshade-shaders" },
           configs: new[] { "reshade.ini", "reshadepreset.ini" }),

        Mk(modId: "seamless-coop", display: "Seamless Co-op", chip: "co-op", author: "Yui",
           getUrl: "https://www.nexusmods.com/eldenring/mods/510",
           files: new[] { "ersc.dll", "ersc_settings.ini", "launch_elden_ring_seamlesscoop.exe" },
           dirs: new[] { "seamlesscoop" },
           // Seamless ships under three install layouts across the project's history:
           //   - Current rewrite:  <playFolder>/SeamlessCoop/seamlesscoopsettings.ini
           //   - LukeYui middle:   <playFolder>/SeamlessCoop/ersc_settings.ini  (subfolder, OLD name)
           //   - LukeYui original: <playFolder>/ersc_settings.ini               (loose at game root)
           // Resolver checks all three, returns whichever exists on disk.
           configs: new[]
           {
               "SeamlessCoop/seamlesscoopsettings.ini",
               "SeamlessCoop/ersc_settings.ini",
               "ersc_settings.ini",
           }),

        Mk(modId: "erss2-frame-gen", display: "ERSS2 Frame Gen", chip: "upscaler", author: "(unknown)",
           files: new[] { "erss-fg.dll", "erss-fg.toml", "erss2loader.log" },
           dirs: new[] { "erss2" },
           configs: new[] { "erss-fg.toml" }),

        Mk(modId: "ultrawide-fix", display: "Ultrawide / Widescreen Fix", chip: "display",
           author: "(community)",
           contains: new[] { "ultrawide", "widescreen" }),

        Mk(modId: "modded-regulation", display: "Modded regulation.bin", chip: "gameplay",
           author: "(varies)",
           files: new[] { "regulation.bin" }),

        Mk(modId: "dll-mod-loader", display: "DLL mod loader", chip: "dll", author: "(community)",
           files: new[] { "dinput8.dll" }),
    };

    private static KnownDirectInjectMod Mk(
        string modId, string display, string chip, string author,
        string? getUrl = null,
        string[]? files = null, string[]? dirs = null, string[]? contains = null,
        string[]? configs = null)
        => new(
            Kind: "directInjectMod",
            ModId: modId,
            DisplayName: display,
            ChipKind: chip,
            Author: author,
            Engine: "fromsoft",
            SteamAppId: null,
            GetUrl: getUrl,
            InstallSignatureFiles: files ?? Array.Empty<string>(),
            InstallSignatureDirs: dirs ?? Array.Empty<string>(),
            InstallSignatureContains: contains ?? Array.Empty<string>(),
            InstallRoot: "PlayFolder",
            ConfigPaths: configs ?? Array.Empty<string>(),
            ForbiddenOverridePaths: Array.Empty<string>());
}
