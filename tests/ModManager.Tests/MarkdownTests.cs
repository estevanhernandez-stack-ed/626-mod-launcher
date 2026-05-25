using ModManager.Core;

namespace ModManager.Tests;

// TDD for the render-only readme markdown parser. Produces a typed block/span model
// a WinUI renderer maps to native controls. Pure: no UI, no IO. The parser captures
// link URLs verbatim — link-safety is the renderer's job at click time, not the parser's.
public class MarkdownTests
{
    // ---- block: headings ----

    [Fact]
    public void Heading_captures_level_and_inline_spans()
    {
        var blocks = Markdown.Parse("## Hello world");
        var b = Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Heading, b.Kind);
        Assert.Equal(2, b.Level);
        Assert.Equal("", b.Text);
        var span = Assert.Single(b.Spans);
        Assert.Equal(MdSpanKind.Text, span.Kind);
        Assert.Equal("Hello world", span.Text);
    }

    [Fact]
    public void Heading_supports_levels_one_through_six()
    {
        Assert.Equal(1, Assert.Single(Markdown.Parse("# A")).Level);
        Assert.Equal(6, Assert.Single(Markdown.Parse("###### F")).Level);
    }

    [Fact]
    public void Seven_hashes_is_not_a_heading()
    {
        var b = Assert.Single(Markdown.Parse("####### nope"));
        Assert.Equal(MdBlockKind.Paragraph, b.Kind);
    }

    // ---- inline: emphasis + code + link ----

    [Fact]
    public void Bold_span_with_double_star()
    {
        var b = Assert.Single(Markdown.Parse("**bold**"));
        var span = Assert.Single(b.Spans);
        Assert.Equal(MdSpanKind.Bold, span.Kind);
        Assert.Equal("bold", span.Text);
    }

    [Fact]
    public void Bold_span_with_double_underscore()
    {
        var span = Assert.Single(Assert.Single(Markdown.Parse("__bold__")).Spans);
        Assert.Equal(MdSpanKind.Bold, span.Kind);
        Assert.Equal("bold", span.Text);
    }

    [Fact]
    public void Italic_span_with_single_star()
    {
        var span = Assert.Single(Assert.Single(Markdown.Parse("*it*")).Spans);
        Assert.Equal(MdSpanKind.Italic, span.Kind);
        Assert.Equal("it", span.Text);
    }

    [Fact]
    public void Italic_span_with_single_underscore()
    {
        var span = Assert.Single(Assert.Single(Markdown.Parse("_it_")).Spans);
        Assert.Equal(MdSpanKind.Italic, span.Kind);
        Assert.Equal("it", span.Text);
    }

    [Fact]
    public void Inline_code_is_verbatim_with_no_nested_parsing()
    {
        var span = Assert.Single(Assert.Single(Markdown.Parse("`a **b** c`")).Spans);
        Assert.Equal(MdSpanKind.Code, span.Kind);
        Assert.Equal("a **b** c", span.Text);
        Assert.Null(span.Url);
    }

    [Fact]
    public void Link_captures_label_and_url_verbatim()
    {
        var span = Assert.Single(Assert.Single(Markdown.Parse("[docs](https://example.com/x)")).Spans);
        Assert.Equal(MdSpanKind.Link, span.Kind);
        Assert.Equal("docs", span.Text);
        Assert.Equal("https://example.com/x", span.Url);
    }

    [Fact]
    public void Link_with_javascript_url_is_still_captured_parser_does_not_filter()
    {
        // Proves the parser is render-only and does NOT do safety filtering —
        // that is the renderer's job at click time.
        var span = Assert.Single(Assert.Single(Markdown.Parse("[click](javascript:alert(1))")).Spans);
        Assert.Equal(MdSpanKind.Link, span.Kind);
        Assert.Equal("click", span.Text);
        Assert.Equal("javascript:alert(1)", span.Url);
    }

    [Fact]
    public void Lone_star_is_literal_text()
    {
        var span = Assert.Single(Assert.Single(Markdown.Parse("a * b")).Spans);
        Assert.Equal(MdSpanKind.Text, span.Kind);
        Assert.Equal("a * b", span.Text);
    }

    [Fact]
    public void Leading_text_then_link_then_trailing_text_emits_three_spans_in_order()
    {
        var spans = Assert.Single(Markdown.Parse("see [docs](https://x.io) now")).Spans;
        Assert.Equal(3, spans.Count);
        Assert.Equal(MdSpanKind.Text, spans[0].Kind);
        Assert.Equal("see ", spans[0].Text);
        Assert.Equal(MdSpanKind.Link, spans[1].Kind);
        Assert.Equal("docs", spans[1].Text);
        Assert.Equal("https://x.io", spans[1].Url);
        Assert.Equal(MdSpanKind.Text, spans[2].Kind);
        Assert.Equal(" now", spans[2].Text);
    }

    // ---- block: lists ----

    [Fact]
    public void Bullet_list_yields_one_block_per_item()
    {
        var blocks = Markdown.Parse("- one\n- two\n* three\n+ four");
        Assert.Equal(4, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(MdBlockKind.BulletItem, b.Kind));
        Assert.Equal("one", Assert.Single(blocks[0].Spans).Text);
        Assert.Equal("four", Assert.Single(blocks[3].Spans).Text);
    }

    [Fact]
    public void Numbered_list_yields_number_item_blocks()
    {
        var blocks = Markdown.Parse("1. first\n2. second");
        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(MdBlockKind.NumberItem, b.Kind));
        Assert.Equal("first", Assert.Single(blocks[0].Spans).Text);
        Assert.Equal("second", Assert.Single(blocks[1].Spans).Text);
    }

    // ---- block: fenced code ----

    [Fact]
    public void Fenced_code_preserves_content_with_no_inline_parsing()
    {
        var blocks = Markdown.Parse("```\nline **1**\nline 2\n```");
        var b = Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Code, b.Kind);
        Assert.Equal("line **1**\nline 2", b.Text);
        Assert.Empty(b.Spans);
        Assert.Equal(0, b.Level);
    }

    [Fact]
    public void Fenced_code_with_language_tag_is_still_a_code_block()
    {
        var b = Assert.Single(Markdown.Parse("```csharp\nvar x = 1;\n```"));
        Assert.Equal(MdBlockKind.Code, b.Kind);
        Assert.Equal("var x = 1;", b.Text);
    }

    [Fact]
    public void Unterminated_fence_runs_to_end_of_input()
    {
        var b = Assert.Single(Markdown.Parse("```\nstill code\nmore code"));
        Assert.Equal(MdBlockKind.Code, b.Kind);
        Assert.Equal("still code\nmore code", b.Text);
    }

    // ---- block: paragraphs ----

    [Fact]
    public void Two_wrapped_plain_lines_coalesce_into_one_paragraph()
    {
        var blocks = Markdown.Parse("first line\nsecond line");
        var b = Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, b.Kind);
        Assert.Equal("first line second line", Assert.Single(b.Spans).Text);
    }

    [Fact]
    public void Blank_line_splits_two_paragraphs()
    {
        var blocks = Markdown.Parse("para one\n\npara two");
        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(MdBlockKind.Paragraph, b.Kind));
        Assert.Equal("para one", Assert.Single(blocks[0].Spans).Text);
        Assert.Equal("para two", Assert.Single(blocks[1].Spans).Text);
    }

    // ---- empty input ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_empty_or_whitespace_input_yields_empty_list(string? input)
    {
        Assert.Empty(Markdown.Parse(input));
    }
}
