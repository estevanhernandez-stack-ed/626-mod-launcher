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

    private static readonly HashSet<string> PluginExts = new(StringComparer.OrdinalIgnoreCase) { "esp", "esm", "esl" };

    public bool Supported => Mechanism != LoadOrderMechanism.NotSupported;

    /// <summary>True when the game's mods are Creation Engine plugins, whose names other files refer to.</summary>
    public static bool IsPluginGame(string? engine, IEnumerable<string>? declaredExts)
        => string.Equals(engine, "bethesda", StringComparison.OrdinalIgnoreCase)
           || (declaredExts ?? Array.Empty<string>()).Any(PluginExts.Contains);

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
public sealed record LoadOrderUndoItem(string Location, string From, string To, IReadOnlyList<string> Dirs, bool Collision);

/// <summary>What undoing 626's load order would do, before anything moves.</summary>
public sealed record LoadOrderUndoPlan(IReadOnlyList<LoadOrderUndoItem> Items)
{
    public bool IsEmpty => Items.Count == 0;

    /// <summary>The line the game-state strip shows. "plugins" on a plugin game, "mod files" elsewhere.</summary>
    public string Describe(bool pluginGame)
    {
        var n = Items.Count;
        var noun = pluginGame ? (n == 1 ? "plugin" : "plugins") : (n == 1 ? "mod file" : "mod files");
        return $"{n} {noun} {(n == 1 ? "carries" : "carry")} a load-order prefix from 626.";
    }
}

/// <summary>What an undo did.</summary>
public sealed record LoadOrderUndoResult(
    int Renamed,
    IReadOnlyList<LoadOrderUndoItem> Collisions,
    IReadOnlyList<(string File, string Error)> Failures,
    bool LoadOrderCleared)
{
    /// <summary>The status line: the count, then every file left alone and why.</summary>
    public string Describe()
    {
        var parts = new List<string> { $"Removed 626's prefix from {Renamed} file{(Renamed == 1 ? "" : "s")}." };
        foreach (var c in Collisions) parts.Add($"{c.From} was left as is: {c.To} already exists.");
        foreach (var (file, error) in Failures) parts.Add($"{file} couldn't be renamed: {error.TrimEnd('.')}.");
        return string.Join(" ", parts);
    }
}
