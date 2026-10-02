namespace ModManager.Core;

/// <summary>Why a mod can't be uninstalled.</summary>
public enum UninstallBlock
{
    /// <summary>Loose files in the game folder (direct-inject, loose-root): 626 never deletes those.</summary>
    LooseFiles,
    /// <summary>A mod another tool manages: uninstall it there.</summary>
    ManagedByAnotherTool,
    /// <summary>A row the listing appends that is not an installed mod (a proxy loader DLL, a shared
    /// library): there is nothing of its own to delete by name. Turn it off instead.</summary>
    NotAnInstalledMod,
    /// <summary>A turned-off mod with files held in <c>disabled-trees/&lt;Mod&gt;</c> (B4 stage two): uninstall
    /// would orphan them. Turn it on first.</summary>
    HeldInOtherFolders,
}

public sealed record UninstallRefusal(UninstallBlock Kind, string Message);

/// <summary>
/// Uninstalling a mod: whether it may be, and doing it. The one destructive mod operation. The app's
/// row (whether it offers Uninstall), its uninstall and family uninstall, and the agent's uninstall_mod
/// all ask here, so the agent can never delete what the app would not (E1, fifth slice).
/// </summary>
public static class ModUninstall
{
    /// <summary>Why this mod can't be uninstalled, or null when it can.</summary>
    public static UninstallRefusal? Refusal(GameContext ctx, Mod mod) => Refusal(ctx, ModListing.MechanismFor(ctx.Game, ctx), mod);

    // The lane's rules, then the one rule that needs the game's data folder. The lane-only overload below
    // answers for the row (whether it offers Uninstall) without touching disk per row; running an uninstall
    // always comes through here, so a held-extras mod is refused in words rather than leaving orphans.
    private static UninstallRefusal? Refusal(GameContext ctx, ListingMechanism lane, Mod mod)
        => Refusal(lane, mod) ?? HeldExtrasRefusal(ctx, mod);

    // B4 stage two: a turned-off mod can hold its extra-tree entries in disabled-trees/<Mod>. The delete
    // below knows only the main files and disabled/<Mod>, so it would orphan them there, and a later copy of
    // the same mod would then refuse to turn off on them. Refused rather than deleted: those entries were
    // matched to the mod by name, and whether uninstall may delete them is Este's call, not yet made. A
    // folder that can't be read is not reported as holding files (the row-text rule), so it doesn't refuse.
    private static UninstallRefusal? HeldExtrasRefusal(GameContext ctx, Mod mod)
    {
        if (string.IsNullOrEmpty(mod.Name)) return null;
        bool holds;
        try { holds = TreeHolding.HoldsFiles(ctx, mod.Name); }
        catch { holds = false; }
        return holds
            ? new(UninstallBlock.HeldInOtherFolders,
                $"Turn \"{mod.Name}\" on first: some of its files are held in other folders.")
            : null;
    }

    /// <summary>The lane's rules only, for the row deciding whether to offer Uninstall. It does not look for
    /// held extra-tree files (<see cref="UninstallBlock.HeldInOtherFolders"/>): the row still offers Uninstall,
    /// and running it refuses with the reason, which is how the user learns to turn the mod on first.</summary>
    /// <param name="lane">The game's lane, worked out once by a caller asking about many mods.</param>
    public static UninstallRefusal? Refusal(ListingMechanism lane, Mod mod)
    {
        if (lane is ListingMechanism.DirectInject or ListingMechanism.LooseRoot)
            return new(UninstallBlock.LooseFiles,
                $"\"{mod.Name}\" is loose files in the game folder, which 626 never deletes. Turn it off instead "
                + "(its files move aside and can come back), or remove them by hand.");
        if (mod.ReadOnly)
            return new(UninstallBlock.ManagedByAnotherTool, $"\"{mod.Name}\" is managed by another tool. Uninstall it there.");
        // Appended by the listing on any lane, so the scanner's uninstall cannot find them by name and
        // would report success having deleted nothing (the trap ModToggle routes around for toggles).
        if (mod.Location == ProxyLoaderRows.LocationTag || mod.Class == "library")
            return new(UninstallBlock.NotAnInstalledMod,
                $"\"{mod.Name}\" is a loader or shared library 626 lists alongside your mods, not an installed mod. "
                + "Turn it off instead.");
        return null;
    }

    /// <summary>Delete the mod: its live files from every location and mirror, any held (disabled) copy,
    /// and the install records that claimed them; for a Mod Engine 2 game, its folder and its config
    /// entry. Refuses (throws) when <see cref="Refusal(GameContext, Mod)"/> does. Locked-file errors surface.</summary>
    public static void Run(GameContext ctx, Mod mod) => RunAll(ctx, new[] { mod });

    /// <summary>Delete several mods (a variant family) as one decision: every one is checked first, so a
    /// refused member stops the whole family before anything is deleted, rather than partway through.</summary>
    public static void RunAll(GameContext ctx, IReadOnlyList<Mod> mods)
    {
        var lane = ModListing.MechanismFor(ctx.Game, ctx);
        foreach (var m in mods)
            if (Refusal(ctx, lane, m) is { } why) throw new InvalidOperationException(why.Message);
        foreach (var m in mods)
        {
            if (lane == ListingMechanism.ModEngine2) ModEngine2Writer.RemoveMod(ctx.Game, m.Name);
            else Scanner.UninstallMod(m.Name, ctx);
        }
    }
}
