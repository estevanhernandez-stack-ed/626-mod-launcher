using System.Text;

namespace ModManager.Core;

public enum MdBlockKind { Paragraph, Heading, BulletItem, NumberItem, Code }
public sealed record MdBlock(MdBlockKind Kind, int Level, IReadOnlyList<MdSpan> Spans, string Text);
public enum MdSpanKind { Text, Bold, Italic, Code, Link }
public sealed record MdSpan(MdSpanKind Kind, string Text, string? Url = null);

/// <summary>
/// Pure, render-only markdown parser for the in-house readme viewer. Produces a typed
/// block/span model a WinUI renderer maps to native controls — no HTML, no IO, no UI refs.
/// Deliberately small: a flat block list (no nesting) with flat inline spans (no nested
/// emphasis). Link URLs are captured verbatim; safety filtering is the renderer's job at
/// click time, not the parser's. Mirrors the intent of the JS readme viewer.
/// </summary>
public static class Markdown
{
    private static readonly MdSpan[] NoSpans = Array.Empty<MdSpan>();

    public static IReadOnlyList<MdBlock> Parse(string? text)
    {
        var blocks = new List<MdBlock>();
        if (string.IsNullOrWhiteSpace(text)) return blocks;

        // Normalize line endings, then walk line by line.
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var paragraph = new List<string>();
        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            var joined = string.Join(" ", paragraph);
            blocks.Add(new MdBlock(MdBlockKind.Paragraph, 0, ParseInline(joined), ""));
            paragraph.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Fenced code block: opens on a line whose trimmed text starts with ```.
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var code = new List<string>();
                i++; // consume the opening fence
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Add(lines[i]);
                    i++;
                }
                // i now points at the closing fence (consumed by the loop step) or past end.
                blocks.Add(new MdBlock(MdBlockKind.Code, 0, NoSpans, string.Join("\n", code)));
                continue;
            }

            // Blank line ends any open paragraph; it is not its own block.
            if (line.Trim().Length == 0)
            {
                FlushParagraph();
                continue;
            }

            // Heading: 1-6 '#' then a space.
            if (TryHeading(line, out int level, out string headingRest))
            {
                FlushParagraph();
                blocks.Add(new MdBlock(MdBlockKind.Heading, level, ParseInline(headingRest), ""));
                continue;
            }

            // Bullet item: '- ', '* ', or '+ '.
            if (TryBullet(line, out string bulletRest))
            {
                FlushParagraph();
                blocks.Add(new MdBlock(MdBlockKind.BulletItem, 0, ParseInline(bulletRest), ""));
                continue;
            }

            // Numbered item: one-or-more digits, a dot, then whitespace.
            if (TryNumber(line, out string numberRest))
            {
                FlushParagraph();
                blocks.Add(new MdBlock(MdBlockKind.NumberItem, 0, ParseInline(numberRest), ""));
                continue;
            }

            // Plain line: accumulate into the current paragraph (coalesce wrapped lines).
            paragraph.Add(line.Trim());
        }

        FlushParagraph();
        return blocks;
    }

    private static bool TryHeading(string line, out int level, out string rest)
    {
        level = 0;
        rest = "";
        int hashes = 0;
        while (hashes < line.Length && line[hashes] == '#') hashes++;
        if (hashes < 1 || hashes > 6) return false;
        if (hashes >= line.Length || line[hashes] != ' ') return false;
        level = hashes;
        rest = line[(hashes + 1)..].Trim();
        return true;
    }

    private static bool TryBullet(string line, out string rest)
    {
        rest = "";
        if (line.Length < 2) return false;
        char c = line[0];
        if ((c == '-' || c == '*' || c == '+') && line[1] == ' ')
        {
            rest = line[2..].Trim();
            return true;
        }
        return false;
    }

    private static bool TryNumber(string line, out string rest)
    {
        rest = "";
        int n = 0;
        while (n < line.Length && char.IsDigit(line[n])) n++;
        if (n == 0) return false;                       // need at least one digit
        if (n >= line.Length || line[n] != '.') return false; // need the dot
        int after = n + 1;
        if (after >= line.Length || !char.IsWhiteSpace(line[after])) return false; // need whitespace
        rest = line[after..].Trim();
        return true;
    }

    /// <summary>
    /// Flat left-to-right inline scan. Emits a leading Text span for any run before the next
    /// marker, then the marked span. No nested emphasis (v1). Unmatched/dangling markers are
    /// literal text. Inline code and link inner content are verbatim (no nested parsing).
    /// </summary>
    private static IReadOnlyList<MdSpan> ParseInline(string text)
    {
        var spans = new List<MdSpan>();
        if (text.Length == 0) return spans;

        var pending = new StringBuilder(); // accumulates literal text awaiting a marker boundary
        void FlushPending()
        {
            if (pending.Length == 0) return;
            spans.Add(new MdSpan(MdSpanKind.Text, pending.ToString()));
            pending.Clear();
        }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            // Inline code: `...`
            if (c == '`')
            {
                int close = text.IndexOf('`', i + 1);
                if (close > i)
                {
                    FlushPending();
                    spans.Add(new MdSpan(MdSpanKind.Code, text[(i + 1)..close]));
                    i = close + 1;
                    continue;
                }
            }

            // Link: [label](url)
            if (c == '[')
            {
                int labelEnd = text.IndexOf(']', i + 1);
                if (labelEnd > i && labelEnd + 1 < text.Length && text[labelEnd + 1] == '(')
                {
                    int urlEnd = FindLinkUrlEnd(text, labelEnd + 2);
                    if (urlEnd > labelEnd + 1)
                    {
                        FlushPending();
                        var label = text[(i + 1)..labelEnd];
                        var url = text[(labelEnd + 2)..urlEnd];
                        spans.Add(new MdSpan(MdSpanKind.Link, label, url));
                        i = urlEnd + 1;
                        continue;
                    }
                }
            }

            // Bold: ** or __
            if (TryEmphasis(text, i, "**", out int boldEnd))
            {
                FlushPending();
                spans.Add(new MdSpan(MdSpanKind.Bold, text[(i + 2)..boldEnd]));
                i = boldEnd + 2;
                continue;
            }
            if (TryEmphasis(text, i, "__", out int boldEnd2))
            {
                FlushPending();
                spans.Add(new MdSpan(MdSpanKind.Bold, text[(i + 2)..boldEnd2]));
                i = boldEnd2 + 2;
                continue;
            }

            // Italic: * or _
            if (c == '*' && TryEmphasis(text, i, "*", out int itEnd))
            {
                FlushPending();
                spans.Add(new MdSpan(MdSpanKind.Italic, text[(i + 1)..itEnd]));
                i = itEnd + 1;
                continue;
            }
            if (c == '_' && TryEmphasis(text, i, "_", out int itEnd2))
            {
                FlushPending();
                spans.Add(new MdSpan(MdSpanKind.Italic, text[(i + 1)..itEnd2]));
                i = itEnd2 + 1;
                continue;
            }

            // No marker matched at i: literal character.
            pending.Append(c);
            i++;
        }

        FlushPending();
        return spans;
    }

    // Finds the closing ')' for a link URL, balancing nested parens so a url like
    // javascript:alert(1) is captured whole and verbatim. Returns the index of the
    // matching ')', or -1 if unbalanced (treated as not-a-link by the caller).
    private static int FindLinkUrlEnd(string text, int start)
    {
        int depth = 0;
        for (int j = start; j < text.Length; j++)
        {
            char c = text[j];
            if (c == '(') depth++;
            else if (c == ')')
            {
                if (depth == 0) return j;
                depth--;
            }
        }
        return -1;
    }

    // Matches an emphasis run delimited by `marker` starting at `start`. Requires a closing
    // marker and non-empty inner content. Returns the index of the closing marker's first
    // char. A dangling marker (no close) returns false → literal text.
    private static bool TryEmphasis(string text, int start, string marker, out int closeIndex)
    {
        closeIndex = -1;
        int openEnd = start + marker.Length;
        if (openEnd > text.Length) return false;
        if (!text.AsSpan(start, marker.Length).SequenceEqual(marker)) return false;

        int search = openEnd;
        while (search <= text.Length - marker.Length)
        {
            if (text.AsSpan(search, marker.Length).SequenceEqual(marker))
            {
                if (search > openEnd) // non-empty inner content
                {
                    closeIndex = search;
                    return true;
                }
                return false; // empty run like ** ** with no content -> literal
            }
            search++;
        }
        return false;
    }
}
