namespace ModManager.Core;

/// <summary>
/// Where a turned-off mod's extra-tree entries (B4 stage two) wait: <c>&lt;dataDir&gt;/disabled-trees/&lt;Mod&gt;/&lt;tree
/// segments&gt;/&lt;entry&gt;</c>. A sibling of the primary <c>disabled</c> root rather than a folder inside it, so the
/// primary lane's own scans of <c>disabled/&lt;Mod&gt;</c> never mistake a held <c>r6/scripts</c> for a mod's files.
///
/// <para>The layout IS the record. A tree is stored the way the manifest spells it (<c>r6/scripts</c>), and
/// each segment is its own folder, so a held entry is found by walking the declared trees: no manifest file
/// to go stale, nothing to parse. Held entries are folders or files; a file such as
/// <c>r6/input/CoolMod.xml</c> is as much an entry as a folder.</para>
///
/// <para>Layout only. Which entries move is <see cref="ModTrees.MovableFor"/>'s decision, and the moving is
/// the toggle's. Cleanup goes through <see cref="HoldingFolder"/> so a held file is never deleted.</para>
/// </summary>
internal static class TreeHolding
{
    /// <summary>One held entry: its tree (manifest spelling, forward slashes), its name, and where it waits.</summary>
    public sealed record HeldEntry(string Tree, string EntryName, string AbsPath);

    // HoldingFolder skips one named record at the top level. This layout has none, and '*' can never be a
    // file name, so nothing is skipped.
    private const string NoRecord = "*";

    /// <summary>Tests only: called with the mod's holding folder before <see cref="Held"/> or
    /// <see cref="HoldsFiles"/> reads it, so a test can make the read fail as an unreadable folder would.
    /// Thread-static, like the toggle's own test hooks.</summary>
    [ThreadStatic] internal static Action<string>? BeforeReadForTests;

    /// <summary>The root of every mod's held extra-tree entries.</summary>
    public static string Root(GameContext ctx) => Path.Combine(ctx.DataDir, "disabled-trees");

    /// <summary>One mod's holding folder: <see cref="HoldingName.Folder"/>, so <c>Foo.</c> is never held in
    /// <c>Foo</c>'s folder and <c>CON</c> never names the console device.
    /// When there is no encoded folder but an older build held the mod under its raw name (v0.23.0 on
    /// Windows 11 could make <c>disabled-trees/Aux</c>), that folder, by its exact real name
    /// (<see cref="HoldingName.LegacyPath"/>), so its held entries come back and a new hold joins them.
    /// Throws for a name with no holding folder (<see cref="CanHold"/> is false); every caller asks first or
    /// goes through <see cref="Held"/> / <see cref="HoldsFiles"/>, which answer "nothing held" for it.</summary>
    public static string ModDir(GameContext ctx, string mod)
        => DirFor(ctx, mod) ?? throw new InvalidOperationException(HoldingName.TooLongMessage(mod));

    /// <summary>False when the mod has no holding folder: a risky name too long to encode
    /// (<see cref="HoldingName.Folder"/> is null) with no older raw-named hold.</summary>
    public static bool CanHold(GameContext ctx, string mod) => DirFor(ctx, mod) is not null;

    private static string? DirFor(GameContext ctx, string mod)
    {
        var root = Root(ctx);
        var folder = HoldingName.Folder(mod);
        if (folder == mod) return Path.Combine(root, folder);
        var encoded = folder is null ? null : Path.Combine(root, folder);
        if ((encoded is null || !Directory.Exists(encoded)) && HoldingName.LegacyPath(root, mod) is { } legacy)
            return legacy;
        return encoded;
    }

    /// <summary>Where an entry of <paramref name="tree"/> is held while the mod is off.</summary>
    public static string PathFor(GameContext ctx, string mod, string tree, string entry)
        => Path.Combine(ModDir(ctx, mod), Path.Combine(Segments(tree)), entry);

    /// <summary>
    /// The entries held for a mod, found by looking under each declared tree: the children of
    /// <c>ModDir/&lt;tree&gt;</c>, in tree order then name order. Declared trees are what say where an entry
    /// begins, because a tree such as <c>bin/x64/plugins/cyber_engine_tweaks/mods</c> has depth and the layout
    /// does not record it. A child that is itself the start of another declared tree (declaring both
    /// <c>r6</c> and <c>r6/scripts</c>) is that tree's folder, not an entry. A tree the manifest has since
    /// dropped is not looked for.
    /// </summary>
    public static IReadOnlyList<HeldEntry> Held(GameContext ctx, string mod, IEnumerable<string>? declaredTrees)
    {
        var result = new List<HeldEntry>();
        if (!CanHold(ctx, mod)) return result;
        var modDir = ModDir(ctx, mod);
        BeforeReadForTests?.Invoke(modDir);
        if (declaredTrees is null || !Directory.Exists(modDir)) return result;

        var trees = declaredTrees.Select(NormalizeTree).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var tree in trees)
        {
            var dir = Path.Combine(modDir, Path.Combine(Segments(tree)));
            IEnumerable<string> children;
            try { children = Directory.Exists(dir) ? Directory.EnumerateFileSystemEntries(dir).ToList() : Enumerable.Empty<string>(); }
            catch { continue; }

            foreach (var child in children.OrderBy(c => Path.GetFileName(c), StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(child);
                var asTree = tree + "/" + name;
                if (trees.Any(t => string.Equals(t, asTree, StringComparison.OrdinalIgnoreCase)
                                   || t.StartsWith(asTree + "/", StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(new HeldEntry(tree, name, child));
            }
        }
        return result;
    }

    /// <summary>True when a turned-off copy of any extra-tree entry is held: any file anywhere under the
    /// mod's folder. A folder of empty folders holds nothing.</summary>
    public static bool HoldsFiles(GameContext ctx, string mod)
    {
        if (!CanHold(ctx, mod)) return false;
        var modDir = ModDir(ctx, mod);
        BeforeReadForTests?.Invoke(modDir);
        return HoldingFolder.HoldsFiles(modDir, NoRecord);
    }

    /// <summary>Remove the mod's holding folder once no file remains under it; a file never goes.</summary>
    public static void RemoveIfEmpty(GameContext ctx, string mod)
    {
        if (CanHold(ctx, mod)) HoldingFolder.RemoveIfNoFiles(ModDir(ctx, mod));
    }

    private static string NormalizeTree(string? tree) => (tree ?? "").Replace('\\', '/').Trim('/');

    private static string[] Segments(string tree)
        => NormalizeTree(tree).Split('/', StringSplitOptions.RemoveEmptyEntries);
}
