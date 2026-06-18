using System.IO;
using ModManager.Core;
using ModManager.Core.Plugins;
using ModManager.Plugins.Abstractions;

namespace ModManager.App.Services;

/// <summary>
/// The debounced Nexus auto-check, modeled on <see cref="UpdateChecker"/>. On game load — when the
/// auto-check setting is on, a Nexus <see cref="IModSource"/> plugin is loaded, Nexus is connected, the
/// game has a domain, and it's been more than 24h since the last poll for this game — it makes one bulk
/// <c>updated.json</c> window call, narrows to the mods that actually changed since their baseline
/// (<see cref="NexusRefresh.SelectCandidates"/>), refreshes only those by id, persists, and stamps the
/// poll time.
///
/// <para><b>Routes through the loaded plugin's <see cref="IModSource"/></b> (resolved from the shared
/// <see cref="ModSourceRegistry"/>) — not Core's <c>NexusClient</c>. The window is fetched via
/// <see cref="IModSource.GetRecentlyUpdatedAsync"/>; the candidates are refreshed via
/// <see cref="NexusRefresh.RefreshAllAsync"/> (selective <c>Overlay</c> — stats + upstream version
/// only, the heart preserved). When no plugin is loaded (STORE flavor / zero-plugins) the source is
/// null and the auto-check is a clean no-op.</para>
///
/// <para>Comfort, not load-bearing: every failure (offline, 429, bad data) is swallowed and logged
/// via <see cref="AppDiagnostics"/>. The auto-check can never break a working session — exactly like
/// the manifest feed. The per-game stamp lives at
/// <c>%LOCALAPPDATA%\ModManagerBuilder\last-nexus-poll-&lt;gameId&gt;.txt</c> (mirrors the
/// update-check stamp). The stamp is written only on a clean sweep — a rate limit (either from the
/// window call or mid-refresh) leaves it untouched so the throttled/partial poll retries next
/// launch.</para>
/// </summary>
public sealed class NexusUpdatePoll
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromHours(24);

    private static string StampPath(string gameId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModManagerBuilder",
        $"last-nexus-poll-{Sanitize(gameId)}.txt");

    // Game ids are slugs already, but keep the stamp filename safe regardless of what's in the id.
    private static string Sanitize(string id)
    {
        var chars = id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    /// <summary>
    /// Run the debounced auto-check for the active game, routed through the loaded Nexus
    /// <paramref name="source"/>. Returns true when something was refreshed and written (so the caller
    /// can reload rows to surface UPDATE chips); false when the check was skipped (no source / setting
    /// off / not connected / no domain / not due) or nothing changed. Never throws.
    /// </summary>
    public async Task<bool> MaybePollAsync(GameContext ctx, IModSource? source, NexusService nexus, AppSettingsService settings)
    {
        try
        {
            if (source is null) return false;           // STORE flavor / no plugin loaded — clean no-op
            if (!settings.AutoCheckModUpdates) return false;
            if (!nexus.IsConnected) return false;

            var domain = NexusDomains.Effective(ctx.Game);
            if (string.IsNullOrWhiteSpace(domain)) return false;

            var stampPath = StampPath(ctx.Game.Id);
            var last = NexusPollStamp.Read(stampPath);
            var now = DateTime.UtcNow;
            if (!NexusPollStamp.ShouldPoll(last, now, DebounceWindow)) return false;

            // The window scales with how long it's been since we last polled, so a long gap still
            // catches mid-range updates. Updates older than the window are caught by the manual sweep.
            var elapsed = last is { } l ? now - l : TimeSpan.FromDays(365);
            var period = NexusRefresh.PeriodFor(elapsed);

            IReadOnlyList<SourceUpdateEntry> updated;
            try
            {
                updated = await source.GetRecentlyUpdatedAsync(domain!, period);
            }
            catch (SourceRateLimitException)
            {
                // Rate-limited before we even read the window — we didn't poll, so leave the stamp
                // untouched and try again next launch.
                return false;
            }

            var byKey = Scanner.LoadMetadata(ctx);
            // Baseline for "did this land after I last looked" = the last poll time (per-mod
            // InstalledUtc still wins inside SelectCandidates when it's set).
            var baseline = last ?? DateTime.MinValue.ToUniversalTime();
            var candidates = NexusRefresh.SelectCandidates(byKey.Values, updated, baseline);

            if (candidates.Count == 0)
            {
                // Nothing to refresh, but we did successfully poll — stamp so we don't re-poll for 24h.
                NexusPollStamp.Write(stampPath, now);
                return false;
            }

            var result = await NexusRefresh.RefreshAllAsync(
                candidates, domain!, source,
                throttle: () => Task.Delay(120));

            bool wroteAnything = false;
            if (result.Updated.Count > 0)
            {
                // Map each refreshed meta back to its on-disk key by re-resolving the (deterministic)
                // id — identity fields survive the refresh, so the lookup is exact.
                var keyById = new Dictionary<int, string>();
                foreach (var kv in byKey)
                    if (NexusRefresh.ResolveModId(kv.Value) is { } id)
                        keyById[id] = kv.Key;

                var writes = new List<(string, ModMeta)>();
                foreach (var meta in result.Updated)
                    if (NexusRefresh.ResolveModId(meta) is { } id && keyById.TryGetValue(id, out var key))
                        writes.Add((key, meta));

                if (writes.Count > 0)
                {
                    Scanner.WriteManyMeta(ctx, writes);
                    wroteAnything = true;
                }
            }

            // Stamp only when we didn't get rate-limited — a 429 means we didn't fully poll the window,
            // so leave the stamp untouched and try again next launch.
            if (!result.RateLimited) NexusPollStamp.Write(stampPath, now);

            return wroteAnything;
        }
        catch (Exception ex)
        {
            // Comfort, not load-bearing — never surface a crash from the auto-check.
            AppDiagnostics.Log("nexus-auto-check", ex);
            return false;
        }
    }
}
