namespace ModManager.Core;

/// <summary>
/// Keeps an answer on the status line long enough to read.
///
/// <para>The status line has two kinds of writer. AMBIENT ones — a progress counter — fire many
/// times a second while an operation runs. ANSWERS — a refusal, an error, an outcome — are written
/// once, in response to something the user just did. Without arbitration the ambient writer wins by
/// sheer frequency: live smoke found a refusal ("Identify is already running…") flashing and being
/// erased by the running search's own counter before it could be read, which makes a correct guard
/// look like a dead click.</para>
///
/// <para>An answer takes a short hold; ambient writes defer until it lapses. There is no timer —
/// the hold expires by being asked, so a progress tick arriving after the window simply proceeds and
/// nothing has to fire to release the line.</para>
///
/// <para>Deliberately does NOT hold the text. Which string is current belongs to the view model's
/// bindable property; what belongs here is the decision about whether a low-priority writer may
/// speak yet — the part worth testing, and the part that had no home before.</para>
/// </summary>
public sealed class StatusHold
{
    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset _until = DateTimeOffset.MinValue;

    /// <param name="now">Clock seam for tests; defaults to the system clock.</param>
    public StatusHold(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

    /// <summary>True when a low-priority writer may put its text on the line.</summary>
    public bool AmbientAllowed => _now() >= _until;

    /// <summary>Reserve the line for <paramref name="duration"/>. Never shortens a longer hold that
    /// is already running — a second, briefer answer must not cut the first one off mid-read.</summary>
    public void Hold(TimeSpan duration)
    {
        var until = _now() + duration;
        if (until > _until) _until = until;
    }

    /// <summary>Release the line immediately. For news that supersedes the held answer rather than
    /// competing with it — an operation's outcome is newer information about the very thing the user
    /// asked for, not chatter to be deferred.</summary>
    public void Clear() => _until = DateTimeOffset.MinValue;
}
