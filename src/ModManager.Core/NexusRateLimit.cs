namespace ModManager.Core;

/// <summary>
/// A snapshot of the Nexus rate-limit headers (<c>x-rl-*</c>) attached to every API response.
/// Nexus enforces ~20,000/day plus a 500/hr fallback once the daily quota is spent; these headers
/// report the remaining and total budget so the sweep can back off before it gets throttled.
/// Any header that is absent or non-numeric maps to null — the parser never throws.
/// </summary>
public sealed record NexusRateLimit(
    int? DailyRemaining,
    int? DailyLimit,
    int? HourlyRemaining,
    int? HourlyLimit)
{
    /// <summary>
    /// Reads <c>x-rl-daily-remaining</c> / <c>x-rl-daily-limit</c> / <c>x-rl-hourly-remaining</c> /
    /// <c>x-rl-hourly-limit</c> from a response-header dictionary. Header names are matched
    /// case-insensitively; missing or non-numeric values become null.
    /// </summary>
    public static NexusRateLimit Parse(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        int? Read(string name)
        {
            foreach (var (k, v) in headers)
            {
                if (!string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) continue;
                var first = v?.FirstOrDefault();
                return int.TryParse(first, out var n) ? n : (int?)null;
            }
            return null;
        }

        return new NexusRateLimit(
            DailyRemaining: Read("x-rl-daily-remaining"),
            DailyLimit: Read("x-rl-daily-limit"),
            HourlyRemaining: Read("x-rl-hourly-remaining"),
            HourlyLimit: Read("x-rl-hourly-limit"));
    }
}

/// <summary>
/// Thrown when Nexus responds with HTTP 429 (Too Many Requests). Carries the parsed
/// <see cref="NexusRateLimit"/> so callers can report remaining budget and stop a sweep
/// cleanly instead of treating the throttle as a generic transport failure.
/// </summary>
public sealed class NexusRateLimitException : Exception
{
    public NexusRateLimit RateLimit { get; }

    public NexusRateLimitException(NexusRateLimit rateLimit)
        : base("Nexus rate limit reached (HTTP 429)")
        => RateLimit = rateLimit;
}
