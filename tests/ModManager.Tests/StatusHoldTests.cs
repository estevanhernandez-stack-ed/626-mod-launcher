using ModManager.Core;

namespace ModManager.Tests;

// The status line has several writers. Progress ticks are AMBIENT — they fire many times a second
// while an operation runs. A refusal, an error, an outcome are ANSWERS to something the user just
// did, and they are written once. Without a hold the ambient writer overwrites the answer within a
// frame, which is exactly what live smoke found: refusing a second long operation flashed the
// reason and the running search's counter erased it before it could be read.
public class StatusHoldTests
{
    private static (StatusHold hold, Action<TimeSpan> advance) At(DateTimeOffset start)
    {
        var now = start;
        return (new StatusHold(() => now), d => now += d);
    }

    [Fact]
    public void Ambient_writes_are_allowed_when_nothing_is_held()
    {
        var (hold, _) = At(DateTimeOffset.UnixEpoch);

        Assert.True(hold.AmbientAllowed);
    }

    [Fact]
    public void A_hold_silences_ambient_writes_for_its_duration()
    {
        var (hold, advance) = At(DateTimeOffset.UnixEpoch);

        hold.Hold(TimeSpan.FromSeconds(4));
        Assert.False(hold.AmbientAllowed);

        advance(TimeSpan.FromSeconds(3));
        Assert.False(hold.AmbientAllowed); // still inside the window — this is the whole point

        advance(TimeSpan.FromSeconds(2));
        Assert.True(hold.AmbientAllowed);  // expired, ambient resumes on its own
    }

    // No timer anywhere: the hold expires by being asked. A progress tick that arrives after the
    // window simply proceeds, so nothing has to fire to un-stick the line.
    [Fact]
    public void The_hold_expires_without_anyone_clearing_it()
    {
        var (hold, advance) = At(DateTimeOffset.UnixEpoch);
        hold.Hold(TimeSpan.FromSeconds(1));

        advance(TimeSpan.FromMinutes(5));

        Assert.True(hold.AmbientAllowed);
    }

    [Fact]
    public void A_second_hold_extends_the_window_from_now()
    {
        var (hold, advance) = At(DateTimeOffset.UnixEpoch);

        hold.Hold(TimeSpan.FromSeconds(4));
        advance(TimeSpan.FromSeconds(3));
        hold.Hold(TimeSpan.FromSeconds(4)); // user clicked again — restart the window

        advance(TimeSpan.FromSeconds(2));
        Assert.False(hold.AmbientAllowed);
    }

    // A shorter second hold must not cut a longer one short.
    [Fact]
    public void A_shorter_hold_never_shortens_a_longer_one_already_running()
    {
        var (hold, advance) = At(DateTimeOffset.UnixEpoch);

        hold.Hold(TimeSpan.FromSeconds(10));
        hold.Hold(TimeSpan.FromSeconds(1));

        advance(TimeSpan.FromSeconds(3));
        Assert.False(hold.AmbientAllowed);
    }

    [Fact]
    public void A_zero_or_negative_hold_silences_nothing()
    {
        var (hold, _) = At(DateTimeOffset.UnixEpoch);

        hold.Hold(TimeSpan.Zero);
        Assert.True(hold.AmbientAllowed);

        hold.Hold(TimeSpan.FromSeconds(-5));
        Assert.True(hold.AmbientAllowed);
    }

    // An outcome ("Adopted 4 mods.") should replace a refusal immediately — it is newer news about
    // the same thing the user asked for, not ambient chatter.
    [Fact]
    public void Clearing_releases_the_line_at_once()
    {
        var (hold, _) = At(DateTimeOffset.UnixEpoch);
        hold.Hold(TimeSpan.FromSeconds(30));

        hold.Clear();

        Assert.True(hold.AmbientAllowed);
    }
}
