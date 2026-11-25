using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// "Steam is running" is not "Steam is ready" (packaged 0.28 smoke, 2026-11-11). The launch guard started Steam
/// and launched ACE COMBAT 8's exe as soon as steam.exe existed. Measured on Neb: the process appears at 0.1 s
/// and the account signs in at 3.1 s, so the game asked Steam about ownership with nobody signed in and showed
/// "you don't own this game". Steam writes the signed-in account to HKCU\...\ActiveProcess\ActiveUser, 0 until
/// sign-in and 0 again after a clean shutdown.
///
/// <para>"0 again after shutdown" only holds for a CLEAN shutdown (#423 smoke, 2026-11-24). Measured on Neb after a
/// forced close: the registry kept the old pid and account, the new steam.exe ran from 0.08 s, Steam wrote its own pid
/// at 1.42 s, reset the account to 0 at 1.54 s and signed in at 2.79 s. "Process up and account non-zero" was true from
/// 0.08 s, and so was a registry pid match from 1.42 s. Only an account seen at 0 under the running client's pid and
/// then signed in is a fresh sign-in.</para>
/// </summary>
public class SteamReadinessTests
{
    private const int User = 8945417;

    private static SteamState S(int registryPid, int activeUser, params int[] clients) => new(clients, registryPid, activeUser);

    [Theory]
    [InlineData(0, 0, new int[0], false)]
    [InlineData(51932, 0, new[] { 51932 }, false)]        // client up, nobody signed in
    [InlineData(56576, User, new int[0], false)]          // stale values, no client (Steam crashed)
    [InlineData(56576, User, new[] { 51932 }, false)]     // stale values beside a NEW client: the 2026-11-24 race
    [InlineData(51932, User, new[] { 51932 }, true)]
    public void Ready_means_the_running_client_owns_the_registry_and_an_account_is_signed_in(
        int registryPid, int activeUser, int[] clients, bool ready)
        => Assert.Equal(ready, SteamReadiness.IsReady(S(registryPid, activeUser, clients)));

    private sealed class Clock
    {
        public DateTime Now = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
        public void Sleep(TimeSpan t) => Now += t;
    }

    private static Func<SteamState> Trace(Clock clock, params (double At, SteamState State)[] steps)
    {
        var start = clock.Now;
        return () =>
        {
            var t = (clock.Now - start).TotalSeconds;
            return steps.Last(s => s.At <= t).State;
        };
    }

    [Fact]
    public void After_a_clean_quit_the_wait_holds_until_sign_in()
    {
        // Measured 2026-11-24, clean quit: pid 0 / account 0, then the client, its pid at 0.19 s, the account at 3.07 s.
        var clock = new Clock();
        var start = clock.Now;
        var read = Trace(clock,
            (0, S(0, 0)),
            (0.08, S(0, 0, 56576)),
            (0.19, S(56576, 0, 56576)),
            (3.07, S(56576, User, 56576)));

        Assert.True(SteamReadiness.WaitForFreshSignIn(read, TimeSpan.FromSeconds(60), clock.Sleep, () => clock.Now));
        Assert.True(clock.Now - start >= TimeSpan.FromSeconds(3.07));
    }

    [Fact]
    public void After_a_forced_close_the_left_over_account_does_not_release_the_wait()
    {
        // Measured 2026-11-24 after taskkill /F: the old pid and account stay in the registry while the new client runs.
        var clock = new Clock();
        var start = clock.Now;
        var read = Trace(clock,
            (0, S(56576, User)),
            (0.08, S(56576, User, 51932)),     // old values, new client: the 1.0.28.1 guard launched here
            (1.42, S(51932, User, 51932)),     // pid now matches, account still the old one
            (1.54, S(51932, 0, 51932)),        // Steam resets it
            (2.79, S(51932, User, 51932)));    // the real sign-in

        Assert.True(SteamReadiness.WaitForFreshSignIn(read, TimeSpan.FromSeconds(60), clock.Sleep, () => clock.Now));
        Assert.True(clock.Now - start >= TimeSpan.FromSeconds(2.79));
    }

    [Fact]
    public void A_client_that_never_signs_in_times_out_false()
    {
        var clock = new Clock();
        var start = clock.Now;

        Assert.False(SteamReadiness.WaitForFreshSignIn(() => S(51932, 0, 51932), TimeSpan.FromSeconds(60), clock.Sleep, () => clock.Now));
        Assert.True(clock.Now - start >= TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Left_over_values_that_never_reset_time_out_false_rather_than_launch()
    {
        var clock = new Clock();
        Assert.False(SteamReadiness.WaitForFreshSignIn(() => S(51932, User, 51932), TimeSpan.FromSeconds(10), clock.Sleep, () => clock.Now));
    }
}
