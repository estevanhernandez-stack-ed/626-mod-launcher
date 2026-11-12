using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// "Steam is running" is not "Steam is ready" (packaged 0.28 smoke, 2026-11-11). The launch guard started Steam
/// and launched ACE COMBAT 8's exe as soon as steam.exe existed. Measured on Neb: the process appears at 0.1 s
/// and the account signs in at 3.1 s, so the game asked Steam about ownership with nobody signed in and showed
/// "you don't own this game". Steam writes the signed-in account to HKCU\...\ActiveProcess\ActiveUser, 0 until
/// sign-in and 0 again after shutdown: that is the readiness signal.
/// </summary>
public class SteamReadinessTests
{
    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]          // the old guard's "ready": process up, nobody signed in
    [InlineData(false, 8945417, false)]   // a stale value with no client (Steam crashed) is not ready
    [InlineData(true, 8945417, true)]
    public void Ready_means_the_client_is_running_and_an_account_is_signed_in(bool running, int activeUser, bool ready)
        => Assert.Equal(ready, SteamReadiness.IsReady(running, activeUser));

    private sealed class Clock
    {
        public DateTime Now = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
        public void Sleep(TimeSpan t) => Now += t;
    }

    [Fact]
    public void The_wait_holds_until_sign_in_not_until_the_process_appears()
    {
        var clock = new Clock();
        var start = clock.Now;
        bool Running() => clock.Now - start >= TimeSpan.FromMilliseconds(100);
        int User() => clock.Now - start >= TimeSpan.FromSeconds(3.1) ? 8945417 : 0;

        var ready = SteamReadiness.WaitUntilReady(Running, User, TimeSpan.FromSeconds(60), clock.Sleep, () => clock.Now);

        Assert.True(ready);
        Assert.True(clock.Now - start >= TimeSpan.FromSeconds(3.1));   // not released at 0.1 s
    }

    [Fact]
    public void A_client_that_never_signs_in_times_out_false()
    {
        var clock = new Clock();
        var start = clock.Now;

        var ready = SteamReadiness.WaitUntilReady(() => true, () => 0, TimeSpan.FromSeconds(60), clock.Sleep, () => clock.Now);

        Assert.False(ready);
        Assert.True(clock.Now - start >= TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Already_ready_returns_at_once()
    {
        var clock = new Clock();
        var start = clock.Now;

        Assert.True(SteamReadiness.WaitUntilReady(() => true, () => 1, TimeSpan.FromSeconds(60), clock.Sleep, () => clock.Now));
        Assert.Equal(start, clock.Now);
    }
}
