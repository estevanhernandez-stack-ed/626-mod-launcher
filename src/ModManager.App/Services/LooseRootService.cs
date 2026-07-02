using ModManager.Core;
using ModManager.Core.LooseMods;

namespace ModManager.App.Services;

/// <summary>
/// App-layer bridge for loose-root (decima) mods — loose files in the GAME ROOT recognized by the
/// DirectInject signature catalog + the by-nature detector (<see cref="LooseModScan"/>). The toggle
/// is not a new mechanism: it reuses the proven <see cref="DirectInject.Disable"/> /
/// <see cref="DirectInject.Enable"/> reversible move with <see cref="LooseRootListing.Holding"/>
/// (&lt;dataDir&gt;/loose-disabled) as the holding root — byte-for-byte restore, no-clobber, never a
/// delete. Structural twin of <see cref="DirectInjectService.SetEnabled"/>; the listing itself stays
/// in Core (<see cref="LooseRootListing"/>, dispatched by <c>ModListing.Resolve</c>). Stateless, so
/// static — no DI registration needed.
/// </summary>
public static class LooseRootService
{
    /// <summary>True for loose-root games (mods drop as loose files into the game root). Delegates
    /// to the ONE predicate (<see cref="LooseRootListing.Applies(GameContext)"/>, form-derived) so
    /// the App lane and <c>ModListing.Resolve</c> can never route the same game differently.</summary>
    public static bool Applies(GameEntry game) => LooseRootListing.Applies(game);

    /// <summary>Overload for callers already holding the resolved context (the VM's game context).</summary>
    public static bool Applies(GameContext ctx) => LooseRootListing.Applies(ctx);

    /// <summary>Toggle one loose-root mod by name — a reversible move to/from the holding root.
    /// Enabling an unrestorable holding entry (corrupt/missing sidecar) is a safe no-op inside
    /// <see cref="DirectInject.Enable"/>; disabling an unknown name is a safe no-op here.</summary>
    public static void SetEnabled(GameEntry game, string modName, bool enabled)
    {
        var folder = LooseRootListing.PlayFolder(game.GameRoot);
        if (folder is null) return;
        var holding = LooseRootListing.Holding(game);
        if (enabled) { DirectInject.Enable(folder, holding, modName); return; }

        var mod = LooseRootListing.Enabled(folder).FirstOrDefault(m => m.Name == modName);
        if (mod is not null) DirectInject.Disable(folder, holding, mod);
    }
}
