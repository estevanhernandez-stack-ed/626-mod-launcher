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

    // Exactly what Prefix writes: a multiple of ten, zero-padded to four digits (never 0000), or five or
    // more digits with no padding once past the 999th mod. The undo plan
    // keys on THIS, not PrefixRe, so a mod an author named "10__thing.pak" is never renamed by an undo.
    // PrefixRe stays loose because it feeds mod identity (Scanner.ModKey): tightening it would re-key
    // every existing file whose name starts with 2-3 digits and "__", and with it their metadata and
    // disabled-holding folders.
    [GeneratedRegex(@"^(?:(?!0000)\d{3}0|[1-9]\d{3,}0)__")]
    private static partial Regex OwnPrefixRe();

    /// <summary>Remove a leading launcher load-order prefix (NNNN__), if present.</summary>
    public static string StripPrefix(string name) => PrefixRe().Replace(name, "");

    /// <summary>True when <paramref name="name"/> starts with a prefix <see cref="Prefix"/> could have written.</summary>
    public static bool HasOwnPrefix(string name) => OwnPrefixRe().IsMatch(name);

    /// <summary>Remove a prefix <see cref="Prefix"/> could have written, and nothing else.</summary>
    public static string StripOwnPrefix(string name) => OwnPrefixRe().Replace(name, "");

    /// <summary>The prefix for a given position (step of 10 leaves room to insert).</summary>
    public static string Prefix(int index) => ((index + 1) * 10).ToString("D4") + "__";

    /// <summary>Rewrite a filename to carry the order prefix for <paramref name="index"/>.</summary>
    public static string WithOrder(string fileName, int index) => Prefix(index) + StripPrefix(fileName);
}
