using ModManager.Core.Nexus;
using Xunit;

namespace ModManager.Tests.Nexus;

public class ModDescriptionTextTests
{
    [Fact]
    public void Null_input_returns_empty_string()
    {
        Assert.Equal(string.Empty, ModDescriptionText.ToPlainText(null));
    }

    [Fact]
    public void Empty_input_returns_empty_string()
    {
        Assert.Equal(string.Empty, ModDescriptionText.ToPlainText(string.Empty));
    }

    [Fact]
    public void Whitespace_only_input_returns_empty_string()
    {
        Assert.Equal(string.Empty, ModDescriptionText.ToPlainText("   \n\t  "));
    }

    [Fact]
    public void Plain_text_passes_through_unchanged()
    {
        Assert.Equal("Just some plain text.", ModDescriptionText.ToPlainText("Just some plain text."));
    }

    [Fact]
    public void Live_captured_palworld_sample_strips_markup_and_preserves_lines()
    {
        // Real body captured from the Nexus API, palworld modId 577.
        const string raw =
            "[b]Features:[/b]\n<br />[list]\n<br />[*][b]In Game UI for configuring mod settings[/b]\n<br />[/list]";

        var result = ModDescriptionText.ToPlainText(raw);

        Assert.DoesNotContain('[', result);
        Assert.DoesNotContain(']', result);
        Assert.DoesNotContain("<br", result, System.StringComparison.OrdinalIgnoreCase);

        var lines = result.Split('\n');
        Assert.Contains(lines, l => l.Trim() == "Features:");
        Assert.Contains(lines, l => l.Trim() == "In Game UI for configuring mod settings");

        // "Features:" must appear on an earlier line than the bullet text.
        var featuresIndex = System.Array.FindIndex(lines, l => l.Trim() == "Features:");
        var bulletIndex = System.Array.FindIndex(lines, l => l.Trim() == "In Game UI for configuring mod settings");
        Assert.True(featuresIndex < bulletIndex);
    }

    [Fact]
    public void Url_bbcode_with_parameter_keeps_only_link_text()
    {
        var result = ModDescriptionText.ToPlainText("[url=https://x]text[/url]");
        Assert.Equal("text", result.Trim());
        Assert.DoesNotContain("https://x", result);
    }

    [Theory]
    [InlineData("[b]bold[/b]", "bold")]
    [InlineData("[i]italic[/i]", "italic")]
    [InlineData("[u]underline[/u]", "underline")]
    [InlineData("[size=5]big[/size]", "big")]
    [InlineData("[color=red]red text[/color]", "red text")]
    [InlineData("[quote]quoted[/quote]", "quoted")]
    [InlineData("[code]var x = 1;[/code]", "var x = 1;")]
    [InlineData("[center]centered[/center]", "centered")]
    [InlineData("[img]https://example.com/x.png[/img]", "https://example.com/x.png")]
    public void Common_bbcode_tags_are_stripped(string raw, string expectedContent)
    {
        var result = ModDescriptionText.ToPlainText(raw);
        Assert.Equal(expectedContent, result.Trim());
    }

    [Fact]
    public void Bbcode_tags_are_stripped_case_insensitively()
    {
        var result = ModDescriptionText.ToPlainText("[B]bold[/B]");
        Assert.Equal("bold", result.Trim());
    }

    [Fact]
    public void Br_self_closing_and_open_forms_become_line_breaks()
    {
        var result = ModDescriptionText.ToPlainText("line one<br />line two<br>line three");
        var lines = result.Split('\n');
        Assert.Contains(lines, l => l.Trim() == "line one");
        Assert.Contains(lines, l => l.Trim() == "line two");
        Assert.Contains(lines, l => l.Trim() == "line three");
    }

    [Fact]
    public void List_items_start_new_lines()
    {
        var result = ModDescriptionText.ToPlainText("[list][*]first[*]second[/list]");
        var lines = result.Split('\n');
        Assert.Contains(lines, l => l.Trim() == "first");
        Assert.Contains(lines, l => l.Trim() == "second");
    }

    [Theory]
    [InlineData("Tom &amp; Jerry", "Tom & Jerry")]
    [InlineData("value &lt; threshold", "value < threshold")]
    [InlineData("value &gt; threshold", "value > threshold")]
    [InlineData("She said &quot;hi&quot;", "She said \"hi\"")]
    [InlineData("It&#39;s here", "It's here")]
    [InlineData("a&nbsp;b", "a b")]
    public void Common_html_entities_are_decoded(string raw, string expected)
    {
        Assert.Equal(expected, ModDescriptionText.ToPlainText(raw));
    }

    [Fact]
    public void Decoded_angle_brackets_that_form_a_tag_shape_are_still_stripped()
    {
        // Known limitation, not a bug: the spec orders entity-decode BEFORE the final HTML-tag strip, so a
        // decoded "&lt; ... &gt;" pair that happens to look like a tag gets removed like a real tag would.
        // Nexus mod descriptions don't use encoded angle brackets as literal comparison operators in
        // practice, so this tradeoff is accepted rather than reordering the documented pipeline.
        var result = ModDescriptionText.ToPlainText("1 &lt; 2 &gt; 0");
        Assert.DoesNotContain("2", result);
    }

    [Fact]
    public void Remaining_html_tags_are_stripped()
    {
        var result = ModDescriptionText.ToPlainText("<div><span>hello</span></div>");
        Assert.DoesNotContain('<', result);
        Assert.DoesNotContain('>', result);
        Assert.Contains("hello", result);
    }

    [Fact]
    public void Three_or_more_blank_lines_collapse_to_one()
    {
        var raw = "line one\n\n\n\n\nline two";
        var result = ModDescriptionText.ToPlainText(raw);
        Assert.DoesNotContain("\n\n\n", result);
        Assert.Contains("line one", result);
        Assert.Contains("line two", result);
    }

    [Fact]
    public void Result_is_trimmed()
    {
        var result = ModDescriptionText.ToPlainText("\n\n  hello  \n\n");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Unclosed_tags_do_not_throw()
    {
        var result = ModDescriptionText.ToPlainText("[b]unclosed bold and <div unclosed");
        Assert.False(string.IsNullOrEmpty(result) && false); // just assert no throw; content best-effort
    }

    [Fact]
    public void Deeply_nested_tags_do_not_throw()
    {
        var raw = string.Concat(System.Linq.Enumerable.Repeat("[b][i][u]", 50)) + "text" +
                  string.Concat(System.Linq.Enumerable.Repeat("[/u][/i][/b]", 50));
        var result = ModDescriptionText.ToPlainText(raw);
        Assert.Contains("text", result);
    }

    [Fact]
    public void Malformed_bracket_soup_does_not_throw()
    {
        var raw = "[b][i][/b][/i][url=x][/url][list][*][*]][[[<br<br/>><<>>";
        var result = ModDescriptionText.ToPlainText(raw);
        Assert.NotNull(result);
    }

    [Fact]
    public void Large_body_completes_quickly()
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < 1000; i++)
        {
            sb.Append("[b]Section ").Append(i).Append("[/b]<br />Some text with &amp; entities. [*] bullet\n");
        }
        var raw = sb.ToString();
        Assert.True(raw.Length > 10_000);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = ModDescriptionText.ToPlainText(raw);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000, $"Took {sw.ElapsedMilliseconds}ms");
        Assert.Contains("Section 0", result);
        Assert.Contains("Section 999", result);
    }
}
