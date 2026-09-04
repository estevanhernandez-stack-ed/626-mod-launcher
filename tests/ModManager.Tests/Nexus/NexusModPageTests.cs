using ModManager.Core.Nexus;

namespace ModManager.Tests.Nexus;

public class NexusModPageTests
{
    [Fact]
    public void A_domain_and_an_id_make_the_mod_page()
        => Assert.Equal("https://www.nexusmods.com/windrose/mods/153",
                        NexusModPage.Url("windrose", 153));

    // Null rather than a half-built link. A domain with no id is the game's whole mod list, which is
    // not what a row naming one mod promised; an id with no domain cannot be addressed at all. The
    // caller uses null to decide whether to show the button, so a wrong non-null here becomes a
    // button that goes somewhere the user did not ask for.
    [Theory]
    [InlineData(null, 153)]
    [InlineData("", 153)]
    [InlineData("   ", 153)]
    [InlineData("windrose", null)]
    [InlineData("windrose", 0)]
    [InlineData("windrose", -1)]
    public void Anything_missing_or_meaningless_yields_null(string? domain, int? modId)
        => Assert.Null(NexusModPage.Url(domain, modId));
}
