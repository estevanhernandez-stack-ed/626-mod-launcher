namespace ModManager.Core.Recency;

/// <summary>
/// One 626-observed launch session for a game: when it started, and — if we saw the process
/// end — when it did. Persisted append-only (see App-side LaunchLog) as camelCase JSON via
/// AtomicJson. EndedUtc is null until (if ever) the session close is observed; playtime sums
/// only entries where both timestamps are present.
/// </summary>
public sealed record LaunchLogEntry(string GameId, DateTime StartedUtc, DateTime? EndedUtc);
