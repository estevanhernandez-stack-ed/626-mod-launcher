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

        // Library rows for folders in the mod location that no lane produced. Additive, like the
        // loader rows: the existing lanes keep their output untouched and this only surfaces what was
        // invisible. On a real Monster Hunter Wilds install that was _CatLib (33 files) and utility -
        // a mod the overlay cannot run without, and neither appeared anywhere in the launcher.
        var libraryRows = LibraryRowsFor(ctx, merged);

        if (loaderRows.Count == 0 && libraryRows.Count == 0) return merged;
        return merged.Concat(loaderRows).Concat(libraryRows).ToList();
    }

    /// <summary>
    /// Rows for unpaired folders in the primary mod location — libraries other mods consume.
    ///
    /// <para>Shown and NOT switchable, per the mod-provenance design: the row's value is what it
    /// explains, and a switch whose consequence needs a paragraph is a paragraph wearing a switch.
    /// <c>ReadOnly</c> carries that, since the App already refuses a toggle and an uninstall on it and
    /// only <c>IsLoader</c> bypasses the refusal — which a library must never set.</para>
    ///
    /// <para>Costs nothing on a pak game: inference finds no unpaired folder, so the dependency scan
    /// never opens a file.</para>
    /// </summary>
    private static IReadOnlyList<Mod> LibraryRowsFor(GameContext ctx, IReadOnlyList<Mod> alreadyListed)
    {
        var primary = ctx.Locations.FirstOrDefault();
        if (primary is null || !Directory.Exists(primary.Abs)) return Array.Empty<Mod>();

        string[] files, dirs;
        try
        {
            files = Directory.GetFiles(primary.Abs).Select(Path.GetFileName).Where(f => f is not null).Select(f => f!).ToArray();
            dirs = Directory.GetDirectories(primary.Abs).Select(Path.GetFileName).Where(d => d is not null).Select(d => d!).ToArray();
        }
        catch { return Array.Empty<Mod>(); }
        if (dirs.Length == 0) return Array.Empty<Mod>();   // no folders -> nothing to infer, nothing to scan

        var claimed = ModInstallRegistry.List(ctx.DataDir).SelectMany(m => m.Files);
        var disabled = DisabledKeys(ctx);
        var inferred = ModTreeInference.Group(files, dirs, claimed, disabled);

        // Only libraries, and only ones no lane already produced. A folder a lane lists is that lane's
        // to describe.
        var listed = new HashSet<string>(alreadyListed.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        var libraries = inferred.Where(r => r.Kind == InferredKind.Library && !listed.Contains(r.Key)).ToList();
        if (libraries.Count == 0) return Array.Empty<Mod>();

        var sources = LuaSourcesUnder(primary.Abs);

        var rows = new List<Mod>();
        foreach (var lib in libraries)
        {
            var dependents = LuaRequires.DependentsOf(lib.Key, sources);
            rows.Add(new Mod
            {
                Name = lib.Key,
                Base = lib.Key,
                DisplayName = lib.DisplayName,
                Class = "library",
                Location = primary.Name,
                Files = new List<string> { lib.Key },
                IsFolder = true,
                Enabled = true,
                // No toggle, no uninstall. Not IsLoader - that flag bypasses exactly this refusal.
                ReadOnly = true,
                Description = DescribeLibrary(lib.Key, dependents),
            });
        }
        return rows;
    }

    /// <summary>Consequence copy rather than a switch. Names the dependents when they can be read,
    /// says so plainly when they cannot, and points at the action that expresses what someone
    /// reaching for a switch here usually wants.</summary>
    private static string DescribeLibrary(string name, IReadOnlyList<string> dependents)
    {
        var head = $"A shared library other mods load. 626 did not install this.";
        var who = dependents.Count switch
        {
            0 => " Nothing here declares that it needs it — it may be used by a mod whose "
                 + "dependencies can't be read from its files.",
            1 => $" {dependents[0]} needs it: removing it would stop that mod loading.",
            _ => $" {dependents.Count} mods need it ({string.Join(", ", dependents)}) — removing it "
                 + "would stop all of them loading.",
        };
        return head + who + " To play without mods for a session, use Play vanilla, which steps "
             + "everything aside together and puts it back afterwards.";
    }

    /// <summary>Mod keys currently stepped aside. A disabled mod's files are in the holding folder, so
    /// without this a disabled PAIRED mod looks like an orphan folder and gets called a library.</summary>
    private static IReadOnlyList<string> DisabledKeys(GameContext ctx)
    {
        try
        {
            return Directory.Exists(ctx.DisabledRoot)
                ? Directory.GetDirectories(ctx.DisabledRoot).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList()
                : Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Every Lua file under the mod location, keyed relative to it. Capped: a dependency scan
    /// is a convenience, and an enormous tree must not make opening the mod list slow.</summary>
    private static IReadOnlyDictionary<string, string> LuaSourcesUnder(string root)
    {
        var sources = new Dictionary<string, string>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(root, "*.lua", SearchOption.AllDirectories).Take(2000))
            {
                try { sources[Path.GetRelativePath(root, f)] = File.ReadAllText(f); } catch { }
            }
        }
        catch { }
        return sources;
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
