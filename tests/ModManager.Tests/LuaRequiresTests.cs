using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Reading a library's dependents from source. The design's first draft said we cannot know who
/// depends on a library; for a script tree the dependency is declared, so it is readable. On the real
/// install, `require("_CatLib")` returns mhwilds_overlay and nothing else.
/// </summary>
public class LuaRequiresTests
{
    [Theory]
    [InlineData(@"local Core = require(""_CatLib"")", "_CatLib")]
    [InlineData(@"local d = require('_CatLib.draw')", "_CatLib")]          // dotted submodule -> root
    [InlineData(@"require ""_CatLib""", "_CatLib")]                        // no parens
    [InlineData(@"local x = require( ""_CatLib.game.singletons"" )", "_CatLib")]
    public void Reads_the_library_root_a_source_requires(string src, string expected)
        => Assert.Equal(expected, Assert.Single(LuaRequires.Parse(src)));

    [Fact]
    public void Collects_each_library_once()
    {
        var src = @"
            local Core  = require(""_CatLib"")
            local draw  = require(""_CatLib.draw"")
            local other = require(""SomethingElse"")
        ";

        Assert.Equal(new[] { "_CatLib", "SomethingElse" }, LuaRequires.Parse(src));
    }

    [Fact]
    public void A_computed_require_is_not_claimed_as_a_name_we_know()
    {
        // require(modName) cannot be read statically. Reporting a guess here would put a dependency on
        // screen that may not exist.
        Assert.Empty(LuaRequires.Parse(@"local m = require(modName)"));
        Assert.Empty(LuaRequires.Parse(null));
        Assert.Empty(LuaRequires.Parse(""));
    }

    [Fact]
    public void Names_the_top_level_owners_that_need_a_library()
    {
        // Mirrors the real tree: the overlay's files require CatLib; KittyBig does not.
        var sources = new Dictionary<string, string>
        {
            ["mhwilds_overlay/mod.lua"] = @"local Core = require(""_CatLib"")",
            ["mhwilds_overlay/boss/draw.lua"] = @"local d = require(""_CatLib.draw"")",
            ["KittyBig.lua"] = @"-- no requires here",
        };

        Assert.Equal(new[] { "mhwilds_overlay" }, LuaRequires.DependentsOf("_CatLib", sources));
    }

    [Fact]
    public void A_library_never_counts_as_depending_on_itself()
    {
        // CatLib's own 33 files require CatLib constantly. Reporting that would tell the user the
        // library is needed by itself, which is true and useless.
        var sources = new Dictionary<string, string>
        {
            ["_CatLib/draw.lua"] = @"local c = require(""_CatLib.const"")",
            ["_CatLib/init.lua"] = @"require(""_CatLib"")",
        };

        Assert.Empty(LuaRequires.DependentsOf("_CatLib", sources));
    }

    [Fact]
    public void Reports_each_owner_once_however_many_files_require_it()
    {
        var sources = Enumerable.Range(0, 20)
            .ToDictionary(i => $"mhwilds_overlay/f{i}.lua", _ => @"require(""_CatLib"")");

        Assert.Equal(new[] { "mhwilds_overlay" }, LuaRequires.DependentsOf("_CatLib", sources));
    }

    [Fact]
    public void Nothing_in_nothing_out()
    {
        Assert.Empty(LuaRequires.DependentsOf(null, new Dictionary<string, string>()));
        Assert.Empty(LuaRequires.DependentsOf("_CatLib", null));
        Assert.Empty(LuaRequires.DependentsOf("   ", new Dictionary<string, string>()));
    }

    // ---- the reverse read: what a mod needs and does not have ------------------------------

    [Fact]
    public void Tells_a_mod_which_library_it_is_missing()
    {
        // The more valuable direction: a mod that would silently fail to load becomes a row that says
        // why - the NEEDS UE4SS chip's job, applied to libraries inside the mod folder.
        var missing = LuaRequires.MissingFor(
            new[] { @"local Core = require(""_CatLib"")" },
            present: new[] { "KittyBig", "utility" });

        Assert.Equal("_CatLib", Assert.Single(missing));
    }

    [Fact]
    public void Says_nothing_when_the_library_is_there()
        => Assert.Empty(LuaRequires.MissingFor(
            new[] { @"require(""_CatLib"")" }, present: new[] { "_CatLib", "utility" }));

    [Fact]
    public void Reports_each_missing_library_once_across_many_files()
    {
        var missing = LuaRequires.MissingFor(
            new[] { @"require(""_CatLib"")", @"require(""_CatLib.draw"")", @"require(""Other"")" },
            present: Array.Empty<string>());

        Assert.Equal(new[] { "_CatLib", "Other" }, missing);
    }
}
