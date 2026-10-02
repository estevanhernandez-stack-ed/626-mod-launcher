namespace ModManager.Core;

/// <summary>
/// One game's extra-tree picture for a whole mod list (B4 stage two): the trees read once, the rows'
/// names read once, and the toggle's selection rule asked per row. The toggle (<c>DisableEntry</c>) and the
/// row text both come through here, so a row never promises a move the toggle won't make.
///
/// <para>Built by <see cref="Scanner.ExtraTreeRowsFor"/>, once per reload. A game that declares no extra
/// trees gets <see cref="None"/> and pays nothing; a game whose lane is not the scanner's reads its trees
/// (so the row can still say where the mod's files are) but never the mod list.</para>
/// </summary>
public sealed class ExtraTreeRows
{
    private readonly GameContext? _ctx;
    private readonly IReadOnlyList<string> _rowNames;
    private readonly bool _laneMovesExtras;

    public static readonly ExtraTreeRows None = new(null, ModTrees.Empty, Array.Empty<string>(), false);

    /// <summary>The trees, read once.</summary>
    public ModTrees Trees { get; }

    internal ExtraTreeRows(GameContext? ctx, ModTrees trees, IReadOnlyList<string> rowNames, bool laneMovesExtras)
    {
        _ctx = ctx;
        Trees = trees;
        _rowNames = rowNames;
        _laneMovesExtras = laneMovesExtras;
    }

    /// <summary>
    /// The toggle's rule, exactly: <see cref="ModTrees.MovableFor"/> with every other row's name as a
    /// possible claimant and a tree owned (or re-deployed) by another tool as not 626's to move.
    /// </summary>
    internal ModTreeMoves Select(Mod m)
    {
        if (_ctx is null) return ModTreeMoves.None;
        var ctx = _ctx;
        var otherRows = _rowNames.Where(n => !string.Equals(n, m.Name, StringComparison.Ordinal));
        return Trees.MovableFor(m.Name, otherRows,
            // Re-deployed is owned too: the other manager put its files back into a folder the user
            // had taken over, so what is there is that manager's again, not 626's to move.
            dir => ToolOwnership.Resolve(Path.GetFullPath(dir), ctx.TakenOver).State
                is OwnershipState.Owned or OwnershipState.ReDeployed);
    }

    /// <summary>
    /// What turning <paramref name="row"/> off would move, and the trees where an entry with its name
    /// stays put, each with its reason. Adds the row-level rules the toggle applies before it ever asks
    /// for extras: a read-only row, a loader-driven mod (UE4SS, BepInEx), a proxy-loader or library row,
    /// and a game whose mods are not toggled by the scanner all move nothing, so every tree with the mod's
    /// name is held back as <see cref="HeldReason.RowNotMoved"/>.
    /// </summary>
    public ModTreeMoves MovesFor(Mod row)
    {
        if (_ctx is null) return ModTreeMoves.None;
        if (!MovesExtras(row))
            return new ModTreeMoves(Array.Empty<ModTreeEntry>(),
                Trees.For(row.Name).Select(t => new HeldTree(t, HeldReason.RowNotMoved)).ToList());
        return Select(row);
    }

    /// <summary>The trees held in <c>disabled-trees/&lt;Mod&gt;</c> for a turned-off mod, in the manifest's
    /// order, once each. Empty when the folder can't be read; <see cref="TextFor"/> says so instead.</summary>
    public IReadOnlyList<string> HeldWhileOff(string modName)
        => Held(modName).Entries.Select(e => e.Tree).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Each held entry as <c>tree/entry</c>, in the manifest's order.</summary>
    public IReadOnlyList<string> HeldWhileOffEntries(string modName)
        => Held(modName).Entries.Select(e => e.Tree + "/" + e.EntryName).ToList();

    /// <summary>The row's line and tooltip. A live row reads the toggle's selection. A turned-off row reads
    /// the held layout, names any entry still live, and names the holding folder when files are held
    /// there that no declared tree accounts for, or when it can't be read.</summary>
    public ModTreesText TextFor(Mod row)
    {
        if (_ctx is null) return ModTreesText.None;
        var moves = MovesFor(row);
        var moving = moves.Movable.Select(e => e.Tree);
        if (row.Enabled)
        {
            var live = ModTreesText.For(moving, moves.Held, Array.Empty<string>());
            // A live mod can still have files in disabled-trees/<Mod> (left under a tree the game no longer
            // declares, or a turn-on that could not move one back). Said on the row, or the user learns of
            // them only when the next turn-off refuses on them. An unreadable folder says nothing here: it is
            // not known to hold files, and a live row's line never claims it does.
            return HeldWhileLive(row.Name) is { } dir ? live.WithHeldLeftover(dir) : live;
        }

        var held = Held(row.Name);
        return ModTreesText.For(Array.Empty<string>(), moves.Held,
            held.Entries.Select(e => e.Tree), stillOn: moving, heldUnknown: held.Unknown);
    }

    // What is held for a mod. Unknown is the holding folder when files sit there under no declared tree
    // (Readable), or when the folder could not be read (not Readable): neither may fall silent, and the
    // second must never be reported as files being there.
    private (IReadOnlyList<TreeHolding.HeldEntry> Entries, TreeLeftover? Unknown) Held(string modName)
    {
        // A name too long to hold has no holding folder, so nothing can be held for it.
        if (_ctx is null || string.IsNullOrEmpty(modName) || !TreeHolding.CanHold(_ctx, modName))
            return (Array.Empty<TreeHolding.HeldEntry>(), null);
        var dir = TreeHolding.ModDir(_ctx, modName);
        try
        {
            var entries = TreeHolding.Held(_ctx, modName, _ctx.ExtraModTrees);
            if (entries.Count > 0) return (entries, null);
            return (entries, TreeHolding.HoldsFiles(_ctx, modName) ? new TreeLeftover(dir, Readable: true) : null);
        }
        catch { return (Array.Empty<TreeHolding.HeldEntry>(), new TreeLeftover(dir, Readable: false)); }
    }

    private string? HeldWhileLive(string modName)
    {
        if (_ctx is null || string.IsNullOrEmpty(modName)) return null;
        try { return TreeHolding.HoldsFiles(_ctx, modName) ? TreeHolding.ModDir(_ctx, modName) : null; }
        catch { return null; }
    }

    private bool MovesExtras(Mod row)
    {
        if (!_laneMovesExtras) return false;
        if (row.ReadOnly || row.Loader is "ue4ss" or "bepinex") return false;
        return row.Location != ProxyLoaderRows.LocationTag && row.Class != "library";
    }
}
