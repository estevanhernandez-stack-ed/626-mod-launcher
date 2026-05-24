using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Pure helpers for the Unreal-pak load-order scheme: enforce order by prefixing filenames with
/// a zero-padded index (<c>0010__Name.pak</c>) so alphabetical = intended order. The prefix is
/// purely additive — stripping it restores the original name, so apply is fully reversible with
/// no rename map. <see cref="StripPrefix"/> is what keeps the prefix invisible to mod identity.
/// </summary>
public static partial class LoadOrderApply
{
    [GeneratedRegex(@"^\d{2,}__")]
    private static partial Regex PrefixRe();

    /// <summary>Remove a leading launcher load-order prefix (NNNN__), if present.</summary>
    public static string StripPrefix(string name) => PrefixRe().Replace(name, "");

    /// <summary>The prefix for a given position (step of 10 leaves room to insert).</summary>
    public static string Prefix(int index) => ((index + 1) * 10).ToString("D4") + "__";

    /// <summary>Rewrite a filename to carry the order prefix for <paramref name="index"/>.</summary>
    public static string WithOrder(string fileName, int index) => Prefix(index) + StripPrefix(fileName);
}
