namespace ModManager.Core;

/// <summary>How a game's load order is applied, if at all.</summary>
public enum LoadOrderMechanism
{
    /// <summary>Filename prefixes (<c>0010__Name.pak</c>): the loader reads its folder alphabetically.</summary>
    PrefixRename,
    /// <summary>The mod loader's own config lists the mods in order (Mod Engine 2).</summary>
    Config,
    /// <summary>626 has no safe way to apply an order here. <see cref="LoadOrderSupport.Reason"/> says why.</summary>
    NotSupported,
}

/// <summary>
/// The one answer to "can 626 arrange this game's load order, and how". The App asks it before entering
/// load-order mode and <c>Scanner.ApplyLoadOrder</c> asks it again before renaming anything, so a caller
/// that skips the first check still cannot rename a Bethesda plugin.
///
/// <para><b>Why Bethesda is refused.</b> Prefix-renaming works where the loader reads a folder in
/// alphabetical order and nothing else names the files (Unreal paks). A Creation Engine game names its
/// plugins in Plugins.txt, and every plugin names its masters by filename inside the file itself, so
/// <c>0010__MyMod.esp</c> is simply a missing <c>MyMod.esp</c> — the mod stops loading and anything that
/// depends on it fails with it. The same holds for any game whose mods are <c>.esp</c>/<c>.esm</c>/<c>.esl</c>
/// plugins (a Morrowind added as a custom game), so the rule keys on the plugin extensions as well as the
/// engine id.</para>
/// </summary>
public sealed record LoadOrderSupport(LoadOrderMechanism Mechanism, string? Reason)
{
    public const string BethesdaReason =
        "On Bethesda games the load order lives in Plugins.txt. 626 doesn't edit it yet, so it won't rename your plugins.";

    public const string IndependentReason = "Load order doesn't apply to these mods — they load independently.";

    // Plugins, plus the archives that load only because their name matches a plugin's (bsa/ba2) and
    // OpenMW's content files. No other engine uses any of these extensions.
    private static readonly HashSet<string> PluginGameExts = new(StringComparer.OrdinalIgnoreCase) { "esp", "esm", "esl", "bsa", "ba2", "omwaddon" };

    private static readonly HashSet<string> PluginExts = new(StringComparer.OrdinalIgnoreCase) { "esp", "esm", "esl" };

    /// <summary>True when <paramref name="fileName"/> is a Creation Engine plugin (esp/esm/esl).</summary>
    public static bool IsPluginFile(string fileName) => PluginExts.Contains(Path.GetExtension(fileName).TrimStart('.'));

    public bool Supported => Mechanism != LoadOrderMechanism.NotSupported;

    /// <summary>True when the game's mods are Creation Engine plugins, whose names other files refer to.</summary>
    public static bool IsPluginGame(string? engine, IEnumerable<string>? declaredExts)
        => string.Equals(engine, "bethesda", StringComparison.OrdinalIgnoreCase)
           || (declaredExts ?? Array.Empty<string>()).Any(PluginGameExts.Contains);

    /// <param name="c">The resolved game.</param>
    /// <param name="configBacked">The App's Mod Engine 2 config owns this game's order.</param>
    /// <param name="loadsIndependently">Direct-inject or loose-root mods: there is no order to arrange.</param>
    public static LoadOrderSupport For(GameContext c, bool configBacked = false, bool loadsIndependently = false)
    {
        if (configBacked) return new(LoadOrderMechanism.Config, null);
        if (loadsIndependently) return new(LoadOrderMechanism.NotSupported, IndependentReason);
        if (IsPluginGame(c.Game.Engine, c.DeclaredExts)) return new(LoadOrderMechanism.NotSupported, BethesdaReason);
        return new(LoadOrderMechanism.PrefixRename, null);
    }
}

/// <summary>One file 626's load order renamed, and what undoing it would do.</summary>
/// <param name="Location">The mod location's name.</param>
/// <param name="From">The prefixed filename as it is on disk.</param>
/// <param name="To">The original name the prefix is stripped back to.</param>
/// <param name="Dirs">Every folder (primary first, then mirrors) holding <paramref name="From"/>; renamed in lockstep.</param>
/// <param name="Collision">True when <paramref name="To"/> already exists in one of those folders: neither file is touched.</param>
/// <param name="MirrorHeldBy">The tool that owns a mirror holding <paramref name="From"/>, when one does. 626 never
/// renames inside another tool's folder, and renaming the primary alone would leave the two under different names,
/// so every copy is left as it is.</param>
/// <param name="Group">The mod this file belongs to (its mod key). Undo moves a group all or nothing.</param>
/// <param name="Taken">The original name, of any file in the group, that already exists — what blocks the group.</param>
public sealed record LoadOrderUndoItem(string Location, string From, string To, IReadOnlyList<string> Dirs, bool Collision,
    string? MirrorHeldBy = null, string? Group = null, string? Taken = null)
{
    /// <summary>True when undo will leave this file as it is.</summary>
    public bool Blocked => Collision || MirrorHeldBy is not null;

    /// <summary>Why it is left, in the words the status line uses.</summary>
    public string WhyLeft => Collision ? $"{Taken ?? To} already exists" : $"a mirror folder is managed by {MirrorHeldBy}";
}

/// <summary>What undoing 626's load order would do, before anything moves.</summary>
public sealed record LoadOrderUndoPlan(IReadOnlyList<LoadOrderUndoItem> Items)
{
    public bool IsEmpty => Items.Count == 0;

    /// <summary>True when there are prefixes and Undo can take none of them off. Undo cannot clear the
    /// chip then, so the chip has to say so and be dismissible rather than nag on every reload.</summary>
    public bool Stuck => Items.Count > 0 && Items.All(i => i.Blocked);

    /// <summary>The line the game-state strip shows.</summary>
    public string Describe(bool pluginGame)
    {
        var n = Items.Count;
        var one = n == 1;
        if (Stuck)
        {
            var why = Items.All(i => i.Collision)
                ? (one ? "its original name is taken" : "their original names are taken")
                : (one ? "626 can't safely put its original name back" : "626 can't safely put their original names back");
            return $"{n} {(one ? "file carries" : "files carry")} 626's load-order prefix, but {why}; see the status for which.";
        }
        if (pluginGame)
        {
            // "plugins" only when every one is a plugin: an archive or a sidecar is a file, not a plugin.
            var noun = Items.All(i => LoadOrderSupport.IsPluginFile(i.From)) ? (one ? "plugin" : "plugins") : (one ? "file" : "files");
            return $"{n} {noun} {(one ? "carries" : "carry")} a load-order prefix from 626.";
        }
        // Where the prefix IS the order working as asked, the chip says that, not something that reads like damage.
        return one
            ? "Load order applied by renaming 1 file. Undo puts the original name back."
            : $"Load order applied by renaming {n} files. Undo puts the original names back.";
    }
}

/// <summary>What an undo did.</summary>
/// <param name="LeftAlone">Planned files undo would not touch: a taken original name, or an owned mirror.</param>
public sealed record LoadOrderUndoResult(
    int Renamed,
    IReadOnlyList<LoadOrderUndoItem> LeftAlone,
    IReadOnlyList<(string File, string Error)> Failures,
    bool LoadOrderCleared)
{
    /// <summary>The status line: what was renamed, then every file left as it is and why — blocked up
    /// front, or rolled back because a rename failed partway.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Renamed > 0) parts.Add($"Removed 626's prefix from {Renamed} file{(Renamed == 1 ? "" : "s")}.");
        var left = LeftAlone.Select(i => $"{i.From} ({i.WhyLeft})")
            .Concat(Failures.Select(f => $"{f.File} ({f.Error.TrimEnd('.')})"))
            .ToList();
        if (left.Count > 0)
        {
            var lead = left.Count == 1 ? "626 left this as it is: " : "626 left these as they are: ";
            parts.Add(lead + string.Join(", ", left) + ".");
        }
        if (parts.Count == 0) parts.Add("No files carry 626's load-order prefix.");
        return string.Join(" ", parts);
    }
}

/// <summary>What applying an order did.</summary>
/// <param name="Support">The rule's answer; when it refused, nothing was touched.</param>
/// <param name="Placed">Mods now in the requested order (renamed, already named for it, or reordered in a UE4SS manifest).</param>
/// <param name="LeftAlone">Mods 626 kept their names for, and why.</param>
/// <param name="Saved">Whether the order was recorded. Never when nothing was placed.</param>
public sealed record LoadOrderApplyResult(
    LoadOrderSupport Support,
    int Placed,
    IReadOnlyList<(string Mod, string Why)> LeftAlone,
    bool Saved)
{
    public LoadOrderMechanism Mechanism => Support.Mechanism;
    public string? Reason => Support.Reason;
    public bool Supported => Support.Supported;

    /// <summary>The status line. Says "applied" only when something was.</summary>
    public string Describe()
    {
        if (!Supported) return Reason ?? LoadOrderSupport.IndependentReason;
        var list = string.Join(", ", LeftAlone.Select(x => $"{x.Mod} ({x.Why.TrimEnd('.')})"));
        if (!Saved) return $"626 didn't apply the load order: {list}.";
        if (LeftAlone.Count == 0) return "Load order applied.";
        return $"Load order applied. 626 left {(LeftAlone.Count == 1 ? "this mod as it is" : "these mods as they are")}: {list}.";
    }
}
