namespace ModManager.Core;

/// <summary>What the Steam client and its registry record say right now: the running <c>steam.exe</c> process ids,
/// and <c>HKCU\Software\Valve\Steam\ActiveProcess</c>'s <c>pid</c> and <c>ActiveUser</c>.</summary>
public sealed record SteamState(IReadOnlyCollection<int> ClientPids, int RegistryPid, int ActiveUser)
{
    /// <summary>The registry was written by a client that is running now, not left behind by an earlier one.</summary>
    public bool OwnedByRunningClient => RegistryPid > 0 && ClientPids.Contains(RegistryPid);
}

/// <summary>
/// When Steam can answer a game's ownership check. The client process existing is not enough: measured on
/// 2026-11-11, steam.exe appears at 0.1 s and the account signs in at 3.1 s, and an exe launched in between
/// (ACE COMBAT 8's offline launch, started by the launch guard) asked Steam about ownership with nobody signed
/// in and showed "you don't own this game". Steam records the signed-in account at
/// <c>HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser</c>.
///
/// <para>A non-zero account is not proof on its own (2026-11-24). After a forced close the registry keeps the old pid
/// and account. Measured on Neb: the new client ran from 0.08 s, wrote its pid at 1.42 s, reset the account to 0 at
/// 1.54 s and signed in at 2.79 s, so "process up and account non-zero" released a launch at 1 s. Ready therefore means
/// the registry belongs to the running client, and after starting Steam, the wait also needs to see the account at 0
/// under that client before it counts a sign-in. The App reads the state; the rule and the wait live here.</para>
/// </summary>
public static class SteamReadiness
{
    /// <summary>Steam that is already up: the running client owns the registry and an account is signed in. Used before
    /// deciding to start or wait. A client started a moment ago by someone else can still pass on old values for about
    /// a tenth of a second (1.42 to 1.54 s in the forced-close trace); <see cref="WaitForFreshSignIn"/> closes that for
    /// the start 626 makes itself.</summary>
    public static bool IsReady(SteamState s) => s.OwnedByRunningClient && s.ActiveUser > 0;

    /// <summary>Poll until a sign-in that happened under the running client: the account seen at 0 while that client owns
    /// the registry, then non-zero. Old values never release it, so it times out false rather than launch on them.
    /// Synchronous; the App calls it off the UI thread. The clock and sleep are passed in so it is testable without
    /// waiting.</summary>
    public static bool WaitForFreshSignIn(Func<SteamState> read, TimeSpan timeout, Action<TimeSpan> sleep, Func<DateTime> utcNow)
    {
        var deadline = utcNow() + timeout;
        var sawSignedOut = false;
        while (true)
        {
            var s = read();
            if (s.OwnedByRunningClient && s.ActiveUser == 0) sawSignedOut = true;
            if (sawSignedOut && IsReady(s)) return true;
            if (utcNow() >= deadline) return false;
            sleep(TimeSpan.FromMilliseconds(100));   // the signed-out gap measured 1.25 s; poll well inside it
        }
    }
}
