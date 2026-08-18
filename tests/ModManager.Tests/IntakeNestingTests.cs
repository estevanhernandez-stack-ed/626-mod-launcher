using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// A19. Intake placed every archive entry by its bare filename, so a mod shipping a tree landed flat.
/// Correct for a pak game and destructive for a script tree: CatLib is 33 lua files under
/// <c>autorun/_CatLib/</c> that resolve nowhere once flattened, and two mods each shipping
/// <c>autorun/utility/Statics.lua</c> collide on one name with the second silently winning. The
/// install reported success and did not work.
///
/// <para>Every path below is verbatim from a real Monster Hunter Wilds archive, read via
/// <c>Scanner.PlanIntake</c> against Este's install.</para>
/// </summary>
public class IntakeNestingTests
{
    private const string Anchor = "reframework/autorun";

    private static string P(params string[] parts) => string.Join(Path.DirectorySeparatorChar, parts);

    [Theory]
    // A wrapper folder named after the mod, then the real path.
    [InlineData("_CatLib/reframework/autorun/_CatLib/action.lua", "_CatLib", "action.lua")]
    // A release-versioned wrapper.
    [InlineData("KittyBig-v1.0/reframework/autorun/KittyBig.lua", "KittyBig.lua")]
    // No wrapper at all - the archive starts at the anchor.
    [InlineData("reframework/autorun/utility/Statics.lua", "utility", "Statics.lua")]
    // A deep tree under the mod's own folder.
    [InlineData("MHWilds Overlay/reframework/autorun/mhwilds_overlay/backups/draw.lua",
                "mhwilds_overlay", "backups", "draw.lua")]
    public void Keeps_everything_below_the_declared_mod_location(string entry, params string[] expected)
        => Assert.Equal(P(expected), IntakeNesting.RelativeUnderAnchor(entry, Anchor));

    [Fact]
    public void Anchors_on_the_LAST_match_so_a_repeated_name_does_not_duplicate_a_level()
    {
        // The mod's folder shares its name with the wrapper. Anchoring on the first match would yield
        // reframework/autorun/_CatLib/action.lua and reinstall the whole tree one level too deep.
        Assert.Equal(P("_CatLib", "action.lua"),
            IntakeNesting.RelativeUnderAnchor("_CatLib/reframework/autorun/_CatLib/action.lua", Anchor));
    }

    [Fact]
    public void Returns_null_when_the_archive_says_nothing_so_the_caller_stays_flat()
    {
        // A pak mod. Flat is correct here and has always been correct - a pak is one file and its name
        // is its identity. This is the case that keeps every existing pak game byte-identical.
        Assert.Null(IntakeNesting.RelativeUnderAnchor(
            "Shop Tweaks/Shop Tweaks - Everything/Shop Tweaks - Everything.pak", Anchor));

        // And an archive with no folders at all.
        Assert.Null(IntakeNesting.RelativeUnderAnchor("re_chunk_000.pak.patch_002.pak", Anchor));
    }

    [Fact]
    public void Returns_null_when_the_entry_IS_the_anchor_with_nothing_under_it()
        => Assert.Null(IntakeNesting.RelativeUnderAnchor("reframework/autorun", Anchor));

    [Fact]
    public void Matches_a_partial_anchor_only_as_a_whole()
    {
        // "autorun" alone is not the anchor. Matching a tail segment would let any folder called
        // autorun anywhere in an archive redirect the install.
        Assert.Null(IntakeNesting.RelativeUnderAnchor("autorun/KittyBig.lua", Anchor));
        Assert.Null(IntakeNesting.RelativeUnderAnchor("reframework/KittyBig.lua", Anchor));
    }

    [Theory]
    // Zips use forward slashes; a declared location on disk uses backslashes. Neither spelling is a
    // choice anyone made, and requiring them to agree would make the fix work by luck.
    [InlineData("reframework/autorun/KittyBig.lua", @"reframework\autorun")]
    [InlineData(@"reframework\autorun\KittyBig.lua", "reframework/autorun")]
    [InlineData("reframework/autorun/KittyBig.lua", "reframework/autorun/")]
    [InlineData("REFRAMEWORK/AutoRun/KittyBig.lua", "reframework/autorun")]
    public void Separator_trailing_slash_and_case_do_not_matter(string entry, string anchor)
        => Assert.Equal("KittyBig.lua", IntakeNesting.RelativeUnderAnchor(entry, anchor));

    [Theory]
    [InlineData(null, "reframework/autorun")]
    [InlineData("reframework/autorun/x.lua", null)]
    [InlineData("", "")]
    [InlineData("   ", "reframework/autorun")]
    public void Nothing_in_nothing_out(string? entry, string? anchor)
        => Assert.Null(IntakeNesting.RelativeUnderAnchor(entry, anchor));

    [Fact]
    public void A_single_segment_anchor_works()
    {
        // Not every game nests. A "mods" location is one segment and must anchor just the same.
        Assert.Equal(P("MyMod", "script.lua"),
            IntakeNesting.RelativeUnderAnchor("Release/mods/MyMod/script.lua", "mods"));
    }
}

/// <summary>
/// Zip Slip. Preserving the archive's path means preserving whatever it says, and the previous
/// behaviour was accidentally safe: <c>Path.GetFileName</c> threw every directory component away, so
/// a traversal segment could not survive. Keeping the tree removes that accident, so the refusal has
/// to be explicit. Caught by review on the pushed commit, not by me.
/// </summary>
public class IntakeNestingTraversalTests
{
    private const string Anchor = "reframework/autorun";

    [Theory]
    [InlineData("reframework/autorun/../../../../Windows/System32/evil.dll")]
    [InlineData("reframework/autorun/../../eldenring.exe")]
    [InlineData(@"reframework\autorun\..\..\..\evil.dll")]
    [InlineData("reframework/autorun/mod/../../../escape.lua")]
    [InlineData("reframework/autorun/./sneaky.lua")]
    public void Refuses_any_entry_that_walks_out_of_the_mod_folder(string entry)
    {
        // Null puts the caller back on the filename-only fallback, which cannot escape.
        Assert.Null(IntakeNesting.RelativeUnderAnchor(entry, Anchor));
    }

    [Fact]
    public void Traversal_BEFORE_the_anchor_is_harmless_and_still_installs()
    {
        // The relative path starts fresh at the anchor, so anything above it cannot reach the
        // destination. Refusing here would reject legitimate archives for no gain.
        Assert.Equal(Path.Combine("mod", "x.lua"),
            IntakeNesting.RelativeUnderAnchor("../../reframework/autorun/mod/x.lua", Anchor));
    }

    [Fact]
    public void A_file_merely_NAMED_like_a_traversal_is_not_one()
    {
        // ".." is a segment, not a substring. A mod called "..thing.lua" is odd but not an attack.
        Assert.Equal("..thing.lua", IntakeNesting.RelativeUnderAnchor("reframework/autorun/..thing.lua", Anchor));
    }
}
