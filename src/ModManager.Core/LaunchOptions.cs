namespace ModManager.Core;

/// <summary>How a launch option is applied: the app runs it, the user pastes it into Steam, or the
/// app reversibly toggles the game's anti-cheat so normal Play launches modded + offline.</summary>
public enum LaunchOptionKind { Internal, External, AntiCheatToggle }

/// <summary>
/// One verified, plain-English launch option for a game. Internal options carry an exe/args the app
/// runs directly; external options carry the <see cref="SteamOptions"/> string the user adds to
/// Steam; anti-cheat-toggle options carry the <see cref="Bootstrapper"/> + <see cref="RealExe"/> the
/// reversible swap operates on. <see cref="Recommended"/> games get highlighted in the library.
/// </summary>
public sealed record LaunchOption(string Title, string Detail, LaunchOptionKind Kind)
{
    public string? Exe { get; init; }           // internal: exe to run, relative to the game root
    public string? Args { get; init; }          // internal: process arguments
    public string? WorkingSubdir { get; init; } // working dir / play subfolder relative to the game root (e.g. "Game")
    public string? SteamOptions { get; init; }  // external: the exact string to paste into Steam
    public string? Bootstrapper { get; init; }  // anti-cheat toggle: the EAC bootstrapper exe (start_protected_game.exe)
    public string? RealExe { get; init; }        // anti-cheat toggle: the real game exe to swap in
    public bool Recommended { get; init; }      // surface a "needs a launch option" highlight
}

/// <summary>
/// Verified, gated catalog of per-game launch options — curated knowledge, never a guess. Each
/// game we've researched maps to the option(s) that make mods work (run the real exe offline for
/// anti-cheat games, Steam launch args, ...). Unknown App IDs return nothing; the UI offers a
/// "request research" path for those. Keyed by Steam App ID. Entries grow by engine/publisher family.
/// </summary>
public static class LaunchOptions
{
    public static IReadOnlyList<LaunchOption> For(string? appId) => appId switch
    {
        // Elden Ring — verified on disk: Game\eldenring.exe behind start_protected_game.exe (EAC).
        // EAC blocks mods, and launching eldenring.exe directly does NOT bypass it — the proven
        // method is a reversible swap (back up start_protected_game.exe, copy eldenring.exe in its
        // place). Turn off to mod offline; turn back on for online. The swap is fully reversible.
        "1245620" => new[]
        {
            new LaunchOption(
                "Anti-cheat (mods need it off)",
                "Elden Ring's EasyAntiCheat blocks mods. Turn it OFF and your installed mods load when "
                + "you press Play — but stay offline; don't go into online multiplayer with anti-cheat off. "
                + "Turn it back ON before playing online. Fully reversible.",
                LaunchOptionKind.AntiCheatToggle)
            {
                Bootstrapper = "start_protected_game.exe",
                RealExe = "eldenring.exe",
                WorkingSubdir = "Game",
                Recommended = true,
            },
        },
        _ => Array.Empty<LaunchOption>(),
    };

    /// <summary>True when we know this game needs a launch option (has a recommended one) — for the library highlight.</summary>
    public static bool NeedsAttention(string? appId) => For(appId).Any(o => o.Recommended);
}
