namespace ModManager.Core;

/// <summary>
/// Sidecar files that can also stand ALONE as a mod: Cyberpunk's ArchiveXL <c>.xl</c>, and a Fallout 4 /
/// Starfield <c>.ba2</c> archive whose plugin is absent.
///
/// <para><see cref="Scanner.SidecarsFor"/> pairs <c>Foo.archive.xl</c> with <c>Foo.archive</c> when both
/// exist. But an <c>.xl</c> is a config ArchiveXL reads on its own, and mods ship it without an archive:
/// plain <c>QuestTriggerFixes.xl</c> (it edits what other mods and the game already have), or
/// <c>Cronos.archive.xl</c> whose archive lives elsewhere or never existed. The game declares only
/// <c>archive</c>, so those matched nothing and were never listed — and a file the launcher cannot list is
/// a file it cannot disable, enable or uninstall.</para>
///
/// <para>The rule is keyed on the game's declared mod extension, not on a game id or a stored registration:
/// a registration that froze <c>["archive"]</c> keeps working without an edit to its <c>games.json</c>, and
/// the remote game-definition feed cannot revert it. (Adding <c>xl</c> to the manifest's
/// <c>fileExtensions</c> would not reach such a registration — <see cref="RegistrationRefresh.Extensions"/>
/// only takes the manifest's list over an untouched preset default — and would make a paired
/// <c>Foo.archive.xl</c> list as a second row.)</para>
///
/// <para>Deliberately not part of <see cref="Scanner.SidecarsFor"/>: pairing decides what rides WITH a mod
/// file; this decides which unclaimed files are mods in their own right.</para>
/// </summary>
internal static class StandaloneSidecars
{
    /// <summary>declared mod extension -> sidecar extensions that may stand alone beside it.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> ByModExt =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["archive"] = new[] { "xl" },
            // Fallout 4 / Starfield: <Plugin> - Main.ba2 rides with <Plugin>.esp (PluginArchives); one no plugin
            // claims (a texture-only mod, a plugin deleted by hand) is a row of its own. Skyrim's .bsa is declared.
            ["esp"] = new[] { "ba2" }, ["esm"] = new[] { "ba2" }, ["esl"] = new[] { "ba2" },
        };

    /// <summary>The standalone-capable extensions for a game scanning <paramref name="declaredExts"/>
    /// (lowercase, dot-less). Empty for a game whose extensions carry none.</summary>
    internal static IReadOnlyList<string> ExtsFor(IEnumerable<string> declaredExts)
        => declaredExts.SelectMany(e => ByModExt.TryGetValue(e, out var xs) ? xs : Array.Empty<string>())
                       .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// The files in <paramref name="allFiles"/> that are standalone mods: they wear a standalone-capable
    /// extension, are not themselves a mod file, and no mod file in <paramref name="modFiles"/> claims them
    /// as a sidecar. <paramref name="modFiles"/> is every file the game's extensions match in the folder
    /// (base-game files included), so a file stays claimed even when its owner is filtered out of the listing.
    /// </summary>
    internal static IEnumerable<string> Find(
        IReadOnlyList<string> allFiles, IReadOnlyList<string> modFiles, GameContext c,
        PluginArchives.Ownership? ownership = null)
    {
        var exts = ExtsFor(c.Exts);
        if (exts.Count == 0) yield break;
        PluginArchives.Ownership? own = ownership;
        foreach (var n in allFiles)
        {
            var ext = Path.GetExtension(n).TrimStart('.');
            if (!exts.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
            if (c.FileRe.IsMatch(n)) continue;
            // An archive's owner is decided over the whole folder and the held plugins (PluginArchives), the same
            // contest the plugin's row ran, so the two can never disagree about who has it.
            if (PluginArchives.IsArchive(n))
            {
                own ??= PluginArchives.Ownership.Build(allFiles);
                if (own.IsClaimed(n)) continue;
                yield return n;
                continue;
            }
            var one = new[] { n };
            if (modFiles.Any(m => Scanner.SidecarsFor(m, one).Any())) continue;
            yield return n;
        }
    }

    /// <summary>
    /// The row name for a standalone file: the filename without its extension, without a load-order prefix,
    /// and without the mod extension an <c>X.archive.xl</c> spells in the middle — so an orphan
    /// <c>Cronos.archive.xl</c> is the row <c>Cronos</c>, the name its archive would give it if one arrives.
    /// </summary>
    internal static string KeyFor(string file, GameContext c, PluginArchives.Ownership? ownership = null)
    {
        // An archive no plugin claims is named for its owner, so Retex - Main.ba2 and Retex - Textures.ba2 are one row.
        // When a plugin in holding owns it, it is that plugin's row (so MyMod - Plus - Extra - Main.ba2 hides behind
        // the held "MyMod - Plus"); with no owner at all it is named for the stem before its last " - ".
        if (PluginArchives.IsArchive(file))
            return Scanner.GroupKey(ownership?.OwnerOf(file) ?? PluginArchives.OwnerStem(file), c);
        var stem = Path.GetFileNameWithoutExtension(file);
        foreach (var e in c.Exts)
            if (stem.EndsWith("." + e, StringComparison.OrdinalIgnoreCase)) { stem = stem[..^(e.Length + 1)]; break; }
        return Scanner.GroupKey(stem, c);
    }
}
