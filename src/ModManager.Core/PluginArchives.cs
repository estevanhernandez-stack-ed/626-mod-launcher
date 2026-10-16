namespace ModManager.Core;

/// <summary>
/// Fallout 4 and Starfield archives that belong to a plugin: <c>MyMod.esp</c> owns <c>MyMod - Main.ba2</c>,
/// <c>MyMod - Textures.ba2</c> (and a bare <c>MyMod.ba2</c>). The engine loads an archive because its name
/// carries a loaded plugin's stem, so the archive has to follow the plugin on turn-off, turn-on and uninstall.
///
/// <para>The game never declares <c>ba2</c> as a mod extension (the Bethesda preset stays esp/esl/esm/bsa), so
/// this is a companion rule like <see cref="Scanner.SidecarsFor"/>'s, not a listing rule: an archive rides with
/// its plugin instead of being a row of its own. <see cref="StandaloneSidecars"/> lists the ones no plugin
/// claims.</para>
///
/// <para><b>Skyrim's <c>.bsa</c> is deliberately not here.</b> <c>bsa</c> is a declared extension, so
/// <c>Foo.bsa</c> already shares <c>Foo.esp</c>'s row by name and <c>Foo - Textures.bsa</c> is already a row of its
/// own. Folding the latter into the plugin would change what a Skyrim user sees.</para>
///
/// <para>Matching is case-insensitive and by stem followed by <c>" - "</c> or the extension, never a bare
/// prefix, so <c>Cool.esp</c> does not adopt <c>CoolExtra - Main.ba2</c>. Each archive has exactly ONE owning
/// stem: the longest plugin stem that names it (<c>MyMod - Plus.esp</c> beats <c>MyMod.esp</c> for
/// <c>MyMod - Plus - Main.ba2</c>), and between stems of one length that differ only in case, the
/// ordinal-smallest. So two rows can never both claim an archive.</para>
///
/// <para><b>Held plugins count.</b> An older build turned a plugin off by moving the plugin alone, leaving its
/// archives live. A shorter live plugin must not capture such an archive just because the longer plugin is no
/// longer in the folder, so the plugins in holding take part in the contest. The archive then has no live owner
/// and stays out of the shorter plugin's row.</para>
/// </summary>
internal static class PluginArchives
{
    internal const string Ext = ".ba2";

    /// <summary>True for a Fallout 4 / Starfield archive.</summary>
    internal static bool IsArchive(string? fileName)
        => Path.GetExtension(fileName ?? "").Equals(Ext, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The archives in <paramref name="allFiles"/> that <paramref name="pluginFile"/> owns; empty when it is
    /// not a plugin. <paramref name="allFiles"/> is the whole folder, so a longer-named plugin beside it is seen;
    /// <paramref name="heldPlugins"/> are the plugin filenames currently in holding.
    /// </summary>
    internal static IEnumerable<string> OwnedBy(
        string pluginFile, IReadOnlyList<string> allFiles, IReadOnlyCollection<string>? heldPlugins = null)
    {
        if (!BaseGameFiles.IsPlugin(pluginFile)) return Array.Empty<string>();
        var archives = allFiles.Where(IsArchive).ToList();
        if (archives.Count == 0) return Array.Empty<string>();
        var stem = Path.GetFileNameWithoutExtension(pluginFile);
        var stems = PluginStems(allFiles, heldPlugins);
        return archives.Where(a => Winner(stems, Path.GetFileNameWithoutExtension(a)) is { } w && w == stem);
    }

    /// <summary>True when a LIVE plugin owns <paramref name="archive"/>. False when none names it, or when the
    /// winning plugin is one in holding (the archive is then that held row's, left live by an older build).</summary>
    internal static bool IsClaimed(
        string archive, IReadOnlyList<string> allFiles, IReadOnlyCollection<string>? heldPlugins = null)
    {
        var stems = PluginStems(allFiles, heldPlugins);
        return Winner(stems, Path.GetFileNameWithoutExtension(archive)) is { } w
               && allFiles.Any(f => BaseGameFiles.IsPlugin(f) && Path.GetFileNameWithoutExtension(f) == w);
    }

    private static List<string> PluginStems(IReadOnlyList<string> live, IReadOnlyCollection<string>? held)
        => live.Concat(held ?? Array.Empty<string>()).Where(BaseGameFiles.IsPlugin)
               .Select(p => Path.GetFileNameWithoutExtension(p)).Distinct(StringComparer.Ordinal).ToList();

    // The single owning stem: longest that names the archive, then ordinal-smallest.
    private static string? Winner(List<string> stems, string archiveStem)
    {
        string? best = null;
        foreach (var s in stems)
        {
            if (!Names(s, archiveStem)) continue;
            if (best is null || s.Length > best.Length
                || (s.Length == best.Length && string.CompareOrdinal(s, best) < 0)) best = s;
        }
        return best;
    }

    /// <summary>True when an archive stem names a plugin stem: equal, or the stem then <c>" - "</c> and more.</summary>
    private static bool Names(string pluginStem, string archiveStem)
        => archiveStem.Equals(pluginStem, StringComparison.OrdinalIgnoreCase)
           || archiveStem.StartsWith(pluginStem + " - ", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The mod an archive no plugin claims is named for: the stem before the LAST <c>" - "</c>, so
    /// <c>Retex - Main.ba2</c> and <c>Retex - Textures.ba2</c> are one row. Deliberately the opposite split to
    /// <see cref="BaseGameFiles.IsBethesdaBaseFile"/>, which cuts at the FIRST <c>" - "</c> to find the owner:
    /// for <c>DLCCoast - Fixes - Main.ba2</c> the row is <c>DLCCoast - Fixes</c> yet the file is judged the game's
    /// by <c>DLCCoast</c>, so it locks, the safe direction. The base check must stay on the first split.
    /// </summary>
    internal static string OwnerStem(string archiveFile)
    {
        var stem = Path.GetFileNameWithoutExtension(archiveFile);
        var dash = stem.LastIndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? stem[..dash] : stem;
    }
}
