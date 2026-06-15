using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Pure mod-metadata helpers. A per-game metadata map keys a mod's BASE name to a real
/// title/description/author/links. When absent, the display name is auto-prettified from
/// the filename. Curated entries win. Mirrors metadata-core.js.
/// </summary>
public static partial class Metadata
{
    [GeneratedRegex(@"[_-]+")]
    private static partial Regex SepRe();

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex CamelRe();

    [GeneratedRegex(@"([A-Za-z])(\d)")]
    private static partial Regex LetterDigitRe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WsRe();

    [GeneratedRegex(@"\b\w")]
    private static partial Regex WordStartRe();

    public static string Prettify(string? name)
    {
        var original = name ?? "";
        var s = SepRe().Replace(original, " ");
        s = CamelRe().Replace(s, "$1 $2");
        s = LetterDigitRe().Replace(s, "$1 $2");
        s = WsRe().Replace(s, " ").Trim();
        s = WordStartRe().Replace(s, m => m.Value.ToUpperInvariant());
        return s.Length > 0 ? s : original;
    }

    /// <summary>
    /// Attach display/credit/link fields to each mod. metaMap is keyed by base (preferred)
    /// or full mod name. Variant suffix is appended to the display title. Mutates and returns.
    /// </summary>
    public static IReadOnlyList<Mod> MergeMetadata(IEnumerable<Mod> mods, IReadOnlyDictionary<string, ModMeta>? metaMap)
    {
        metaMap ??= new Dictionary<string, ModMeta>();
        var list = new List<Mod>();
        foreach (var m in mods)
        {
            ModMeta? e = null;
            if (!string.IsNullOrEmpty(m.Base) && metaMap.TryGetValue(m.Base, out var byBase)) e = byBase;
            else if (metaMap.TryGetValue(m.Name, out var byName)) e = byName;

            var baseSource = string.IsNullOrEmpty(m.Base) ? m.Name : m.Base;
            var baseTitle = !string.IsNullOrEmpty(e?.Title) ? e!.Title! : Prettify(baseSource);
            m.BaseTitle = baseTitle;
            m.DisplayName = !string.IsNullOrEmpty(m.Variant) ? $"{baseTitle} ({m.Variant})" : baseTitle;
            m.Description = e?.Description;
            m.Author = e?.Author;
            m.AuthorUrl = e?.AuthorUrl;
            m.ModUrl = e?.Url;
            m.Source = e?.Source;
            m.Donate = e?.Donate;
            m.Image = e?.Image;
            m.Downloads = e?.Downloads;
            m.EndorsementCount = e?.EndorsementCount;
            m.Available = e?.Available;
            m.Category = e?.Category;
            m.HasMeta = e is not null;
            if (e is null && m.Builtin && Ue4ssBuiltins.Lookup(m.Name) is { } b)
            {
                m.BaseTitle = b.Title;
                m.DisplayName = !string.IsNullOrEmpty(m.Variant) ? $"{b.Title} ({m.Variant})" : b.Title;
                m.Description = b.Description;
                m.Source = b.DocsUrl;   // shows as the source/docs link
                m.HasMeta = true;        // so the credit line (with the docs link) renders
            }
            list.Add(m);
        }
        return list;
    }
}
