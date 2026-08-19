namespace ModManager.Core;

/// <summary>How much a condition costs. Drives both the ordering and how loudly it renders.</summary>
public enum GameStateSeverity
{
    /// <summary>Something is lost or broken: an account, or mods that will not load.</summary>
    Danger,
    /// <summary>Something may be wrong, or affects someone else.</summary>
    Warning,
    /// <summary>True and worth knowing. Costs nothing.</summary>
    Info,
}

/// <summary>One thing that is true about this game right now.</summary>
/// <param name="Id">Stable and kebab-case — a harness keys on it, so it outlives any label change.</param>
/// <param name="Label">The chip's face. Short enough to sit in a row of them.</param>
/// <param name="Detail">The sentence it expands to. Says the CONSEQUENCE, not the definition — the
/// pattern the LOADER and BAN RISK copy already prove works on someone who has never modded.</param>
/// <param name="ActionLabel">What the button says, when there is one to press.</param>
/// <param name="Dismissible">Whether the user may put it away for the session.</param>
public sealed record GameStateChip(
    string Id,
    GameStateSeverity Severity,
    string Label,
    string Detail,
    string? ActionLabel,
    bool Dismissible);

/// <summary>What holds true for a game, as the view-model already knows it.</summary>
public sealed record GameStateConditions
{
    public bool BanRisk { get; init; }
    public bool LaunchOptionsNeeded { get; init; }
    /// <summary>Non-empty when a framework this game needs is missing or half-installed — the sentence
    /// A13 taught the launcher to say, e.g. "UE4SS — loader present, runtime missing".</summary>
    public string? MissingFrameworks { get; init; }
    public bool SetupDrift { get; init; }
    public bool SteamUpdated { get; init; }
    public string? SteamMessage { get; init; }
    public bool CoopLauncherMissing { get; init; }
    /// <summary>Non-empty when enabled mods may desync co-op.</summary>
    public string? MpWarning { get; init; }
    public bool VortexReDeployed { get; init; }
    public bool VortexManaged { get; init; }
}

/// <summary>
/// One place for what is true about this game, in one order.
///
/// <para><b>Why this is in Core.</b> The app used to say these things in two registers and eight
/// places: four full-width banners stacked above the mod list in DECLARATION ORDER, and four inline
/// warnings squeezed into the command bar's right cluster. So the weights ran backwards against
/// consequence — the warning that can cost someone their account rendered as a
/// <c>Border Padding="8,2"</c> beside the theme picker, while "another tool has files in a folder"
/// took a full-width bar and pushed the mods down the screen.</para>
///
/// <para>The ranking is the part worth arguing about, so it lives here under test rather than being
/// re-decided by whatever order someone happened to add markup in.</para>
///
/// <para><b>The principle:</b> what breaks, and how far outside this machine the damage reaches.
/// Account first. Then "nothing loads". Then "some things do not load". Then "this may be stale".
/// Then "other people". Then "another tool owns this".</para>
/// </summary>
public static class GameStateStrip
{
    public static IReadOnlyList<GameStateChip> For(GameStateConditions? c)
    {
        var chips = new List<GameStateChip>();
        if (c is null) return chips;

        // 1. The only one whose cost lands outside this machine.
        if (c.BanRisk)
            chips.Add(new GameStateChip("ban-risk", GameStateSeverity.Danger, "BAN RISK",
                "This game uses anti-cheat — enabling mods for online play can get your account banned.",
                null, Dismissible: false));

        // 2. Nothing loads at all until this is set.
        if (c.LaunchOptionsNeeded)
            chips.Add(new GameStateChip("launch-options", GameStateSeverity.Danger, "LAUNCH OPTION",
                "This game needs a launch option set before any mod will load.",
                "Show me", Dismissible: false));

        // 3. The mods that need it do not load. Until this wave, this state had no surface at all —
        // the summary was computed on every reload and bound in no XAML file.
        if (!string.IsNullOrWhiteSpace(c.MissingFrameworks))
            chips.Add(new GameStateChip("framework-missing", GameStateSeverity.Danger, "FRAMEWORK",
                c.MissingFrameworks!.Trim() + " — mods that need it will not load.",
                "Get it", Dismissible: false));

        // 4. The launcher is looking somewhere the mods are not.
        if (c.SetupDrift)
            chips.Add(new GameStateChip("setup-drift", GameStateSeverity.Warning, "SETUP",
                "No mods found, and the folder this game is set to look in doesn't exist.",
                "Check setup", Dismissible: true));

        // 5. What worked yesterday may not today.
        if (c.SteamUpdated)
            chips.Add(new GameStateChip("steam-updated", GameStateSeverity.Warning, "UPDATED",
                string.IsNullOrWhiteSpace(c.SteamMessage)
                    ? "Steam updated this game since you last modded it — your installed mods may need rechecking."
                    : c.SteamMessage!.Trim(),
                // An ACTION, not a dismissal: it re-records the baseline. Keeping the distinction is
                // why the strip can state one dismiss rule and still keep this button.
                "Mark as rechecked", Dismissible: false));

        // 6. Co-op specifically is broken.
        if (c.CoopLauncherMissing)
            chips.Add(new GameStateChip("coop-launcher", GameStateSeverity.Warning, "CO-OP",
                "Seamless Co-op is installed but its launcher is missing — co-op needs the full mod.",
                "What to do", Dismissible: false));

        // 7. Affects other people rather than this install.
        if (!string.IsNullOrWhiteSpace(c.MpWarning))
            chips.Add(new GameStateChip("mp-desync", GameStateSeverity.Warning, "MP RISK",
                c.MpWarning!.Trim(), null, Dismissible: false));

        // 8. A takeover was undone behind the user's back — worse than never having taken over.
        if (c.VortexReDeployed)
            chips.Add(new GameStateChip("vortex-redeployed", GameStateSeverity.Warning, "VORTEX",
                "Vortex re-deployed into a folder you took over.",
                "Take over again", Dismissible: false));

        // 9. True, worth knowing, costs nothing.
        else if (c.VortexManaged)
            chips.Add(new GameStateChip("vortex-managed", GameStateSeverity.Info, "VORTEX",
                "Some folders here are managed by Vortex, so 626 leaves them alone.",
                "Take them over", Dismissible: true));

        return chips;
    }

    /// <summary>The chip that reads as a full sentence without anyone tapping anything: the first
    /// one, which by the ordering above is the most consequential thing true about this game.
    ///
    /// <para>This deliberately does NOT gate on <see cref="GameStateSeverity.Danger"/>. It did at
    /// first, and the rig showed what that costs: Windrose's only news is "Steam updated this game",
    /// which used to be a full-width banner carrying its sentence AND a one-click <i>Mark as
    /// rechecked</i>. Behind a severity gate it collapsed to a four-letter chip reading UPDATED, with
    /// the sentence and the button both a tap away. Fewer surfaces has to mean less clutter, never
    /// less information — a state that was legible before must stay legible after.</para>
    ///
    /// <para>So severity decides COLOUR and ORDER. It does not decide whether the user is told.</para></summary>
    public static GameStateChip? LeadFor(IReadOnlyList<GameStateChip>? chips)
        => chips is null || chips.Count == 0 ? null : chips[0];
}
