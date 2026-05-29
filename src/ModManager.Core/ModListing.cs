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
        // Order is load-bearing: ME2 config wins over loose direct-inject files (mirrors MainViewModel).
        IReadOnlyList<Mod> raw =
            ModEngine2Listing.IsConfigBacked(game) ? ModEngine2Listing.List(game)
            : DirectInjectListing.Applies(game)    ? DirectInjectListing.List(game)
            : Scanner.ListClassified(ctx);
        return Metadata.MergeMetadata(raw, Scanner.LoadMetadata(ctx));
    }
}
