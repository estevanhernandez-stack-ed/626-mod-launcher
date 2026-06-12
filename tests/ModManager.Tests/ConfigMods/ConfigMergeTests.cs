using ModManager.Core.ConfigMods;

namespace ModManager.Tests.ConfigMods;

public class ConfigMergeTests
{
    [Fact]
    public void Plain_key_in_existing_section_is_replaced()
    {
        var existing = "[Core.System]\r\nFoo=1\r\nBar=2\r\n";
        var mod = "[Core.System]\r\nFoo=99\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.Contains("Foo=99", merged);
        Assert.DoesNotContain("Foo=1", merged);
        Assert.Contains("Bar=2", merged); // untouched key survives
    }

    [Fact]
    public void Missing_key_is_added_to_the_existing_section()
    {
        var existing = "[Core.System]\r\nBar=2\r\n";
        var mod = "[Core.System]\r\nFoo=1\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.Contains("Foo=1", merged);
        Assert.Contains("Bar=2", merged);
    }

    [Fact]
    public void Missing_section_is_appended_whole()
    {
        var existing = "[A]\r\nX=1\r\n";
        var mod = "[SystemSettings]\r\nr.Lumen=0\r\nr.Shadow=1\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.Contains("[SystemSettings]", merged);
        Assert.Contains("r.Lumen=0", merged);
        Assert.Contains("r.Shadow=1", merged);
        Assert.Contains("X=1", merged);
    }

    [Fact]
    public void UE_array_syntax_appends_instead_of_replacing()
    {
        var existing = "[Audio]\r\n+Mix=Default\r\n";
        var mod = "[Audio]\r\n+Mix=Loud\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.Contains("+Mix=Default", merged); // existing array line survives
        Assert.Contains("+Mix=Loud", merged);    // mod array line appended
    }

    [Fact]
    public void Identical_array_line_is_not_duplicated_on_remerge()
    {
        var existing = "[Audio]\r\n+Mix=Loud\r\n";
        var mod = "[Audio]\r\n+Mix=Loud\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        var count = merged.Split("+Mix=Loud").Length - 1;
        Assert.Equal(1, count); // idempotent re-merge
    }

    [Fact]
    public void Comments_and_blank_lines_in_existing_are_preserved()
    {
        var existing = "; user comment\r\n\r\n[A]\r\n; keep me\r\nX=1\r\n";
        var mod = "[A]\r\nX=2\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.Contains("; user comment", merged);
        Assert.Contains("; keep me", merged);
        Assert.Contains("X=2", merged);
    }

    [Fact]
    public void Existing_newline_style_is_preserved_LF()
    {
        var existing = "[A]\nX=1\n";
        var mod = "[A]\r\nY=2\r\n";   // mod is CRLF, existing is LF -> output stays LF
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.DoesNotContain("\r\n", merged);
        Assert.Contains("Y=2", merged);
    }

    [Fact]
    public void Empty_existing_becomes_the_mod_content()
    {
        var merged = ConfigMerge.Merge("", "[A]\r\nX=1\r\n");
        Assert.Contains("[A]", merged);
        Assert.Contains("X=1", merged);
    }

    [Fact]
    public void Section_match_is_case_insensitive()
    {
        var existing = "[systemsettings]\r\nX=1\r\n";
        var mod = "[SystemSettings]\r\nX=2\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.Contains("X=2", merged);
        Assert.DoesNotContain("X=1", merged);
        Assert.Equal(1, merged.Split('[').Length - 1); // no second section created
    }

    [Fact]
    public void Mod_comments_are_not_merged_in()
    {
        var existing = "[A]\r\nX=1\r\n";
        var mod = "[A]\r\n; mod chatter\r\nY=2\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.DoesNotContain("mod chatter", merged);
        Assert.Contains("Y=2", merged);
    }
}
