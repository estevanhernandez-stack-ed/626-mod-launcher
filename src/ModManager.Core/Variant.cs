using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Pure variant parsing for multi-option mods. A "variant" is a trailing multiplier token
/// like _2x, _3x, _10x, _6h (and stacked forms like _6h_2x). Stripping all trailing variant
/// tokens yields the family base; mods sharing a base are options of one logical mod.
/// Mirrors variant-core.js.
/// </summary>
public static partial class Variant
{
    [GeneratedRegex(@"_(\d+(?:x|h))$", RegexOptions.IgnoreCase)]
    private static partial Regex VariantRe();

    public sealed record Parsed(string Base, string? Tag);

    public sealed record Family<T>(string Base, IReadOnlyList<Member<T>> Members);

    public sealed record Member<T>(T Mod, string Base, string? Tag);

    public static Parsed ParseVariant(string name)
    {
        var baseName = name;
        var parts = new List<string>();
        while (true)
        {
            var m = VariantRe().Match(baseName);
            if (!m.Success) break;
            parts.Insert(0, m.Groups[1].Value.ToLowerInvariant());
            baseName = baseName[..m.Index];
        }
        return new Parsed(baseName, parts.Count > 0 ? string.Join("_", parts) : null);
    }

    /// <summary>
    /// Group mods into families keyed by base, in original first-appearance order.
    /// </summary>
    public static IReadOnlyList<Family<T>> GroupFamilies<T>(IEnumerable<T> mods, Func<T, string> name)
    {
        var order = new List<string>();
        var byBase = new Dictionary<string, List<Member<T>>>();
        foreach (var m in mods)
        {
            var (b, tag) = ParseVariant(name(m));
            if (!byBase.TryGetValue(b, out var list))
            {
                list = new List<Member<T>>();
                byBase[b] = list;
                order.Add(b);
            }
            list.Add(new Member<T>(m, b, tag));
        }
        return order.Select(b => new Family<T>(b, byBase[b])).ToList();
    }
}
