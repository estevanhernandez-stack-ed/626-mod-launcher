namespace ModManager.Core;

/// <summary>
/// When Steam can answer a game's ownership check. The client process existing is not enough: measured on
/// 2026-11-11, steam.exe appears at 0.1 s and the account signs in at 3.1 s, and an exe launched in between
/// (ACE COMBAT 8's offline launch, started by the launch guard) asked Steam about ownership with nobody signed
/// in and showed "you don't own this game". Steam records the signed-in account at
/// <c>HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser</c>, 0 until sign-in and 0 again after shutdown, so
/// ready is the process plus a non-zero account. The App reads both; the rule and the wait live here.
/// </summary>
public static class SteamReadiness
{
    /// <summary>The client is running and an account is signed in. A non-zero account with no client is a
    /// value left behind by a crash, not a Steam that can answer.</summary>
    public static bool IsReady(bool processRunning, int activeUser) => processRunning && activeUser > 0;

    /// <summary>Poll until <see cref="IsReady"/> or <paramref name="timeout"/>. Synchronous; the App calls it
    /// off the UI thread. The clock and sleep are passed in so the wait is testable without waiting.</summary>
    public static bool WaitUntilReady(Func<bool> processRunning, Func<int> activeUser, TimeSpan timeout,
        Action<TimeSpan> sleep, Func<DateTime> utcNow)
    {
        var deadline = utcNow() + timeout;
        while (true)
        {
            if (IsReady(processRunning(), activeUser())) return true;
            if (utcNow() >= deadline) return false;
            sleep(TimeSpan.FromMilliseconds(250));
        }
    }
}
