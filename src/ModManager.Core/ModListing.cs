using ModManager.Core.LooseMods;

namespace ModManager.Core;

/// <summary>
/// The single read-only mod-listing path. The App (MainViewModel) and the headless agent-access MCP
/// both call <see cref="Resolve"/> — no second source of truth. Dispatches by engine (Mod Engine 2
/// config → direct-inject → scanner), then merges per-game metadata.json. Performs NO disk writes:
/// the scanner world's classification persist + data-dir migration stay explicit App-side steps so a
/// read tool never mutates the user's install.
/// </summary>
public static class ModListing
{
    public static IReadOnlyList<Mod> Resolve(GameEntry game)
    {
        var ctx = Scanner.GameContext(game);
        IReadOnlyList<Mod> raw = MechanismFor(game, ctx) switch
        {
            ListingMechanism.ModEngine2   => ModEngine2Listing.List(game),
            ListingMechanism.DirectInject => DirectInjectListing.List(game),
            ListingMechanism.LooseRoot    => LooseRootListing.List(game),
            _                             => Scanner.ListClassified(ctx),
        };
        var merged = Metadata.MergeMetadata(raw, Scanner.LoadMetadata(ctx));

        // Append loader rows for proxy DLLs no lane above claimed. Appended HERE, in the one shared
        // read path, so the App list and the agent-access MCP cannot disagree about what is loading
        // into the game - the parity rule. Only the direct-inject lane lists these today, and only on
        // FromSoft, which is how a Monster Hunter Wilds install showed six mods and no sign of the
        // REFramework loading them.
        var loaderRows = ProxyLoaderRowsFor(game, merged);
        return loaderRows.Count == 0 ? merged : merged.Concat(loaderRows).ToList();
    }

    /// <summary>Proxy-loader rows for a game, read off disk. Empty when the play folder is unknown.</summary>
    private static IReadOnlyList<Mod> ProxyLoaderRowsFor(GameEntry game, IReadOnlyList<Mod> alreadyListed)
    {
        var play = DirectInjectListing.PlayFolder(game.GameRoot);
        if (play is null) return Array.Empty<Mod>();

        string[] top;
        try { top = Directory.GetFiles(play); } catch { return Array.Empty<Mod>(); }

        string[] held;
        var holding = DirectInject.VanillaProxyHolding(play);
        try { held = Directory.Exists(holding) ? Directory.GetFiles(holding) : Array.Empty<string>(); }
        catch { held = Array.Empty<string>(); }

        // Directories too: a loader is named by what sits BESIDE its proxy, and REFramework's tell is
        // a reframework/ folder rather than any file. Passing only files would leave it unnamed.
        string[] dirs;
        try { dirs = Directory.GetDirectories(play); } catch { dirs = Array.Empty<string>(); }

        // Every filename any lane already produced, so a loader another lane owns is not listed twice.
        var claimed = alreadyListed.SelectMany(m => m.Files).Concat(alreadyListed.Select(m => m.Name));
        return ProxyLoaderRows.Build(top, held, claimed, top.Concat(dirs));
    }

    /// <summary>
    /// Which listing lane a game actually resolves through.
    ///
    /// <para>Order is load-bearing: ME2 config wins over loose direct-inject files (mirrors
    /// MainViewModel). A loose-root game lists through <see cref="LooseRootListing"/> (catalog +
    /// by-nature), not the pak-file scanner — decided by the ONE predicate
    /// (<see cref="LooseRootListing.Applies"/>, form-derived), the same one the App's toggle lane
    /// consults, so listing and toggling can never route differently.</para>
    ///
    /// <para>Named and exposed because the mechanism is itself an ANSWER, not just a branch: a
    /// registration can declare a Mod Engine 2 <c>mod</c> folder while every real mod arrives by
    /// direct-inject, and nothing could report that mismatch while the dispatch was an anonymous
    /// chain of ternaries. <see cref="Discovery.GameShape"/> reads it so the shape report and the
    /// mod list can never disagree about how a game is being read.</para>
    /// </summary>
    public static ListingMechanism MechanismFor(GameEntry game, GameContext? ctx = null)
    {
        ctx ??= Scanner.GameContext(game);
        return ModEngine2Listing.IsConfigBacked(game) ? ListingMechanism.ModEngine2
             : DirectInjectListing.Applies(game)      ? ListingMechanism.DirectInject
             : LooseRootListing.Applies(ctx)          ? ListingMechanism.LooseRoot
             : ListingMechanism.Scanner;
    }
}

/// <summary>How a game's mods are actually found — see <see cref="ModListing.MechanismFor"/>.</summary>
public enum ListingMechanism
{
    /// <summary>Mod Engine 2 config-backed: the toml names the mods, not the folder.</summary>
    ModEngine2,
    /// <summary>Loose files injected at the play folder (DLLs, ReShade, loader-backed subfolders).</summary>
    DirectInject,
    /// <summary>Loose files at the game root, catalog + by-nature classified (Decima and friends).</summary>
    LooseRoot,
    /// <summary>The pak-file scanner over the declared mod locations.</summary>
    Scanner,
}
