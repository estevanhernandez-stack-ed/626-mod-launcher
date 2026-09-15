using ModManager.Core.LooseMods;

namespace ModManager.Core;

/// <summary>
/// The one write path for turning a single mod on or off, shared by the app's row toggle and the
/// agent-access MCP's <c>set_mod_enabled</c>.
///
/// <para><b>Why this exists.</b> Reading was already shared — <see cref="ModListing.Resolve"/> is one
/// path so the app and the MCP cannot disagree about what is installed. Writing was not. The app's
/// view-model picked a lane per game; the MCP tool sent every mod through the scanner's folder move. On
/// a direct-inject game that enable looked for a holding record in the scanner's holding folder, found
/// none, returned an outcome nobody read, and the tool reported success. On 2026-09-14 four Elden Ring
/// mods stayed off while an agent said it had turned them on. A lane chosen in two places is a lane
/// that will one day be chosen differently.</para>
///
/// <para><b>Order matters and mirrors the listing.</b> Proxy-loader and library rows are appended by
/// <see cref="ModListing.Resolve"/> whatever lane listed the game, so they are claimed first; then the
/// game's <see cref="ModListing.MechanismFor"/> decides, the same dispatch that listed the row.</para>
///
/// <para>Gates are not in here. The ban-risk acknowledgment, the loader-disable warning and the
/// managed-folder opt-in belong to each caller, which decides how to ask.</para>
/// </summary>
public static class ModToggle
{
    public static async Task SetEnabledAsync(GameContext ctx, Mod mod, bool enabled)
    {
        var game = ctx.Game;

        // A game with nowhere for mods to live has no lane to route to. Refuse in words, before any
        // lane could index an empty location list.
        if (ModListing.HasNoModLane(ctx))
            throw new InvalidOperationException(ModListEmptyState.NoModLane);

        // Appended by ModListing on any lane, so it must be claimed before the lane dispatch below
        // routes a DLL step-aside through a mod mover that knows nothing about it.
        if (mod.Location == ProxyLoaderRows.LocationTag)
        {
            SetProxyLoaderEnabled(game, mod.Name, enabled);
            return;
        }

        // Also appended by ModListing, so BuildModList cannot resolve it by name and an ordinary move
        // would silently do nothing. The row itself goes over.
        if (mod.Class == "library")
        {
            await Scanner.SetAppendedRowEnabledAsync(mod, enabled, ctx);
            return;
        }

        switch (ModListing.MechanismFor(game, ctx))
        {
            case ListingMechanism.ModEngine2:
                ModEngine2Writer.SetEnabled(game, mod.Name, enabled);
                break;
            case ListingMechanism.DirectInject:
                SetDirectInjectEnabled(game, mod.Name, enabled);
                break;
            case ListingMechanism.LooseRoot:
                SetLooseRootEnabled(game, mod.Name, enabled);
                break;
            default:
                await Scanner.SetLoaderModEnabledAsync(mod.Name, enabled, ctx);
                break;
        }
    }

    /// <summary>True when the listing now shows <paramref name="modName"/> in the requested state. A
    /// name the listing does not have is not applied. Callers that report an outcome to someone else —
    /// the MCP tool — check this after writing instead of assuming the write took.</summary>
    public static bool IsApplied(GameEntry game, string modName, bool enabled)
        => ModListing.Resolve(game).FirstOrDefault(m =>
               string.Equals(m.Name, modName, StringComparison.OrdinalIgnoreCase)) is { } row
           && row.Enabled == enabled;

    /// <summary>Direct-inject (FromSoft, no Mod Engine 2): a reversible move of the mod's own entries in
    /// the play folder to and from <see cref="DirectInjectListing.Holding"/>. Enabling an unknown or
    /// unrestorable name and disabling an unknown name are safe no-ops.</summary>
    public static void SetDirectInjectEnabled(GameEntry game, string modName, bool enabled)
    {
        var folder = DirectInjectListing.PlayFolder(game.GameRoot);
        if (folder is null) return;
        var holding = DirectInjectListing.Holding(game);
        if (enabled) { DirectInject.Enable(folder, holding, modName); return; }

        var mod = DirectInjectListing.Enabled(folder).FirstOrDefault(m => m.Name == modName);
        if (mod is not null) DirectInject.Disable(folder, holding, mod);
    }

    /// <summary>Loose-root (decima): the same reversible move, in the game root, with
    /// <see cref="LooseRootListing.Holding"/> as the holding root.</summary>
    public static void SetLooseRootEnabled(GameEntry game, string modName, bool enabled)
    {
        var folder = LooseRootListing.PlayFolder(game.GameRoot);
        if (folder is null) return;
        var holding = LooseRootListing.Holding(game);
        if (enabled) { DirectInject.Enable(folder, holding, modName); return; }

        var mod = LooseRootListing.Enabled(folder).FirstOrDefault(m => m.Name == modName);
        if (mod is not null) DirectInject.Disable(folder, holding, mod);
    }

    /// <summary>A process-load proxy DLL at the top of the play folder: stepped aside into, and restored
    /// from, the vanilla-proxy holding folder.</summary>
    public static void SetProxyLoaderEnabled(GameEntry game, string proxyDll, bool enabled)
    {
        var folder = DirectInjectListing.PlayFolder(game.GameRoot);
        if (folder is null) return;
        var holding = DirectInject.VanillaProxyHolding(folder);
        if (enabled) DirectInject.EnableSingleFile(folder, holding, proxyDll);
        else DirectInject.DisableSingleFile(folder, holding, proxyDll);
    }
}
