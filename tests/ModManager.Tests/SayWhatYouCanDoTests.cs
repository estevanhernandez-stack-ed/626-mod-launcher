using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Wave 8, item 3. The in-app storefront button bound CatalogVisibility and disappeared entirely when
/// the capability was missing — collapsing "not signed in", "plugin not installed" and "Nexus has no
/// page for this game" into the same nothing. The first two are one step from working.
/// </summary>
public class ModBrowseRulesTests
{
    [Fact]
    public void When_everything_is_in_place_the_button_browses()
    {
        var a = ModBrowseRules.For(connected: true, catalogCapable: true, hasDomain: true);

        Assert.True(a.Show);
        Assert.True(a.CanBrowse);
        Assert.Equal(BrowseRemedy.None, a.Remedy);
    }

    [Fact]
    public void Not_signed_in_shows_the_button_and_names_the_step()
    {
        // The fault, stated as an assertion: this used to render nothing at all, so the app looked
        // like it had never built an in-app storefront.
        var a = ModBrowseRules.For(connected: false, catalogCapable: true, hasDomain: true);

        Assert.True(a.Show);
        Assert.False(a.CanBrowse);
        Assert.Equal(BrowseRemedy.ConnectNexus, a.Remedy);
        Assert.Contains("Sign in", a.Detail);
    }

    [Fact]
    public void A_missing_plugin_shows_the_button_and_names_the_step()
    {
        var a = ModBrowseRules.For(connected: true, catalogCapable: false, hasDomain: true);

        Assert.True(a.Show);
        Assert.False(a.CanBrowse);
        Assert.Equal(BrowseRemedy.InstallPlugin, a.Remedy);
        Assert.Contains("Settings", a.Detail);
    }

    [Fact]
    public void A_game_Nexus_has_no_page_for_hides_it_because_no_step_would_help()
    {
        // The one case where vanishing is right. The rule is "hide only when it can never work HERE",
        // not "never hide".
        foreach (var connected in new[] { true, false })
        foreach (var capable in new[] { true, false })
        {
            var a = ModBrowseRules.For(connected, capable, hasDomain: false);
            Assert.False(a.Show);
            Assert.Equal(BrowseRemedy.None, a.Remedy);
        }
    }

    [Fact]
    public void Sign_in_outranks_the_plugin_when_both_are_missing()
    {
        // Ordered by what the user does first. Installing the plugin while signed out would leave
        // them exactly where they started.
        var a = ModBrowseRules.For(connected: false, catalogCapable: false, hasDomain: true);

        Assert.Equal(BrowseRemedy.ConnectNexus, a.Remedy);
    }

    [Fact]
    public void The_two_doors_are_told_apart_by_where_they_land()
    {
        // They read "Browse Nexus" and "Find mods" — neither said which one leaves the app, and the
        // round table's rule is that the capability belongs in the LABEL, not the hover.
        Assert.NotEqual(ModBrowseRules.InAppLabel, ModBrowseRules.BrowserLabel);
        Assert.Contains("in-app", ModBrowseRules.InAppLabel);
        Assert.Contains("browser", ModBrowseRules.BrowserLabel);
    }

    [Fact]
    public void The_label_never_changes_with_availability()
    {
        // A control that renames itself when it cannot act is a second thing to learn. The label says
        // what it is FOR; the detail says whether it can, and what to do about it.
        var all = new[]
        {
            ModBrowseRules.For(true, true, true),
            ModBrowseRules.For(false, true, true),
            ModBrowseRules.For(true, false, true),
        };

        Assert.All(all, a => Assert.Equal(ModBrowseRules.InAppLabel, a.Label));
    }
}

/// <summary>
/// Wave 8, item 4. EmptyVisibility was <c>HasGame ? Collapsed : Visible</c> and the zero-match message
/// only fired on a search miss, so a registered game with no mods rendered a blank rectangle with no
/// words. Wave 6 opened a second hole in the same place when the MP/SP segments became a filter.
/// </summary>
public class ModListEmptyStateTests
{
    private static string? Msg(bool hasGame, int total, int visible, string? search = null, string? mode = null)
        => ModListEmptyState.MessageFor(hasGame, total, visible, search, mode);

    [Fact]
    public void No_game_says_how_to_add_one()
        => Assert.Contains("+ Game", Msg(false, 0, 0)!);

    [Fact]
    public void A_registered_game_with_no_mods_says_how_to_get_some()
    {
        // The most important empty state in the app, and the one that rendered as nothing at all.
        var m = Msg(true, 0, 0)!;

        Assert.Contains("Drop", m);
        Assert.Contains("+ Add mods", m);
    }

    [Fact]
    public void A_search_that_matches_nothing_names_the_query()
        => Assert.Contains("\"bonfire\"", Msg(true, 27, 0, search: "bonfire")!);

    [Fact]
    public void A_mode_filter_that_matches_nothing_names_the_control_that_did_it()
    {
        // The hole wave 6 opened. Worse than the no-mods case, because the segments are a VIEW
        // control and a wordless blank list reads as "the mods are gone".
        var m = Msg(true, 27, 0, mode: "mp")!;

        Assert.Contains("MP", m);
        Assert.Contains("27", m);
        Assert.Contains("All", m);
    }

    [Fact]
    public void Both_filters_at_once_says_how_to_undo_either()
    {
        var m = Msg(true, 27, 0, search: "bonfire", mode: "sp")!;

        Assert.Contains("\"bonfire\"", m);
        Assert.Contains("SP", m);
        Assert.Contains("All", m);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("")]
    [InlineData(null)]
    public void All_is_not_a_filter_so_it_is_never_blamed(string? mode)
    {
        // "None of your 27 mods are marked ALL" would be nonsense. All is the default, and the
        // default never did anything to you.
        var m = Msg(true, 27, 0, mode: mode)!;

        Assert.Equal("Nothing to show here.", m);
    }

    [Fact]
    public void A_list_with_rows_in_it_says_nothing_at_all()
    {
        // Null, not an empty string: the App binds visibility off this, and an empty message that is
        // still "present" is how a stray blank line ends up above the mods.
        Assert.Null(Msg(true, 27, 27));
        Assert.Null(Msg(true, 27, 3, search: "bonfire"));
        Assert.Null(Msg(true, 27, 11, mode: "mp"));
    }

    [Fact]
    public void Every_message_says_what_to_press()
    {
        // A sentence that names the problem and stops is the blank rectangle with extra steps.
        var cases = new[]
        {
            Msg(false, 0, 0), Msg(true, 0, 0),
            Msg(true, 27, 0, search: "x"), Msg(true, 27, 0, mode: "mp"),
            Msg(true, 27, 0, search: "x", mode: "sp"),
        };

        foreach (var m in cases)
        {
            Assert.False(string.IsNullOrWhiteSpace(m));
            Assert.EndsWith(".", m);
        }
    }
}

/// <summary>
/// Wave 8, item 5. The NEEDS chip was a HyperlinkButton to a GitHub releases page — so the app's
/// answer to "you need UE4SS" was a list of files a first-time modder cannot choose between. The
/// launcher can install it; the chip had never mentioned that.
/// </summary>
public class FrameworkOfferRulesTests
{
    private const string Url = "https://github.com/UE4SS-RE/RE-UE4SS/releases";

    [Fact]
    public void It_offers_the_install_the_app_can_already_perform()
    {
        var o = FrameworkOfferRules.For("UE4SS", Url, soft: false);

        Assert.True(o.CanInstallHere);
        Assert.Equal("I already have the file", o.InstallLabel);
        Assert.Equal("Get it", o.GetLabel);
    }

    [Fact]
    public void It_states_the_consequence_and_never_defines_the_thing()
    {
        // The pattern the LOADER and BAN RISK chips already prove works on someone who has never
        // modded: say what happens, not what it is.
        var o = FrameworkOfferRules.For("UE4SS", Url, soft: false);

        Assert.Contains("will not load", o.Consequence);
        Assert.Contains("UE4SS", o.Consequence);
        Assert.DoesNotContain("script loader", o.Consequence);
        Assert.DoesNotContain(" is a ", o.Consequence);
    }

    [Fact]
    public void A_soft_hint_does_not_promise_something_it_cannot_know()
    {
        // The row says MAY NEED when the launcher is not certain. "will not load" there is a lie, and
        // the kind that makes someone install something they did not need.
        var soft = FrameworkOfferRules.For("Elden Mod Loader", Url, soft: true);

        Assert.Contains("may need", soft.Consequence);
        Assert.Contains("If it does", soft.Consequence);
    }

    [Fact]
    public void With_no_download_page_it_still_offers_the_install()
    {
        // Losing the link must not lose the door the app actually owns.
        var o = FrameworkOfferRules.For("UE4SS", null, soft: false);

        Assert.Null(o.GetLabel);
        Assert.True(o.CanInstallHere);
    }

    [Fact]
    public void A_non_http_url_is_not_offered_as_a_link()
    {
        // Same gate the row chip already applies via SafeUrl — a catalog entry with a file:// or
        // javascript: URL must not become a button.
        Assert.Null(FrameworkOfferRules.For("UE4SS", "file:///C:/Windows", soft: false).GetLabel);
        Assert.Null(FrameworkOfferRules.For("UE4SS", "not a url", soft: false).GetLabel);
    }

    [Fact]
    public void A_nameless_framework_still_produces_a_readable_offer()
    {
        var o = FrameworkOfferRules.For("  ", Url, soft: false);

        Assert.DoesNotContain("  is not installed", o.Title);
        Assert.Contains("this framework", o.Title);
        Assert.Contains("this framework", o.Consequence);
    }

    [Fact]
    public void The_title_names_the_framework_and_nothing_else()
    {
        var o = FrameworkOfferRules.For("UE4SS", Url, soft: false);

        Assert.Equal("UE4SS is not installed", o.Title);
    }
}
