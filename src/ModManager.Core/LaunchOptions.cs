namespace ModManager.Core;

/// <summary>How a launch option is applied: the app runs it, or the user pastes it into Steam.</summary>
public enum LaunchOptionKind { Internal, External }

/// <summary>
/// One verified, plain-English launch option for a game. Internal options carry an exe/args the
/// app runs directly; external options carry the <see cref="SteamOptions"/> string the user adds
/// to Steam's Launch Options box. <see cref="Recommended"/> games get highlighted in the library.
/// </summary>
public sealed record LaunchOption(string Title, string Detail, LaunchOptionKind Kind)
{
    public string? Exe { get; init; }           // internal: exe to run, relative to the game root
    public string? Args { get; init; }          // internal: process arguments
    public string? WorkingSubdir { get; init; } // internal: working dir relative to the game root (e.g. "Game")
    public string? SteamOptions { get; init; }  // external: the exact string to paste into Steam
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
        // Launching the exe directly starts the game with anti-cheat off so installed mods load.
        "1245620" => new[]
        {
            new LaunchOption(
                "Play with mods — offline",
                "Starts Elden Ring directly with EasyAntiCheat off, so your installed mods load. "
                + "Stay offline — don't go into online multiplayer with anti-cheat off; use the normal "
                + "Steam launch for online play.",
                LaunchOptionKind.Internal)
            {
                Exe = System.IO.Path.Combine("Game", "eldenring.exe"),
                WorkingSubdir = "Game",
                Recommended = true,
            },
        },
        _ => Array.Empty<LaunchOption>(),
    };

    /// <summary>True when we know this game needs a launch option (has a recommended one) — for the library highlight.</summary>
    public static bool NeedsAttention(string? appId) => For(appId).Any(o => o.Recommended);
}
