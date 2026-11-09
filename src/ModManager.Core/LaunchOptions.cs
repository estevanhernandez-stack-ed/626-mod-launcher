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
    public IReadOnlyDictionary<string, string>? Env { get; init; } // internal: environment the process needs
    public bool SkipsAntiCheat { get; init; }   // internal: starts the game without its anti-cheat (never picked for vanilla)
}

/// <summary>
/// Verified, gated catalog of per-game launch options — curated knowledge, never a guess. Each
/// game we've researched maps to the option(s) that make mods work (run the real exe offline for
/// anti-cheat games, Steam launch args, ...). Unknown App IDs return nothing; the UI offers a
/// "request research" path for those. Keyed by Steam App ID. Entries grow by engine/publisher family.
/// </summary>
public static class LaunchOptions
{
    public static IReadOnlyList<LaunchOption> For(string? appId)
    {
        var options = Catalog(appId);
#if !FULL
        // STORE SKU (sealed core): the anti-cheat toggle is stripped — never surface it, even if the
        // catalog lists one. Pairs with AntiCheat.cs being absent from the Store binary entirely.
        // An internal launch that skips anti-cheat STAYS. It is not the same class of thing as the toggle:
        // it rewrites nothing in the game folder, it runs the game's own exe with the publisher's own
        // internal-config args, and Play vanilla keeps the protected Steam start. Decided 2026-11-08 (Este):
        // the toggle swaps a file the anti-cheat owns; this only chooses which of the game's exes to start.
        return options.Where(o => o.Kind != LaunchOptionKind.AntiCheatToggle).ToArray();
#else
        return options;
#endif
    }

    static IReadOnlyList<LaunchOption> Catalog(string? appId) => appId switch
    {
        // Elden Ring — verified on disk: Game\eldenring.exe behind start_protected_game.exe (EAC).
        // EAC blocks mods, and launching eldenring.exe directly does NOT bypass it — the proven
        // method is a reversible swap (back up start_protected_game.exe, copy eldenring.exe in its
        // place). Turn off to mod offline; turn back on for online. The swap is fully reversible.
        "1245620" => new[]
        {
            new LaunchOption(
                "Online vs offline mode (anti-cheat)",
                "EasyAntiCheat blocks file-based Elden Ring mods. The trade: anti-cheat ON means "
                + "official online multiplayer works but mods don't load; anti-cheat OFF means Play "
                + "launches in offline mode with mods loaded. Seamless Co-op is its own path — it "
                + "ships a bypass and runs its own multiplayer, so Seamless works either way. "
                + "Fully reversible.",
                LaunchOptionKind.AntiCheatToggle)
            {
                Bootstrapper = "start_protected_game.exe",
                RealExe = "eldenring.exe",
                WorkingSubdir = "Game",
                Recommended = true,
            },
        },
        // ACE COMBAT 8 — verified on disk 2026-11-08: Game\Binaries\Win64\AceCombat8.exe behind
        // start_protected_game.exe (EAC) and Denuvo. EAC refuses to start with UE4SS's dwmapi.dll beside the
        // exe, so modded play is the real exe run directly with -SaveToUserDir: the exe and args of Bandai's
        // own internal "ACE COMBAT 8 No EAC" launch config in Steam's appinfo (QA branches only, not a public
        // option). No swap, no file touched. Run without SteamAppId the game exits mid-init, so it carries it.
        // Play vanilla stays the Steam launch, with UE4SS's proxy stepped aside first.
        "2288340" => new[]
        {
            new LaunchOption(
                "Play modded (offline, no anti-cheat)",
                "Starts ACE COMBAT 8 directly, offline, so UE4SS mods load. EasyAntiCheat doesn't run, so stay "
                + "out of online modes while modded. Play vanilla sets the mod loader aside and "
                + "starts the game through Steam with anti-cheat, for online play.",
                LaunchOptionKind.Internal)
            {
                Exe = @"Game\Binaries\Win64\AceCombat8.exe",
                Args = "-SaveToUserDir",
                WorkingSubdir = @"Game\Binaries\Win64",
                Env = new Dictionary<string, string> { ["SteamAppId"] = "2288340", ["SteamGameId"] = "2288340" },
                SkipsAntiCheat = true,
            },
        },
        _ => Array.Empty<LaunchOption>(),
    };

    /// <summary>True when we know this game needs a launch option (has a recommended one) — for the library highlight.</summary>
    public static bool NeedsAttention(string? appId) => For(appId).Any(o => o.Recommended);
}
