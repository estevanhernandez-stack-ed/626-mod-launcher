namespace ModManager.Core;

/// <summary>A group of mods that are variants of one logical mod — same identified mod page, or
/// the same _Nx variant base. One member = a singleton.</summary>
public sealed record VariantFamily(string Key, string Title, IReadOnlyList<Mod> Members)
{
    public bool IsMulti => Members.Count > 1;
}

public static class VariantGroups
{
    /// <summary>Group mods into families, preserving first-appearance order. Family key:
    /// 1. if ModUrl is set -> "url:" + normalized ModUrl (same mod page = same family);
    /// 2. else -> "base:" + Variant.ParseVariant(Name).Base (lowercased) (the _Nx family);
    /// Title = the first member's DisplayName if non-empty, else its Name.</summary>
    public static IReadOnlyList<VariantFamily> Group(IEnumerable<Mod> mods)
    {
        var order = new List<string>();
        var byKey = new Dictionary<string, List<Mod>>(StringComparer.Ordinal);

        foreach (var mod in mods)
        {
            var key = KeyFor(mod);
            if (!byKey.TryGetValue(key, out var list))
            {
                list = new List<Mod>();
                byKey[key] = list;
                order.Add(key);
            }
            list.Add(mod);
        }

        return order
            .Select(key => new VariantFamily(key, Display(byKey[key][0]), byKey[key]))
            .ToList();
    }

    private static string KeyFor(Mod mod)
    {
        if (!string.IsNullOrEmpty(mod.ModUrl))
        {
            var url = mod.ModUrl.Trim().ToLowerInvariant().TrimEnd('/');
            return "url:" + url;
        }
        return "base:" + Variant.ParseVariant(mod.Name).Base.ToLowerInvariant();
    }

    private static string Display(Mod mod)
        => string.IsNullOrEmpty(mod.DisplayName) ? mod.Name : mod.DisplayName;
}
