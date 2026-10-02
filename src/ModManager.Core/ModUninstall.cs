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
    public static UninstallRefusal? Refusal(GameContext ctx, Mod mod) => Refusal(ModListing.MechanismFor(ctx.Game, ctx), mod);

    /// <inheritdoc cref="Refusal(GameContext, Mod)"/>
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
            if (Refusal(lane, m) is { } why) throw new InvalidOperationException(why.Message);
        foreach (var m in mods)
        {
            if (lane == ListingMechanism.ModEngine2) ModEngine2Writer.RemoveMod(ctx.Game, m.Name);
            else Scanner.UninstallMod(m.Name, ctx);
        }
    }
}
