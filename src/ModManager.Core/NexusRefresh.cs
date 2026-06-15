namespace ModManager.Core;

/// <summary>
/// The outcome of a sweep / candidate refresh: how many mods were re-fetched, how many of those
/// now show a newer Nexus version, whether the sweep stopped early on a rate limit, and the
/// refreshed <see cref="ModMeta"/> entries (to persist). On a 429 mid-sweep, <see cref="RateLimited"/>
/// is true and the counts/list reflect only the partial progress made before the throttle.
/// </summary>
public sealed record NexusRefreshResult(
    int Refreshed,
    int UpdatesAvailable,
    bool RateLimited,
    IReadOnlyList<ModMeta> Updated);

/// <summary>
/// The per-mod Nexus refresh primitive: poll Nexus <em>by mod id</em> (no archive required) to
/// refresh live stats and capture the current upstream version, while preserving the installed
/// side of the update compare. One primitive (<see cref="RefreshOneAsync"/>) feeds two paths —
/// the manual sweep and the debounced auto-check.
///
/// <para>The mod id is recovered two ways: the stored <see cref="ModMeta.NexusModId"/> (set by
/// Backfill) or, failing that, parsed from the stored <see cref="ModMeta.Url"/> (Nexus only).</para>
///
/// <para>Update-available is never trusted from disk: it is computed on the in-memory
/// <see cref="Mod"/> as <c>NexusLatestVersion is not null &amp;&amp; NexusLatestVersion != Version</c>.</para>
/// </summary>
public static class NexusRefresh
{
    /// <summary>
    /// Resolve the Nexus mod id for an entry: the stored <see cref="ModMeta.NexusModId"/> wins;
    /// otherwise parse <see cref="ModMeta.Url"/> (Nexus mod pages only). A CurseForge URL, an
    /// unparseable URL, or no id at all returns null (the mod is skipped — no archive, no id).
    /// </summary>
    public static int? ResolveModId(ModMeta meta)
    {
        if (meta.NexusModId is { } id) return id;

        var parts = ModSiteUrl.Parse(meta.Url);
        if (parts is { Provider: ModSiteProvider.Nexus } && int.TryParse(parts.ModRef, out var parsed))
            return parsed;

        return null;
    }

    /// <summary>
    /// Refresh one mod's Nexus stats by id. Resolves the id (<see cref="ResolveModId"/>); if none,
    /// returns null without calling the client. Otherwise <c>GetMod</c> by id and overlay only the
    /// live stats + the upstream current version onto a clone of <paramref name="existing"/>:
    /// <list type="bullet">
    ///   <item>refresh <see cref="ModMeta.EndorsementCount"/>, <see cref="ModMeta.Downloads"/>,
    ///         <see cref="ModMeta.Available"/>,</item>
    ///   <item>set <see cref="ModMeta.NexusLatestVersion"/> = the fetched current version,</item>
    ///   <item><strong>preserve</strong> the installed <see cref="ModMeta.Version"/> and
    ///         <see cref="ModMeta.NexusFileId"/> (the "what you have" side), plus all identity
    ///         (Title/Author/Source/links), <see cref="ModMeta.IsManual"/>, etc.</item>
    /// </list>
    /// A <see cref="NexusRateLimitException"/> from the client propagates (the caller decides how to
    /// handle a 429 — the sweep stops and reports partial progress).
    /// </summary>
    public static async Task<ModMeta?> RefreshOneAsync(ModMeta existing, string domain, INexusClient client)
    {
        var id = ResolveModId(existing);
        if (id is null) return null;

        var fetched = await client.GetModAsync(domain, id.Value);
        if (fetched is null) return null;

        return Overlay(existing, fetched);
    }

    /// <summary>
    /// Fold the bulk user-endorsements list back onto the library: for each meta whose resolved
    /// Nexus id (<see cref="ResolveModId"/>) matches an entry in <paramref name="endorsements"/>
    /// for the active <paramref name="domain"/>, set <see cref="ModMeta.Endorsed"/> to
    /// <c>status == "Endorsed"</c> (so an <c>Abstained</c> / <c>Undecided</c> match clears it to
    /// false). Metas with no matching entry — or an unresolvable id, or a same-id entry from a
    /// different game domain — are left untouched: the bulk list only knows about mods the user has
    /// interacted with, so absence means "unknown", not "abstained". Pure (mutates the supplied
    /// metas in place); never throws.
    /// </summary>
    public static void ApplyEndorsements(
        IEnumerable<ModMeta> metas,
        IEnumerable<NexusEndorsement> endorsements,
        string domain)
    {
        // id -> status, for the active domain only (last entry wins if Nexus ever repeats an id).
        var byId = new Dictionary<int, string>();
        foreach (var e in endorsements)
            if (string.Equals(e.DomainName, domain, StringComparison.OrdinalIgnoreCase))
                byId[e.ModId] = e.Status;

        foreach (var meta in metas)
        {
            var id = ResolveModId(meta);
            if (id is null) continue;
            if (!byId.TryGetValue(id.Value, out var status)) continue;
            meta.Endorsed = string.Equals(status, "Endorsed", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Pick the <c>updated.json</c> window by how long it's been since the last poll: under a day
    /// → <c>"1d"</c>, under a week → <c>"1w"</c>, otherwise <c>"1m"</c>. Updates older than the
    /// chosen window are caught only by the full manual sweep (which does not use a window).
    /// </summary>
    public static string PeriodFor(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromDays(1)) return "1d";
        if (elapsed < TimeSpan.FromDays(7)) return "1w";
        return "1m";
    }

    /// <summary>
    /// Narrow a library down to the mods worth re-fetching for an auto-check: those whose resolved
    /// id appears in <paramref name="updatedEntries"/> <em>and</em> whose Nexus
    /// <c>latest_file_update</c> (unix seconds → UTC) is newer than the mod's baseline. The baseline
    /// is the mod's own <see cref="ModMeta.InstalledUtc"/> when set (you only care about updates that
    /// landed after you installed), otherwise <paramref name="baselineUtc"/> (the last-poll time).
    /// Unresolvable ids are skipped.
    /// </summary>
    public static IReadOnlyList<ModMeta> SelectCandidates(
        IEnumerable<ModMeta> installedMetas,
        IEnumerable<NexusUpdateEntry> updatedEntries,
        DateTime baselineUtc)
    {
        // id -> latest_file_update (keep the freshest if the feed ever repeats an id).
        var byId = new Dictionary<int, long>();
        foreach (var e in updatedEntries)
            if (!byId.TryGetValue(e.ModId, out var existing) || e.LatestFileUpdate > existing)
                byId[e.ModId] = e.LatestFileUpdate;

        var candidates = new List<ModMeta>();
        foreach (var meta in installedMetas)
        {
            var id = ResolveModId(meta);
            if (id is null) continue;
            if (!byId.TryGetValue(id.Value, out var fileUpdate)) continue;

            var baseline = meta.InstalledUtc ?? baselineUtc;
            var fileUpdateUtc = DateTimeOffset.FromUnixTimeSeconds(fileUpdate).UtcDateTime;
            if (fileUpdateUtc > baseline)
                candidates.Add(meta);
        }
        return candidates;
    }

    /// <summary>
    /// Run <see cref="RefreshOneAsync"/> over every resolvable mod in <paramref name="metas"/>,
    /// throttled by an injectable <paramref name="throttle"/> delay between calls (tests pass a
    /// no-op; the App passes a small inter-call delay well under the burst ceiling). Returns a
    /// <see cref="NexusRefreshResult"/> with the refreshed metas. If a call throws
    /// <see cref="NexusRateLimitException"/>, the sweep stops cleanly — <see cref="NexusRefreshResult.RateLimited"/>
    /// is set and the partial progress is returned, never re-thrown. Unresolvable metas are skipped
    /// (no client call, not counted).
    ///
    /// <para>After the stats sweep, the bulk user-endorsements list is fetched <em>once</em> and
    /// folded onto the refreshed metas (<see cref="ApplyEndorsements"/>) so hearts reflect reality
    /// library-wide — including mods endorsed outside the launcher — without per-mod calls. That
    /// single call is best-effort: any failure (offline, 4xx, even a 429) is swallowed so it can
    /// never sink the stats refresh. Endorsements are read-only state sync, not the
    /// <see cref="NexusRefreshResult.RateLimited"/> signal — that flag stays owned by the stats
    /// sweep's own throttle.</para>
    /// </summary>
    public static async Task<NexusRefreshResult> RefreshAllAsync(
        IEnumerable<ModMeta> metas, string domain, INexusClient client, Func<Task>? throttle = null)
    {
        var updated = new List<ModMeta>();
        int refreshed = 0, updatesAvailable = 0;
        bool rateLimited = false;
        bool first = true;

        foreach (var meta in metas)
        {
            if (ResolveModId(meta) is null) continue; // skip non-Nexus rows without a network call

            // Throttle between (not before) calls so a one-item sweep pays no delay.
            if (!first && throttle is not null) await throttle();
            first = false;

            ModMeta? result;
            try
            {
                result = await RefreshOneAsync(meta, domain, client);
            }
            catch (NexusRateLimitException)
            {
                rateLimited = true;
                break; // stop the sweep, return partial progress
            }

            if (result is null) continue;
            refreshed++;
            updated.Add(result);
            if (result.NexusLatestVersion is { } latest && latest != result.Version)
                updatesAvailable++;
        }

        // One cheap bulk call to sync endorse hearts library-wide. Best-effort: guarded in its own
        // try/catch (including a 429) so it can never abort or downgrade the stats sweep above.
        try
        {
            var endorsements = await client.GetUserEndorsementsAsync();
            ApplyEndorsements(updated, endorsements, domain);
        }
        catch
        {
            // Endorsements are read-only state sync — swallow and keep the stats result intact.
        }

        return new NexusRefreshResult(refreshed, updatesAvailable, rateLimited, updated);
    }

    /// <summary>
    /// Produce a refreshed clone: copy every field from <paramref name="existing"/> (the installed
    /// truth) then overwrite only the live-stat fields from <paramref name="fetched"/> and capture
    /// the upstream version as <see cref="ModMeta.NexusLatestVersion"/>. The installed
    /// <see cref="ModMeta.Version"/> / <see cref="ModMeta.NexusFileId"/> are never touched, and
    /// <see cref="ModMeta.Endorsed"/> (persisted user intent) is carried through verbatim — the
    /// sweep persists these clones wholesale, so a dropped <c>Endorsed</c> here would silently wipe
    /// the user's heart on disk whenever the best-effort bulk endorsements call fails.
    /// </summary>
    private static ModMeta Overlay(ModMeta existing, ModMeta fetched) => new()
    {
        // identity + installed side — preserved verbatim from existing
        Title = existing.Title,
        Description = existing.Description,
        Author = existing.Author,
        AuthorUrl = existing.AuthorUrl,
        Url = existing.Url,
        Source = existing.Source,
        Donate = existing.Donate,
        Image = existing.Image,
        CurseforgeId = existing.CurseforgeId,
        Category = existing.Category,
        IsManual = existing.IsManual,
        InstalledUtc = existing.InstalledUtc,
        SourceConfidence = existing.SourceConfidence,
        ContainsAdultContent = existing.ContainsAdultContent,
        NexusModId = existing.NexusModId,
        NexusFileId = existing.NexusFileId,   // installed file — NOT the upstream latest
        Version = existing.Version,           // installed version — the "what you have" side
        Endorsed = existing.Endorsed,         // persisted user intent (like IsManual) — preserved, never recomputed-or-wiped

        // live stats — refreshed from the fetched mod
        EndorsementCount = fetched.EndorsementCount ?? existing.EndorsementCount,
        Downloads = fetched.Downloads ?? existing.Downloads,
        Available = fetched.Available ?? existing.Available,

        // the "what's available" side of the compare
        NexusLatestVersion = fetched.Version ?? existing.NexusLatestVersion,
    };
}
