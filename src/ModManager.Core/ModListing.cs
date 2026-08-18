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

        // Not in a folder the GAME owns. Calling an unexplained directory a library is sound reasoning
        // inside a folder dedicated to mods - an unpaired folder in ~mods really is probably a library.
        // It is unsound when the mod location IS the game root, where an unexplained directory is just
        // the game: Death Stranding 2 listed LocalCacheWinGame, steaminput, tools, uds and the user's
        // own _MODS_STAGING as mods, and read "15 of 15 enabled" on an install with nine (A23).
        //
        // A directory in the game root still becomes a row on EVIDENCE - a lane that lists it, or an
        // install manifest claiming it (A25). What it never does is become one by sitting there.
        if (IsGameRoot(primary.Abs, ctx.GameRoot)) return Array.Empty<Mod>();

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
        // Matched on the files a row OWNS as well as its name. Matching names alone missed the case
        // where a lane claims a directory under a different name: the ReShade row is called "ReShade"
        // and owns "reshade-shaders", so the folder was listed twice - once inside ReShade, once as its
        // own library row. That double claim is not cosmetic. Play vanilla moved the directory (rightly,
        // under the row that owns it) even though the library row was ReadOnly and excluded, which means
        // a directory belonging to two rows is protected only as strongly as its weakest claim.
        var listed = ClaimedBy(alreadyListed);
        var libraries = inferred.Where(r => r.Kind == InferredKind.Library && !listed.Contains(r.Key)).ToList();
        if (libraries.Count == 0) return Array.Empty<Mod>();

        // Sources from the mod folder AND the holding folder. A disabled mod's files are stepped
        // aside, so scanning only what is live conflates "nothing needs this" with "its dependent is
        // switched off" - and those want opposite answers. Reading holding is what lets the row say
        // which one it is.
        var sources = LuaSourcesUnder(primary.Abs);
        var heldSources = LuaSourcesUnder(ctx.DisabledRoot);

        var enabledByName = alreadyListed
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Any(m => m.Enabled), StringComparer.OrdinalIgnoreCase);

        var rows = new List<Mod>();
        foreach (var lib in libraries)
        {
            var live = LuaRequires.DependentsOf(lib.Key, sources);
            var held = LuaRequires.DependentsOf(lib.Key, heldSources);
            var dependents = live.Concat(held).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // A dependent counts as ON when the listing says so. Unknown to the listing but present in
            // the tree counts as ON too: something is there, and assuming it is off is the assumption
            // that breaks a working game.
            var enabled = dependents
                .Where(d => !enabledByName.TryGetValue(d, out var on) || on)
                .ToList();

            var state = dependents.Count == 0 ? LibraryState.Unknown
                      : enabled.Count > 0     ? LibraryState.InUse
                      :                         LibraryState.Idle;

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
                // Switchable only when we KNOW its dependents and none of them is on. Unknown is not
                // permission: "nothing declared that it needs this" and "nothing needs this" are
                // different claims, and only the second one makes a toggle safe.
                ReadOnly = state != LibraryState.Idle,
                Description = DescribeLibrary(state, dependents, enabled),
            });
        }
        return rows;
    }

    /// <summary>Everything the already-listed rows account for: their names AND the files they own.
    /// A lane that lists a directory is that lane's to describe, and it may well call it something
    /// else — the ReShade row is named "ReShade" and owns "reshade-shaders", so matching on names
    /// alone let the same folder appear twice, once inside ReShade and once as its own library row
    /// (A23).</summary>
    internal static HashSet<string> ClaimedBy(IReadOnlyList<Mod> rows)
        => new(rows.Select(m => m.Name).Concat(rows.SelectMany(m => m.Files)), StringComparer.OrdinalIgnoreCase);

    /// <summary>True when a mod location resolves to the game root itself, as a loose-root
    /// registration's does. Compared on normalised full paths so a trailing separator or a different
    /// spelling of the same folder cannot make the game root look like a dedicated mod folder.</summary>
    private static bool IsGameRoot(string locationAbs, string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot)) return false;
        try
        {
            var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(locationAbs));
            var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Whether a library can be switched off right now, and why.</summary>
    private enum LibraryState
    {
        /// <summary>Something that needs it is on. Refuse.</summary>
        InUse,
        /// <summary>Its dependents are known and all of them are off. Nothing breaks - allow it.</summary>
        Idle,
        /// <summary>Nothing readable declares it. Refuse: we did not learn that nothing needs it, we
        /// learned that we could not tell.</summary>
        Unknown,
    }

    /// <summary>Consequence copy rather than a switch. Names the dependents when they can be read,
    /// says so plainly when they cannot, and points at the action that expresses what someone
    /// reaching for a switch here usually wants.</summary>
    private static string DescribeLibrary(
        LibraryState state, IReadOnlyList<string> dependents, IReadOnlyList<string> enabled)
    {
        const string head = "A shared library other mods load. 626 did not install this.";
        return state switch
        {
            LibraryState.InUse when enabled.Count == 1 =>
                $"{head} {enabled[0]} is on and needs it — turning it off would stop that mod loading. "
                + "To play without mods for a session, use Play vanilla, which steps everything aside "
                + "together and puts it back afterwards.",

            LibraryState.InUse =>
                $"{head} {enabled.Count} mods that are on need it ({string.Join(", ", enabled)}) — "
                + "turning it off would stop all of them loading. To play without mods for a session, "
                + "use Play vanilla, which steps everything aside together and puts it back afterwards.",

            LibraryState.Idle =>
                $"{head} Nothing that needs it is on right now — {Names(dependents)} would need it "
                + "again when turned back on. Safe to switch off until then.",

            _ =>
                $"{head} Nothing readable declares that it needs it, which is not the same as nothing "
                + "needing it — a mod may load it in a way we can't see from its files. Left switched "
                + "on for that reason. To play without mods for a session, use Play vanilla.",
        };
    }

    private static string Names(IReadOnlyList<string> xs)
        => xs.Count == 1 ? xs[0] : string.Join(", ", xs);

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
