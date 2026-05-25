using ModManager.Core;

namespace ModManager.Tests;

// INI/config parse: section + key=value, '#' and ';' comments, each key's immediately-preceding
// contiguous comment block becomes its Description. Blank lines and section headers reset the block.
public class ModConfigTests
{
    [Fact]
    public void Parse_reads_key_value_pairs()
    {
        var entries = ModConfig.Parse("pet_name = Truffle\nenable_rename = true\n");
        Assert.Equal(2, entries.Count);
        Assert.Equal("pet_name", entries[0].Key);
        Assert.Equal("Truffle", entries[0].Value);
        Assert.Equal("true", entries[1].Value);
        Assert.Null(entries[0].Section);
    }

    [Fact]
    public void Parse_tracks_sections()
    {
        var entries = ModConfig.Parse("[Overrides]\nModsFolderPath =\n[General]\nEnableHotReloadSystem = 0\n");
        Assert.Equal("Overrides", entries[0].Section);
        Assert.Equal("ModsFolderPath", entries[0].Key);
        Assert.Equal("", entries[0].Value);
        Assert.Equal("General", entries[1].Section);
    }

    [Fact]
    public void Parse_attaches_preceding_comment_block_as_description()
    {
        var entries = ModConfig.Parse("# What name to display.\n# Keep it short.\npet_name = Truffle\n");
        Assert.Single(entries);
        Assert.Equal("What name to display. Keep it short.", entries[0].Description);
    }

    [Fact]
    public void Parse_blank_line_resets_the_comment_block()
    {
        var entries = ModConfig.Parse("# Banner divider\n\n# Real description\nfoo = bar\n");
        Assert.Equal("Real description", entries[0].Description); // banner dropped after the blank
    }

    [Fact]
    public void Parse_supports_semicolon_comments()
    {
        var entries = ModConfig.Parse("; a setting\nKey = 1\n");
        Assert.Equal("a setting", entries[0].Description);
    }

    [Fact]
    public void Parse_ignores_lines_without_equals_and_handles_crlf()
    {
        var entries = ModConfig.Parse("just a line\r\nKey = Val\r\n");
        Assert.Single(entries);
        Assert.Equal("Val", entries[0].Value);
    }

    [Fact]
    public void SetValue_changes_only_the_target_value()
    {
        var src = "# desc\npet_name = Truffle\nenable_rename = true\n";
        var outp = ModConfig.SetValue(src, null, "pet_name", "Rocky");
        Assert.Contains("pet_name = Rocky", outp);
        Assert.Contains("# desc", outp);              // comment preserved
        Assert.Contains("enable_rename = true", outp); // other key untouched
    }

    [Fact]
    public void SetValue_respects_section_scoping()
    {
        var src = "[A]\nKey = 1\n[B]\nKey = 2\n";
        var outp = ModConfig.SetValue(src, "B", "Key", "9");
        Assert.Contains("[A]\r\nKey = 1", outp.Replace("\n", "\r\n").Replace("\r\r", "\r")); // A.Key untouched
        Assert.Equal("9", ModConfig.Parse(outp).First(e => e.Section == "B" && e.Key == "Key").Value);
        Assert.Equal("1", ModConfig.Parse(outp).First(e => e.Section == "A" && e.Key == "Key").Value);
    }

    [Fact]
    public void SetValue_preserves_key_indentation_and_spacing_prefix()
    {
        var outp = ModConfig.SetValue("pet_name = Truffle\n", null, "pet_name", "Rocky");
        Assert.StartsWith("pet_name =", outp); // left of '=' preserved
    }

    [Fact]
    public void SetValue_appends_when_key_absent()
    {
        var outp = ModConfig.SetValue("existing = 1\n", null, "newkey", "v");
        Assert.Equal("v", ModConfig.Parse(outp).First(e => e.Key == "newkey").Value);
        Assert.Equal("1", ModConfig.Parse(outp).First(e => e.Key == "existing").Value);
    }
}
