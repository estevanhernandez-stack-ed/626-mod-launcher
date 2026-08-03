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
    public void Escaped_angle_brackets_are_kept_not_treated_as_markup()
    {
        // Was a documented limitation until the pipeline was reordered: decoding entities BEFORE stripping
        // tags turned an escaped "&lt; 2 &gt;" into a literal "< 2 >" that the tag regex then ate, so
        // "requires version &lt; 2.0 &gt; only" silently lost its middle. Tags are now stripped first, so
        // only genuine markup is removed and escaped text survives intact.
        Assert.Equal("1 < 2 > 0", ModDescriptionText.ToPlainText("1 &lt; 2 &gt; 0"));
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

    [Fact]
    public void Escaped_angle_brackets_survive_tag_stripping()
    {
        // Regression: entities used to be decoded BEFORE tags were stripped, so an escaped comparison
        // decoded into "< 2.0 >" and was then eaten as if it were markup — the description silently lost
        // its middle. Real markup must still go; escaped text must stay.
        var result = ModDescriptionText.ToPlainText("requires version &lt; 2.0 &gt; only");
        Assert.Equal("requires version < 2.0 > only", result);
    }

    [Fact]
    public void Real_markup_is_still_stripped_after_the_reorder()
    {
        // The reorder must not weaken tag stripping — genuine HTML still goes.
        Assert.Equal("bold and plain", ModDescriptionText.ToPlainText("<b>bold</b> and <i>plain</i>"));
    }
}
