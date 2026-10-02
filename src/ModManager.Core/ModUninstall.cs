namespace ModManager.Core;

/// <summary>
/// Uninstalling a mod: whether it may be, and doing it. The one destructive mod operation. The app's
/// row (whether it offers Uninstall), its uninstall, and the agent's uninstall_mod all ask here, so
/// the agent can never delete what the app would not (E1, fifth slice).
/// </summary>
public static class ModUninstall
{
    /// <summary>Why this mod can't be uninstalled, or null when it can. Loose files a mod put in the game
    /// folder (direct-inject, loose-root) are never deleted by 626: turning the mod off moves them aside,
    /// which is reversible. A mod another tool manages is uninstalled there.</summary>
    public static string? Refusal(GameContext ctx, Mod mod) => Refusal(ModListing.MechanismFor(ctx.Game, ctx), mod);

    /// <inheritdoc cref="Refusal(GameContext, Mod)"/>
    /// <param name="lane">The game's lane, worked out once by a caller asking about many mods.</param>
    public static string? Refusal(ListingMechanism lane, Mod mod)
    {
        if (lane is ListingMechanism.DirectInject or ListingMechanism.LooseRoot)
            return $"\"{mod.Name}\" is loose files in the game folder, which 626 never deletes. Turn it off instead "
                   + "(its files move aside and can come back), or remove them by hand.";
        if (mod.ReadOnly)
            return $"\"{mod.Name}\" is managed by another tool. Uninstall it there.";
        return null;
    }

    /// <summary>Delete the mod: its live files from every location and mirror, any held (disabled) copy,
    /// and the install records that claimed them; for a Mod Engine 2 game, its folder and its config
    /// entry. Refuses (throws) when <see cref="Refusal"/> does. Locked-file errors surface.</summary>
    public static void Run(GameContext ctx, Mod mod)
    {
        var lane = ModListing.MechanismFor(ctx.Game, ctx);
        if (Refusal(lane, mod) is { } why) throw new InvalidOperationException(why);
        if (lane == ListingMechanism.ModEngine2) ModEngine2Writer.RemoveMod(ctx.Game, mod.Name);
        else Scanner.UninstallModAsync(mod.Name, ctx).GetAwaiter().GetResult();
    }
}
