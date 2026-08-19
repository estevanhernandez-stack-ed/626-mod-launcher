using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Wave 7. The launcher used to say what was true about a game in EIGHT places across TWO registers:
/// four full-width banners above the mod list rendered in declaration order, and four inline warnings
/// in the command bar's right cluster. So the weights ran backwards against consequence — the
/// ban-risk warning, the only one whose cost lands outside this machine, was a
/// <c>Border Padding="8,2"</c> next to the theme picker, while "another tool has files in a folder"
/// took a full-width bar.
///
/// <para>The ranking is the arguable part, so it lives in Core under test rather than being
/// re-decided by whoever adds markup next.</para>
/// </summary>
public class GameStateStripTests
{
    private static readonly GameStateConditions Nothing = new();

    private static List<string> Ids(GameStateConditions c)
        => GameStateStrip.For(c).Select(x => x.Id).ToList();

    [Fact]
    public void A_game_with_nothing_wrong_gets_no_strip_at_all()
    {
        // Empty list, not a list with an empty chip: the App renders nothing rather than an empty
        // container taking a row's worth of height above the mods.
        Assert.Empty(GameStateStrip.For(Nothing));
        Assert.Empty(GameStateStrip.For(null));
    }

    [Theory]
    [InlineData("ban-risk")]
    [InlineData("launch-options")]
    [InlineData("framework-missing")]
    [InlineData("setup-drift")]
    [InlineData("steam-updated")]
    [InlineData("coop-launcher")]
    [InlineData("mp-desync")]
    [InlineData("vortex-redeployed")]
    [InlineData("vortex-managed")]
    public void Every_condition_alone_produces_exactly_its_own_chip(string id)
        => Assert.Equal(new[] { id }, Ids(Only(id)));

    [Fact]
    public void Ban_risk_outranks_everything_including_all_of_it_at_once()
        => Assert.Equal("ban-risk", GameStateStrip.For(Everything())[0].Id);

    [Fact]
    public void The_full_order_is_the_one_that_was_agreed()
    {
        // Account first, then "nothing loads", then "some things do not load", then "this may be
        // stale", then "other players", then "another tool owns this".
        Assert.Equal(new[]
        {
            "ban-risk",
            "launch-options",
            "framework-missing",
            "setup-drift",
            "steam-updated",
            "coop-launcher",
            "mp-desync",
            "vortex-redeployed",
        }, Ids(Everything()));
    }

    [Fact]
    public void Adding_a_lesser_condition_never_displaces_a_greater_one()
    {
        // The failure this pins is the one the old layout actually had: a low-consequence state
        // rendering above a high-consequence one because it was declared first.
        Assert.Equal(new[] { "ban-risk" }, Ids(Only("ban-risk")));
        Assert.Equal(new[] { "ban-risk", "vortex-managed" }, Ids(Only("ban-risk") with { VortexManaged = true }));
        Assert.Equal("ban-risk", Ids(Everything())[0]);
    }

    [Fact]
    public void Vortex_says_one_thing_at_a_time()
    {
        // Re-deployed SUPERSEDES managed — same folder, same fact, one step worse. Two Vortex chips
        // side by side would read as two separate problems.
        var both = new GameStateConditions { VortexReDeployed = true, VortexManaged = true };

        Assert.Equal(new[] { "vortex-redeployed" }, Ids(both));
    }

    [Fact]
    public void Ban_risk_can_never_be_dismissed_and_the_informational_ones_can()
    {
        // The one dismiss rule, stated once: a chip is dismissible only when dismissing hides
        // nothing that is still true.
        var chips = GameStateStrip
            .For(Everything() with { VortexReDeployed = false, VortexManaged = true })
            .ToDictionary(c => c.Id);

        Assert.False(chips["ban-risk"].Dismissible);
        Assert.False(chips["launch-options"].Dismissible);
        Assert.False(chips["framework-missing"].Dismissible);
        Assert.False(chips["mp-desync"].Dismissible);

        Assert.True(chips["setup-drift"].Dismissible);
        Assert.True(chips["vortex-managed"].Dismissible);
    }

    [Fact]
    public void Mark_as_rechecked_is_an_action_and_not_a_dismissal()
    {
        // It re-records the build baseline. Keeping that distinction is what lets the strip state
        // one dismiss rule and still carry this button.
        var steam = GameStateStrip.For(Only("steam-updated"))[0];

        Assert.False(steam.Dismissible);
        Assert.Equal("Mark as rechecked", steam.ActionLabel);
    }

    [Fact]
    public void The_framework_chip_carries_the_sentence_the_launcher_learned_to_say()
    {
        // Wave 3 taught this summary to name WHICH PART is missing — "loader present, runtime
        // missing" rather than "Missing: UE4SS" — and until this wave it was bound in no XAML file
        // at all: computed on every reload and rendered by nothing.
        var chip = GameStateStrip.For(new GameStateConditions
        {
            MissingFrameworks = "UE4SS — loader present, runtime missing",
        })[0];

        Assert.Contains("loader present, runtime missing", chip.Detail);
        Assert.Contains("will not load", chip.Detail);
    }

    [Fact]
    public void A_blank_summary_is_not_a_condition()
    {
        // HasMissingFrameworks is a count; a whitespace string must not manufacture a chip that
        // names nothing.
        Assert.Empty(GameStateStrip.For(new GameStateConditions { MissingFrameworks = "   " }));
        Assert.Empty(GameStateStrip.For(new GameStateConditions { MpWarning = "" }));
    }

    [Fact]
    public void The_steam_chip_falls_back_to_a_sentence_when_the_message_is_missing()
    {
        var withMessage = GameStateStrip.For(Only("steam-updated") with { SteamMessage = "Steam updated ELDEN RING." })[0];
        Assert.Equal("Steam updated ELDEN RING.", withMessage.Detail);

        Assert.Contains("rechecking", GameStateStrip.For(Only("steam-updated"))[0].Detail);
    }

    [Fact]
    public void Every_chip_says_the_consequence_not_the_definition()
    {
        // The pattern the LOADER and BAN RISK copy already prove works on someone who has never
        // modded: say what happens, not what the thing is. A detail shorter than its own label has
        // almost certainly reverted to naming the thing.
        foreach (var chip in GameStateStrip.For(Everything()))
        {
            Assert.False(string.IsNullOrWhiteSpace(chip.Detail));
            Assert.True(chip.Detail.Length > chip.Label.Length, chip.Id + " explains nothing");
            Assert.EndsWith(".", chip.Detail);
        }
    }

    [Fact]
    public void Chip_ids_are_stable_kebab_case_because_a_harness_keys_on_them()
    {
        // Automation identity outlives display copy — this repo treats microcopy as something to
        // keep improving, so a harness keyed on the LABEL goes red for no behavioural reason.
        // See .claude/rules/automation-ids.md.
        foreach (var chip in GameStateStrip.For(Everything()))
        {
            Assert.Equal(chip.Id.ToLowerInvariant(), chip.Id);
            Assert.DoesNotContain(" ", chip.Id);
            Assert.DoesNotContain(".", chip.Id);
        }
    }

    [Fact]
    public void The_lead_chip_is_the_most_severe_one_and_only_when_it_is_severe()
    {
        // The lead reads as a full sentence with no interaction. That is what stops the
        // highest-consequence line being the smallest thing on the screen.
        Assert.Equal("ban-risk", GameStateStrip.LeadFor(GameStateStrip.For(Everything()))!.Id);

        var mildOnly = GameStateStrip.For(new GameStateConditions { VortexManaged = true, SetupDrift = true });
        Assert.Null(GameStateStrip.LeadFor(mildOnly));

        Assert.Null(GameStateStrip.LeadFor(GameStateStrip.For(Nothing)));
        Assert.Null(GameStateStrip.LeadFor(null));
    }

    [Fact]
    public void Severity_is_assigned_by_what_breaks_not_by_where_it_used_to_render()
    {
        var chips = GameStateStrip.For(Everything()).ToDictionary(c => c.Id);

        // These three stop mods loading, or worse. Two used to be inline text beside the theme
        // picker; one rendered nowhere at all.
        Assert.Equal(GameStateSeverity.Danger, chips["ban-risk"].Severity);
        Assert.Equal(GameStateSeverity.Danger, chips["launch-options"].Severity);
        Assert.Equal(GameStateSeverity.Danger, chips["framework-missing"].Severity);

        // This one costs nothing and used to take a full-width bar above the mod list.
        Assert.Equal(GameStateSeverity.Info, GameStateStrip.For(Only("vortex-managed"))[0].Severity);
    }

    private static GameStateConditions Everything() => new()
    {
        BanRisk = true,
        LaunchOptionsNeeded = true,
        MissingFrameworks = "UE4SS — loader present, runtime missing",
        SetupDrift = true,
        SteamUpdated = true,
        CoopLauncherMissing = true,
        MpWarning = "2 enabled mods may desync co-op.",
        VortexReDeployed = true,
        VortexManaged = true,
    };

    private static GameStateConditions Only(string id) => id switch
    {
        "ban-risk" => new GameStateConditions { BanRisk = true },
        "launch-options" => new GameStateConditions { LaunchOptionsNeeded = true },
        "framework-missing" => new GameStateConditions { MissingFrameworks = "UE4SS — loader present, runtime missing" },
        "setup-drift" => new GameStateConditions { SetupDrift = true },
        "steam-updated" => new GameStateConditions { SteamUpdated = true },
        "coop-launcher" => new GameStateConditions { CoopLauncherMissing = true },
        "mp-desync" => new GameStateConditions { MpWarning = "2 enabled mods may desync co-op." },
        "vortex-redeployed" => new GameStateConditions { VortexReDeployed = true },
        "vortex-managed" => new GameStateConditions { VortexManaged = true },
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown condition"),
    };
}
