using ModManager.Core.ConfigMods;

namespace ModManager.Tests.ConfigMods;

/// <summary>
/// Byte-exact adversarial probes from the spec review, locked in as permanent tests.
/// Every assertion is full-string equality on the complete merged output — placement,
/// separators, and newline style are all load-bearing.
/// </summary>
public class ConfigMergeAdversarialTests
{
    [Fact]
    public void Insert_lands_inside_the_first_section_not_at_file_end()
        => Assert.Equal("[A]\r\nX=1\r\nZ=3\r\n[B]\r\nY=2\r\n",
            ConfigMerge.Merge("[A]\r\nX=1\r\n[B]\r\nY=2\r\n", "[A]\r\nZ=3\r\n"));

    [Fact]
    public void Insert_lands_before_trailing_blanks_inside_the_section_block()
        => Assert.Equal("[A]\r\nX=1\r\nZ=3\r\n\r\n\r\n[B]\r\nY=2\r\n",
            ConfigMerge.Merge("[A]\r\nX=1\r\n\r\n\r\n[B]\r\nY=2\r\n", "[A]\r\nZ=3\r\n"));

    [Fact]
    public void Appended_section_then_existing_section_sequencing_is_correct()
        => Assert.Equal("[B]\r\nY=2\r\n\r\n[New]\r\nN=1\r\n",
            ConfigMerge.Merge("[B]\r\nY=1\r\n", "[New]\r\nN=1\r\n[B]\r\nY=2\r\n"));

    [Fact]
    public void Remerge_is_byte_identical()
    {
        var existing = "; user\r\n[A]\r\nX=1\r\n+Arr=V\r\n";
        var mod = "[A]\r\nX=9\r\n+Arr=W\r\n[N]\r\nK=1\r\n";
        var once = ConfigMerge.Merge(existing, mod);
        Assert.Equal(once, ConfigMerge.Merge(once, mod));
    }

    [Fact]
    public void LF_existing_with_CRLF_mod_yields_pure_LF_output()
    {
        var merged = ConfigMerge.Merge("[A]\nX=1\n", "[A]\r\nY=2\r\n[B]\r\nZ=3\r\n");
        Assert.Equal("[A]\nX=1\nY=2\n\n[B]\nZ=3\n", merged);
        Assert.DoesNotContain('\r', merged);
    }

    [Fact]
    public void Global_prelude_before_any_section_is_untouched()
        => Assert.Equal("Global=1\r\n[A]\r\nX=2\r\n",
            ConfigMerge.Merge("Global=1\r\n[A]\r\nX=1\r\n", "[A]\r\nX=2\r\n"));
}
