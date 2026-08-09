using ModManager.Core;

namespace ModManager.Tests;

// The app has ONE busy ring, ONE Stop button, and ONE cancellation source, so it can have exactly
// one long operation. This type owns that decision — held or not, by whom, and what to say when a
// second one asks. It lives in Core because the view model that used to hold it takes fourteen
// concrete services, so nothing could construct it and nothing could test it; the two defects a
// single live smoke pass found (a refusal naming the wrong operation, and a flag that could latch)
// were both decisions, not wiring.
public class LongOperationSlotTests
{
    [Fact]
    public void A_free_slot_is_claimable()
    {
        var slot = new LongOperationSlot();

        Assert.False(slot.IsHeld);
        Assert.True(slot.TryClaim("Identify"));
        Assert.True(slot.IsHeld);
    }

    [Fact]
    public void A_held_slot_refuses_the_second_claim()
    {
        var slot = new LongOperationSlot();
        slot.TryClaim("Identify");

        Assert.False(slot.TryClaim("Getting details"));
        Assert.True(slot.IsHeld); // still the FIRST holder's, untouched
    }

    // The bug this replaces: the message named the operation being STARTED, so clicking "Identify"
    // while details refreshed produced "Identify is already running" — an answer that reads as a
    // glitch instead of an explanation. Name what is in the way.
    [Fact]
    public void The_refusal_names_the_operation_that_is_running_not_the_one_attempted()
    {
        var slot = new LongOperationSlot();
        slot.TryClaim("Getting details");

        Assert.Contains("Getting details", slot.RefusalMessage);
        Assert.DoesNotContain("Identify", slot.RefusalMessage);
    }

    [Fact]
    public void There_is_nothing_to_refuse_while_the_slot_is_free()
    {
        var slot = new LongOperationSlot();

        Assert.Equal("", slot.RefusalMessage);
    }

    [Fact]
    public void Releasing_hands_the_slot_back()
    {
        var slot = new LongOperationSlot();
        slot.TryClaim("Identify");

        slot.Release();

        Assert.False(slot.IsHeld);
        Assert.True(slot.TryClaim("Getting details"));
        Assert.Contains("Getting details", slot.RefusalMessage);
    }

    // Release lives in a finally, and a finally can run on a path that never claimed — a guard that
    // threw on the way in, or an early return. It must not throw or corrupt state.
    [Fact]
    public void Releasing_a_slot_nobody_holds_is_a_no_op()
    {
        var slot = new LongOperationSlot();

        slot.Release();
        slot.Release();

        Assert.False(slot.IsHeld);
        Assert.True(slot.TryClaim("Identify"));
    }

    // The failure mode worth naming: a slot that stays claimed silently bricks every long action
    // until the app restarts. Any number of failed claims must leave the holder able to release.
    [Fact]
    public void A_run_of_refusals_never_strands_the_slot()
    {
        var slot = new LongOperationSlot();
        slot.TryClaim("Identify");

        for (var i = 0; i < 5; i++) Assert.False(slot.TryClaim("Getting details"));

        slot.Release();
        Assert.False(slot.IsHeld);
        Assert.True(slot.TryClaim("Identify"));
    }

    [Fact]
    public void A_claim_with_no_name_still_produces_a_usable_refusal()
    {
        var slot = new LongOperationSlot();
        slot.TryClaim("");

        Assert.True(slot.IsHeld);
        Assert.NotEqual("", slot.RefusalMessage); // says SOMETHING rather than " is already running."
    }
}
