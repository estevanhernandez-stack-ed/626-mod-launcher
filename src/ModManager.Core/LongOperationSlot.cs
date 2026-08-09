namespace ModManager.Core;

/// <summary>
/// The one long operation the app can be running.
///
/// <para>There is a single busy ring, a single Stop button, and a single cancellation source, so
/// there can be a single owner. Guarding that per-method was the original bug: one entry point
/// checked and another did not, and since the Advanced menu items are deliberately always clickable
/// (<i>guard, don't hide</i>), starting the second during the first handed Stop only the newcomer's
/// token and let whichever finished first tear down the busy state under the other.</para>
///
/// <para>This type owns the DECISION — held or not, by whom, and what to say when a second one asks
/// — deliberately separated from the WinUI state it guards. The view model it used to live in takes
/// fourteen concrete services, so nothing could construct it and nothing could test it; a single
/// live smoke pass then found two defects, and both were decisions rather than wiring: a refusal
/// that named the wrong operation, and a claim that could latch forever. Neither needed a running
/// app to catch — only a place where they could be asserted.</para>
///
/// <para>Not thread-safe, and deliberately so: it is UI-thread state with one writer. A lock here
/// would imply a concurrency story the callers do not have.</para>
/// </summary>
public sealed class LongOperationSlot
{
    private const string Unnamed = "Another operation";

    /// <summary>True while an operation holds the slot.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>What is holding it, for the refusal. Empty when free.</summary>
    public string HolderName { get; private set; } = "";

    /// <summary>
    /// What to tell the user when their click is refused — naming the operation that is RUNNING, not
    /// the one they attempted.
    ///
    /// <para>Naming the attempted one produces "Identify is already running" in response to clicking
    /// Identify, which reads as a glitch rather than an explanation of what is in the way.</para>
    /// </summary>
    public string RefusalMessage => IsHeld
        ? $"{(HolderName.Length == 0 ? Unnamed : HolderName)} is already running — let it finish, or press Stop."
        : "";

    /// <summary>Take the slot, or refuse. A refusal leaves the current holder completely untouched.</summary>
    public bool TryClaim(string name)
    {
        if (IsHeld) return false;
        HolderName = name;
        IsHeld = true;
        return true;
    }

    /// <summary>Hand the slot back. Safe to call when nothing holds it — this belongs in a
    /// <c>finally</c>, which also runs on paths that never claimed (an early return, or a guard that
    /// threw on the way in). A slot left claimed silently disables every long action until the app
    /// restarts, so the release path must never be the thing that fails.</summary>
    public void Release()
    {
        IsHeld = false;
        HolderName = "";
    }
}
