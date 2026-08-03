using System;
using System.Text;
using System.Text.RegularExpressions;

namespace ModManager.Core.Nexus;

/// <summary>
/// Converts a Nexus mod's <c>description</c> field to plain, readable text. Nexus stores descriptions as a
/// mix of BBCode (<c>[b]</c>, <c>[url=...]</c>, <c>[*]</c>, ...) and raw HTML (<c>&lt;br /&gt;</c>, stray
/// tags, entities) in the same string — there is no single parser for that on either side, so this is a
/// deliberately narrow, pure converter rather than a full markup renderer. It exists in Core (not the UI
/// layer) specifically so the messiest part of the mod-detail feature sits behind unit tests. Read paths in
/// this codebase never throw: malformed, unclosed, or deeply nested markup degrades to best-effort text
/// instead of raising, and null/empty input yields <see cref="string.Empty"/>.
/// </summary>
public static class ModDescriptionText
{
    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // [*] starts a new bullet line. Matched before the generic BBCode stripper below because it carries no
    // closing tag and would otherwise be swallowed as an empty self-closing tag with the wrong replacement.
    private static readonly Regex ListItem = new(@"\[\*\]", Opts);

    // <br /> / <br> in any spacing/self-close form.
    private static readonly Regex BreakTag = new(@"<br\s*/?>", Opts);

    // BBCode tags: [tag], [/tag], [tag=value] (value may be quoted or bare, no brackets inside). Bounded
    // character classes only — no nested quantifiers, so this can't catastrophically backtrack.
    private static readonly Regex BbCodeTag = new(@"\[/?[a-z0-9]+(=[^\]\[]*)?\]", Opts);

    // Any remaining HTML tag, e.g. <div>, </span>, <img src="...">. Bounded to "not a > character" so it
    // can't run away across the whole document on an unclosed '<'.
    private static readonly Regex HtmlTag = new(@"<[^<>]*>", Opts);

    // 3+ consecutive line breaks (with optional surrounding whitespace on the blank lines) collapse to one
    // blank line separating the two sides.
    private static readonly Regex ExcessBlankLines = new(@"(?:[ \t]*\r?\n){3,}", Opts);

    /// <summary>Converts a raw Nexus description (BBCode + HTML mixed) to plain text for display. Never
    /// throws — null, empty, or whitespace-only input returns <see cref="string.Empty"/>, and malformed
    /// markup degrades to best-effort stripped text instead of raising.</summary>
    public static string ToPlainText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        try
        {
            var text = raw;

            text = ListItem.Replace(text, "\n");
            text = BreakTag.Replace(text, "\n");
            text = BbCodeTag.Replace(text, string.Empty);
            text = DecodeCommonEntities(text);
            text = HtmlTag.Replace(text, string.Empty);

            // Normalize line endings before collapsing so \r\n and \n runs both count.
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');

            // Trim trailing whitespace on each line (BBCode/HTML removal can leave stray spaces) without
            // disturbing intentional paragraph breaks.
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
                lines[i] = lines[i].TrimEnd();
            text = string.Join("\n", lines);

            text = ExcessBlankLines.Replace(text, "\n\n");

            return text.Trim();
        }
        catch
        {
            // Never throw on user-facing description text — worst case, show nothing rather than crash.
            return string.Empty;
        }
    }

    private static string DecodeCommonEntities(string text)
    {
        // A small fixed set rather than System.Net.WebUtility.HtmlDecode — Nexus descriptions only use a
        // handful of entities in practice, and a fixed set keeps behavior predictable and allocation-light.
        var sb = new StringBuilder(text);
        sb.Replace("&nbsp;", " ");
        sb.Replace("&amp;", "&");
        sb.Replace("&lt;", "<");
        sb.Replace("&gt;", ">");
        sb.Replace("&quot;", "\"");
        sb.Replace("&#39;", "'");
        sb.Replace("&apos;", "'");
        return sb.ToString();
    }
}
