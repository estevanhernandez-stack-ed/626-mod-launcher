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
/// prefix, so <c>Cool.esp</c> does not adopt <c>CoolExtra - Main.ba2</c>. When two plugins both match
/// (<c>MyMod.esp</c> and <c>MyMod - Plus.esp</c> against <c>MyMod - Plus - Main.ba2</c>) the longer stem owns
/// it.</para>
/// </summary>
internal static class PluginArchives
{
    internal const string Ext = ".ba2";

    /// <summary>True for a Fallout 4 / Starfield archive.</summary>
    internal static bool IsArchive(string? fileName)
        => Path.GetExtension(fileName ?? "").Equals(Ext, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The archives in <paramref name="allFiles"/> that <paramref name="pluginFile"/> owns; empty when it is
    /// not a plugin. <paramref name="allFiles"/> is the whole folder, so a longer-named plugin beside it is seen.
    /// </summary>
    internal static IEnumerable<string> OwnedBy(string pluginFile, IReadOnlyList<string> allFiles)
    {
        if (!BaseGameFiles.IsPlugin(pluginFile)) return Array.Empty<string>();
        var stem = Path.GetFileNameWithoutExtension(pluginFile);
        List<string>? longer = null;
        return allFiles.Where(n =>
        {
            if (!IsArchive(n)) return false;
            var archive = Path.GetFileNameWithoutExtension(n);
            if (!Names(stem, archive)) return false;
            longer ??= allFiles.Where(BaseGameFiles.IsPlugin).Select(p => Path.GetFileNameWithoutExtension(p))
                               .Where(s => s.Length > stem.Length).ToList();
            return !longer.Any(s => Names(s, archive));
        });
    }

    /// <summary>True when an archive stem names a plugin stem: equal, or the stem then <c>" - "</c> and more.</summary>
    private static bool Names(string pluginStem, string archiveStem)
        => archiveStem.Equals(pluginStem, StringComparison.OrdinalIgnoreCase)
           || archiveStem.StartsWith(pluginStem + " - ", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The mod an archive no plugin claims is named for: the stem before the last <c>" - "</c>, so
    /// <c>Retex - Main.ba2</c> and <c>Retex - Textures.ba2</c> are one row, <c>Retex</c>.
    /// </summary>
    internal static string OwnerStem(string archiveFile)
    {
        var stem = Path.GetFileNameWithoutExtension(archiveFile);
        var dash = stem.LastIndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? stem[..dash] : stem;
    }
}
