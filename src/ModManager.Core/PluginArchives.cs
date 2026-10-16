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
///
/// <para><b>Built once per folder.</b> <see cref="Ownership.Build"/> settles every archive's owner in one pass
/// (each archive probes only the prefixes that end at a <c>" - "</c>), and every question after that is a lookup.
/// A real Fallout 4 load order is hundreds of plugins and a thousand archives, and the list is rebuilt on every
/// toggle, so asking each plugin to re-contest every archive was quadratic and measurably slow.</para>
/// </summary>
internal static class PluginArchives
{
    internal const string Ext = ".ba2";

    /// <summary>True for a Fallout 4 / Starfield archive.</summary>
    internal static bool IsArchive(string? fileName)
        => Path.GetExtension(fileName ?? "").Equals(Ext, StringComparison.OrdinalIgnoreCase);

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

    /// <summary>Who owns each archive in one folder: built once, then answered by lookup.</summary>
    internal sealed class Ownership
    {
        /// <summary>No archives in the folder, so nothing is owned.</summary>
        internal static readonly Ownership None = new(new(), new(), new());

        private readonly Dictionary<string, string> _owner;                 // archive file -> winning plugin stem
        private readonly Dictionary<string, List<string>> _byOwner;         // plugin stem -> its archives
        private readonly HashSet<string> _live;                             // stems of plugins in the folder

        private Ownership(Dictionary<string, string> owner, Dictionary<string, List<string>> byOwner, HashSet<string> live)
        { _owner = owner; _byOwner = byOwner; _live = live; }

        /// <summary>
        /// Settle every archive in <paramref name="allFiles"/>: the longest plugin stem that names it, among the
        /// live plugins and <paramref name="heldPlugins"/> (plugin filenames in holding), ties by case to the
        /// ordinal-smallest.
        /// </summary>
        internal static Ownership Build(IReadOnlyList<string> allFiles, IReadOnlyCollection<string>? heldPlugins = null)
        {
            if (!allFiles.Any(IsArchive)) return None;
            // Case-insensitive stem -> the ordinal-smallest spelling. Equal-length candidates can only differ in case.
            var stems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var live = new HashSet<string>(StringComparer.Ordinal);
            void Add(string file, bool isLive)
            {
                if (!BaseGameFiles.IsPlugin(file)) return;
                var s = Path.GetFileNameWithoutExtension(file);
                if (isLive) live.Add(s);
                if (!stems.TryGetValue(s, out var have) || string.CompareOrdinal(s, have) < 0) stems[s] = s;
            }
            foreach (var f in allFiles) Add(f, true);
            if (heldPlugins is not null) foreach (var f in heldPlugins) Add(f, false);

            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            var byOwner = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var a in allFiles)
            {
                if (!IsArchive(a)) continue;
                var w = Winner(stems, Path.GetFileNameWithoutExtension(a));
                if (w is null) continue;
                owner[a] = w;
                if (!byOwner.TryGetValue(w, out var list)) byOwner[w] = list = new List<string>();
                list.Add(a);
            }
            return new Ownership(owner, byOwner, live);
        }

        // The longest plugin stem naming the archive: the whole stem, then each prefix that ends before a " - ".
        private static string? Winner(Dictionary<string, string> stems, string archiveStem)
        {
            for (var end = archiveStem.Length; end > 0;)
            {
                if (stems.TryGetValue(archiveStem.Substring(0, end), out var s)) return s;
                end = archiveStem.LastIndexOf(" - ", end - 1, StringComparison.Ordinal);
            }
            return null;
        }

        /// <summary>The archives <paramref name="pluginFile"/> owns; empty when it is not a plugin.</summary>
        internal IEnumerable<string> OwnedBy(string pluginFile)
            => BaseGameFiles.IsPlugin(pluginFile) && _byOwner.TryGetValue(Path.GetFileNameWithoutExtension(pluginFile), out var l)
                ? l : Array.Empty<string>();

        /// <summary>The plugin stem that owns <paramref name="archive"/> (live or in holding), or null.</summary>
        internal string? OwnerOf(string archive) => _owner.TryGetValue(archive, out var w) ? w : null;

        /// <summary>True when a LIVE plugin owns <paramref name="archive"/>. False when none names it, or when the
        /// winning plugin is one in holding (the archive is then that held row's, left live by an older build).</summary>
        internal bool IsClaimed(string archive) => _owner.TryGetValue(archive, out var w) && _live.Contains(w);
    }
}
