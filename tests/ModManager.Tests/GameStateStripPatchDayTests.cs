using ModManager.Core;

namespace ModManager.Tests;

/// <summary>The PATCH DAY chip: placement, tone, suppression of the two chips it replaces, copy per state.</summary>
public class GameStateStripPatchDayTests
{
    private static readonly DateTime Exe = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly StaleLoader Loader = new("REFramework", @"C:\g\dinput8.dll", Exe.AddDays(-30), "https://example.com/get");
    private static readonly LoaderRelease Release = new("REFramework", "v1.5.9.2", Exe.AddDays(1), "https://example.com/rel");

    private static PatchDayVerdict Verdict(PatchDayState state, PatchDayChoice choice = PatchDayChoice.None,
        bool hidden = false, string? restoreError = null)
        => new(state, hidden,
            new PatchDayRecord { BuildId = "2", DetectedUtc = Exe, Choice = choice, RestorePointError = restoreError },
            Loader, state == PatchDayState.UpdateAvailable ? Release : null, Exe, false);

    private static GameStateConditions With(PatchDayVerdict? v) => new() { GameName = "Resident Evil 4", PatchDay = v };

    private static GameStateChip Chip(PatchDayVerdict v) =>
        GameStateStrip.For(With(v)).Single(c => c.Id == "patch-day");

    [Fact]
    public void Patch_day_sits_directly_after_ban_risk_and_before_launch_options()
    {
        var c = With(Verdict(PatchDayState.Patched)) with { BanRisk = true, LaunchOptionsNeeded = true };
        Assert.Equal(new[] { "ban-risk", "patch-day", "launch-options" }, GameStateStrip.For(c).Select(x => x.Id));
    }

    [Fact]
    public void Patch_day_is_a_caution_warning_and_only_ban_risk_is_red()
    {
        var c = With(Verdict(PatchDayState.Patched)) with { BanRisk = true };
        var chips = GameStateStrip.For(c);
        var pd = chips.Single(x => x.Id == "patch-day");
        Assert.Equal(GameStateTone.Caution, pd.Tone);
        Assert.Equal(GameStateSeverity.Warning, pd.Severity);
        Assert.Equal(new[] { "ban-risk" }, chips.Where(x => x.Tone == GameStateTone.Danger).Select(x => x.Id));
    }

    [Fact]
    public void Quiet_or_missing_verdict_emits_nothing()
    {
        Assert.Empty(GameStateStrip.For(With(PatchDayVerdict.Quiet)));
        Assert.Empty(GameStateStrip.For(With(null)));
    }

    [Theory]
    [InlineData(PatchDayState.Patched, false)]
    [InlineData(PatchDayState.Patched, true)]
    [InlineData(PatchDayState.PlayingVanilla, false)]
    [InlineData(PatchDayState.UpdateAvailable, false)]
    [InlineData(PatchDayState.LoaderUpdated, false)]
    public void Steam_updated_and_stale_loader_are_suppressed_while_patch_day_is_non_quiet_even_snoozed(
        PatchDayState state, bool hidden)
    {
        var c = With(Verdict(state, hidden: hidden)) with { SteamUpdated = true, StaleLoader = "old" };
        var ids = GameStateStrip.For(c).Select(x => x.Id).ToList();
        Assert.DoesNotContain("steam-updated", ids);
        Assert.DoesNotContain("stale-loader", ids);
        Assert.Equal(!hidden, ids.Contains("patch-day"));
    }

    [Fact]
    public void Both_old_chips_still_appear_when_patch_day_is_quiet_or_absent()
    {
        foreach (var v in new PatchDayVerdict?[] { null, PatchDayVerdict.Quiet })
        {
            var ids = GameStateStrip.For(With(v) with { SteamUpdated = true, StaleLoader = "old" }).Select(x => x.Id);
            Assert.Equal(new[] { "steam-updated", "stale-loader" }, ids);
        }
    }

    [Fact]
    public void Patched_first_showing_names_the_game_date_and_steam_line_with_three_actions()
    {
        var chip = Chip(Verdict(PatchDayState.Patched));
        Assert.Equal("Resident Evil 4 updated on Oct 14. Your mod loader is from before that update and probably won't load yet. "
            + "If you launch from Steam, your mods load as they are.", chip.Detail);
        Assert.Equal("Play vanilla for now", chip.ActionLabel);
        Assert.Equal(PatchDayAction.PlayVanilla, chip.PrimaryPatchDayAction);
        Assert.Equal("Play modded anyway", chip.SecondaryActionLabel);
        Assert.Equal(PatchDayAction.PlayModded, chip.SecondaryPatchDayAction);
        Assert.Equal("Tell me when it's fixed", chip.TertiaryActionLabel);
        Assert.Equal(PatchDayAction.Snooze, chip.TertiaryPatchDayAction);
        // Dismiss is session-only; snooze persists, so it must not ride on Dismissible.
        Assert.False(chip.Dismissible);
    }

    [Fact]
    public void Patched_after_a_vanilla_choice_drops_the_steam_line_only_when_choice_is_set()
    {
        var chip = Chip(Verdict(PatchDayState.Patched, PatchDayChoice.Vanilla));
        Assert.DoesNotContain("Steam", chip.Detail);
        Assert.StartsWith("Resident Evil 4 updated on Oct 14.", chip.Detail);
    }

    [Fact]
    public void Patched_with_modded_choice_shrinks_to_a_quiet_note_with_one_action()
    {
        var chip = Chip(Verdict(PatchDayState.Patched, PatchDayChoice.Modded));
        Assert.Equal("You chose to play modded after the Resident Evil 4 update. Your loader may not load.", chip.Detail);
        Assert.Equal("Play vanilla for now", chip.ActionLabel);
        Assert.Equal(PatchDayAction.PlayVanilla, chip.PrimaryPatchDayAction);
        Assert.Null(chip.SecondaryActionLabel);
        Assert.Null(chip.TertiaryActionLabel);
    }

    [Fact]
    public void Playing_vanilla_offers_bring_back()
    {
        var chip = Chip(Verdict(PatchDayState.PlayingVanilla, PatchDayChoice.Vanilla));
        Assert.Equal("Your mods are set aside since the patch. Bring them back any time.", chip.Detail);
        Assert.Equal("Bring my mods back", chip.ActionLabel);
        Assert.Equal(PatchDayAction.BringBack, chip.PrimaryPatchDayAction);
        Assert.Null(chip.SecondaryActionLabel);
    }

    [Fact]
    public void Update_available_names_the_loader_and_offers_get_then_bring_back()
    {
        var chip = Chip(Verdict(PatchDayState.UpdateAvailable));
        Assert.Equal("REFramework has an update since the patch.", chip.Detail);
        Assert.Equal("Get the update", chip.ActionLabel);
        Assert.Equal(PatchDayAction.GetUpdate, chip.PrimaryPatchDayAction);
        Assert.Equal("Bring my mods back", chip.SecondaryActionLabel);
        Assert.Equal(PatchDayAction.BringBack, chip.SecondaryPatchDayAction);
    }

    [Fact]
    public void Loader_updated_offers_bring_back()
    {
        var chip = Chip(Verdict(PatchDayState.LoaderUpdated));
        Assert.Equal("Your loader is newer than the game now. Bring your mods back?", chip.Detail);
        Assert.Equal("Bring my mods back", chip.ActionLabel);
        Assert.Equal(PatchDayAction.BringBack, chip.PrimaryPatchDayAction);
    }

    [Theory]
    [InlineData(PatchDayState.Patched)]
    [InlineData(PatchDayState.PlayingVanilla)]
    [InlineData(PatchDayState.UpdateAvailable)]
    [InlineData(PatchDayState.LoaderUpdated)]
    public void A_failed_restore_point_is_named_in_every_state(PatchDayState state)
    {
        var chip = Chip(Verdict(state, restoreError: "disk full"));
        Assert.EndsWith(" Couldn't save a restore point: disk full.", chip.Detail);
        Assert.DoesNotContain("Couldn't", Chip(Verdict(state)).Detail);
    }

    [Fact]
    public void Existing_chips_have_no_patch_day_actions()
    {
        var chip = GameStateStrip.For(new GameStateConditions { BanRisk = true, LaunchOptionsNeeded = true })[1];
        Assert.Null(chip.PrimaryPatchDayAction);
        Assert.Null(chip.SecondaryActionLabel);
    }
}
