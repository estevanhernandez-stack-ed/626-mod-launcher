namespace ModManager.Core;

/// <summary>
/// Mod Engine 2 facts for FromSoftware games (Elden Ring, Dark Souls III, Sekiro, Armored
/// Core VI). ME2 doesn't load mods through a normal Steam launch — the game must be started by
/// <c>modengine2_launcher.exe -t &lt;code&gt; -c &lt;config&gt;</c>, which boots offline (no anti-cheat)
/// and mounts the configured mod folders. This holds the pure, game-specific bits; the folder
/// scan and process launch live in the App layer.
/// </summary>
public static class ModEngine2
{
    public const string LauncherExe = "modengine2_launcher.exe";

    /// <summary>The <c>-t</c> target code Mod Engine 2 expects for a Steam App ID, or null if not a known FromSoft title.</summary>
    public static string? TargetForAppId(string? appId) => appId switch
    {
        "1245620" => "er",     // Elden Ring
        "374320" => "ds3",     // Dark Souls III
        "814380" => "sekiro",  // Sekiro: Shadows Die Twice
        "1888160" => "ac6",    // Armored Core VI
        _ => null,
    };

    /// <summary>The default config file name ME2 ships per target (e.g. config_eldenring.toml).</summary>
    public static string? ConfigNameForTarget(string? target) => target switch
    {
        "er" => "config_eldenring.toml",
        "ds3" => "config_darksouls3.toml",
        "sekiro" => "config_sekiro.toml",
        "ac6" => "config_armoredcore6.toml",
        _ => null,
    };

    /// <summary>The launcher arguments for a target + config: <c>-t &lt;code&gt; -c .\&lt;config&gt;</c>.</summary>
    public static string LaunchArgs(string target, string configFileName) => $"-t {target} -c .\\{configFileName}";
}
