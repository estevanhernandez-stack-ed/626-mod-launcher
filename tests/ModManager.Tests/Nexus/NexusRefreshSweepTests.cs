using ModManager.Core;

namespace ModManager.Tests.Nexus;

// The two ways the RefreshOne primitive is fed: a full manual sweep (RefreshAllAsync) and the
// auto-check's narrowing (SelectCandidates + PeriodFor). Period is chosen by elapsed time; the
// sweep is rate-limit-aware (stops on a 429 and reports partial progress, never throws).
public class NexusRefreshSweepTests
{
    private sealed class FakeNexus : INexusClient
    {
        private readonly Func<string, int, Task<ModMeta?>> _getMod;
        private readonly Func<Task<IReadOnlyList<NexusEndorsement>>>? _getEndorsements;
        public int GetModCalls { get; private set; }
        public int GetEndorsementsCalls { get; private set; }

        public FakeNexus(
            Func<string, int, Task<ModMeta?>> getMod,
            Func<Task<IReadOnlyList<NexusEndorsement>>>? getEndorsements = null)
        {
            _getMod = getMod;
            _getEndorsements = getEndorsements;
        }

        public Task<ModMeta?> GetModAsync(string gameDomain, int modId)
        {
            GetModCalls++;
            return _getMod(gameDomain, modId);
        }

        public Task<NexusMd5Match?> GetByMd5Async(string gameDomain, string md5) => throw new NotSupportedException();
        public Task<NexusUser?> ValidateAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<NexusUpdateEntry>> GetRecentlyUpdatedAsync(string d, string p) => throw new NotSupportedException();
        public Task<EndorseOutcome> EndorseAsync(string d, int id, string v, EndorseAction a) => throw new NotSupportedException();

        public Task<IReadOnlyList<NexusEndorsement>> GetUserEndorsementsAsync()
        {
            GetEndorsementsCalls++;
            return _getEndorsements is null
                ? Task.FromResult<IReadOnlyList<NexusEndorsement>>(Array.Empty<NexusEndorsement>())
                : _getEndorsements();
        }

        public NexusRateLimit? LastRateLimit => null;
    }

    private static Task NoDelay() => Task.CompletedTask;

    // ---------- PeriodFor ----------

    [Theory]
    [InlineData(0, "1d")]            // just now
    [InlineData(23, "1d")]           // under a day
    [InlineData(25, "1w")]           // over a day, under a week
    [InlineData(24 * 6, "1w")]       // 6 days
    [InlineData(24 * 8, "1m")]       // over a week
    [InlineData(24 * 60, "1m")]      // way over
    public void PeriodFor_picks_window_by_elapsed(int hours, string expected)
    {
        Assert.Equal(expected, NexusRefresh.PeriodFor(TimeSpan.FromHours(hours)));
    }

    // ---------- SelectCandidates ----------

    [Fact]
    public void SelectCandidates_keeps_only_ids_in_set_with_newer_file_update()
    {
        var baseline = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        long After(int days) => ((DateTimeOffset)baseline.AddDays(days)).ToUnixTimeSeconds();
        long Before(int days) => ((DateTimeOffset)baseline.AddDays(-days)).ToUnixTimeSeconds();

        var metas = new[]
        {
            new ModMeta { Title = "changed", NexusModId = 1 },     // in set, newer -> candidate
            new ModMeta { Title = "stale", NexusModId = 2 },       // in set, but older update -> skip
            new ModMeta { Title = "absent", NexusModId = 3 },      // not in set -> skip
            new ModMeta { Title = "no-id", Url = "nope" },         // unresolvable -> skip
        };
        var entries = new[]
        {
            new NexusUpdateEntry(1, After(2), After(2)),
            new NexusUpdateEntry(2, Before(2), Before(2)),
            new NexusUpdateEntry(99, After(5), After(5)),          // unrelated id
        };

        var candidates = NexusRefresh.SelectCandidates(metas, entries, baseline);

        Assert.Single(candidates);
        Assert.Equal("changed", candidates[0].Title);
    }

    [Fact]
    public void SelectCandidates_uses_each_mods_InstalledUtc_over_the_passed_baseline()
    {
        var passedBaseline = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var installedLater = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        long update = ((DateTimeOffset)new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();

        // Update on 2024-03-01: newer than the passed baseline (Jan) but older than this mod's own
        // InstalledUtc (Jun) -> not a candidate (you installed it after the update landed).
        var metas = new[] { new ModMeta { Title = "post-install", NexusModId = 1, InstalledUtc = installedLater } };
        var entries = new[] { new NexusUpdateEntry(1, update, update) };

        var candidates = NexusRefresh.SelectCandidates(metas, entries, passedBaseline);

        Assert.Empty(candidates);
    }

    [Fact]
    public void SelectCandidates_resolves_id_from_url()
    {
        var baseline = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        long after = ((DateTimeOffset)baseline.AddDays(2)).ToUnixTimeSeconds();
        var metas = new[] { new ModMeta { Title = "url-id", Url = "https://www.nexusmods.com/eldenring/mods/555" } };
        var entries = new[] { new NexusUpdateEntry(555, after, after) };

        var candidates = NexusRefresh.SelectCandidates(metas, entries, baseline);

        Assert.Single(candidates);
    }

    // ---------- RefreshAllAsync ----------

    [Fact]
    public async Task RefreshAllAsync_refreshes_each_resolvable_mod_and_counts_updates()
    {
        var metas = new[]
        {
            new ModMeta { Title = "a", NexusModId = 1, Version = "1.0" },   // upstream 2.0 -> update
            new ModMeta { Title = "b", NexusModId = 2, Version = "3.0" },   // upstream 3.0 -> no update
            new ModMeta { Title = "c", Url = "nope" },                      // unresolvable -> skipped
        };
        var fake = new FakeNexus((_, id) => Task.FromResult<ModMeta?>(id switch
        {
            1 => new ModMeta { Version = "2.0", EndorsementCount = 50 },
            2 => new ModMeta { Version = "3.0", EndorsementCount = 9 },
            _ => null,
        }));

        var result = await NexusRefresh.RefreshAllAsync(metas, "eldenring", fake, NoDelay);

        Assert.False(result.RateLimited);
        Assert.Equal(2, result.Refreshed);
        Assert.Equal(1, result.UpdatesAvailable);
        Assert.Equal(2, result.Updated.Count);
        Assert.Equal(2, fake.GetModCalls);
        Assert.Equal("2.0", result.Updated.Single(m => m.NexusModId == 1).NexusLatestVersion);
    }

    [Fact]
    public async Task RefreshAllAsync_stops_on_rate_limit_and_returns_partial_progress()
    {
        var metas = new[]
        {
            new ModMeta { Title = "a", NexusModId = 1, Version = "1.0" },
            new ModMeta { Title = "b", NexusModId = 2, Version = "1.0" },   // this call 429s
            new ModMeta { Title = "c", NexusModId = 3, Version = "1.0" },   // never reached
        };
        var fake = new FakeNexus((_, id) =>
        {
            if (id == 2) throw new NexusRateLimitException(new NexusRateLimit(0, 100, 0, 50));
            return Task.FromResult<ModMeta?>(new ModMeta { Version = "2.0" });
        });

        var result = await NexusRefresh.RefreshAllAsync(metas, "eldenring", fake, NoDelay);

        Assert.True(result.RateLimited);
        Assert.Equal(1, result.Refreshed);          // only the first landed
        Assert.Equal(1, result.UpdatesAvailable);
        Assert.Single(result.Updated);
        Assert.Equal(2, fake.GetModCalls);          // stopped at the 429, never reached #3
    }

    [Fact]
    public async Task RefreshAllAsync_empty_input_is_zero_result()
    {
        var fake = new FakeNexus((_, _) => Task.FromResult<ModMeta?>(null));
        var result = await NexusRefresh.RefreshAllAsync(Array.Empty<ModMeta>(), "eldenring", fake, NoDelay);

        Assert.False(result.RateLimited);
        Assert.Equal(0, result.Refreshed);
        Assert.Equal(0, result.UpdatesAvailable);
        Assert.Empty(result.Updated);
        Assert.Equal(0, fake.GetModCalls);
    }

    // ---------- RefreshAllAsync + endorsement sweep (Task 5) ----------

    [Fact]
    public async Task RefreshAllAsync_fetches_endorsements_once_and_applies_to_returned_metas()
    {
        var metas = new[]
        {
            new ModMeta { Title = "a", NexusModId = 1, Version = "1.0" },   // endorsed on Nexus
            new ModMeta { Title = "b", NexusModId = 2, Version = "3.0" },   // abstained on Nexus
        };
        var fake = new FakeNexus(
            (_, id) => Task.FromResult<ModMeta?>(id switch
            {
                1 => new ModMeta { Version = "2.0", EndorsementCount = 50 },
                2 => new ModMeta { Version = "3.0", EndorsementCount = 9 },
                _ => null,
            }),
            () => Task.FromResult<IReadOnlyList<NexusEndorsement>>(new[]
            {
                new NexusEndorsement(1, "eldenring", "Endorsed"),
                new NexusEndorsement(2, "eldenring", "Abstained"),
            }));

        var result = await NexusRefresh.RefreshAllAsync(metas, "eldenring", fake, NoDelay);

        // The stats sweep is unchanged.
        Assert.False(result.RateLimited);
        Assert.Equal(2, result.Refreshed);
        Assert.Equal(1, result.UpdatesAvailable);
        Assert.Equal(2, result.Updated.Count);
        Assert.Equal("2.0", result.Updated.Single(m => m.NexusModId == 1).NexusLatestVersion);

        // Exactly one bulk endorsements call, folded onto the returned metas.
        Assert.Equal(1, fake.GetEndorsementsCalls);
        Assert.True(result.Updated.Single(m => m.NexusModId == 1).Endorsed);
        Assert.False(result.Updated.Single(m => m.NexusModId == 2).Endorsed);
    }

    [Fact]
    public async Task RefreshAllAsync_endorsements_failure_does_not_abort_the_stats_sweep()
    {
        var metas = new[]
        {
            new ModMeta { Title = "a", NexusModId = 1, Version = "1.0", Endorsed = true },  // previously endorsed on disk
            new ModMeta { Title = "b", NexusModId = 2, Version = "3.0" },                   // unknown on disk
        };
        var fake = new FakeNexus(
            (_, id) => Task.FromResult<ModMeta?>(id switch
            {
                1 => new ModMeta { Version = "2.0", EndorsementCount = 50 },
                2 => new ModMeta { Version = "3.0", EndorsementCount = 9 },
                _ => null,
            }),
            () => throw new HttpRequestException("offline"));

        var result = await NexusRefresh.RefreshAllAsync(metas, "eldenring", fake, NoDelay);

        // Endorsements were attempted (and swallowed) — the stats refresh still completes intact.
        Assert.Equal(1, fake.GetEndorsementsCalls);
        Assert.False(result.RateLimited);
        Assert.Equal(2, result.Refreshed);
        Assert.Equal(1, result.UpdatesAvailable);
        Assert.Equal(2, result.Updated.Count);

        // The bulk call failed, so no fresh endorsement state was applied. The refreshed metas are
        // persisted wholesale, so the PRE-EXISTING Endorsed value (user intent) must survive the
        // sweep — a dropped value here would silently un-fill the heart on disk on any offline
        // refresh. Carried through; never recomputed-or-wiped.
        Assert.True(result.Updated.Single(m => m.NexusModId == 1).Endorsed);   // preserved, not wiped to null
        Assert.Null(result.Updated.Single(m => m.NexusModId == 2).Endorsed);   // never set — stays unknown
    }

    [Fact]
    public async Task RefreshAllAsync_endorsement_rate_limit_does_not_abort_the_stats_sweep()
    {
        var metas = new[] { new ModMeta { Title = "a", NexusModId = 1, Version = "1.0", Endorsed = true } };
        var fake = new FakeNexus(
            (_, _) => Task.FromResult<ModMeta?>(new ModMeta { Version = "2.0" }),
            () => throw new NexusRateLimitException(new NexusRateLimit(0, 100, 0, 50)));

        var result = await NexusRefresh.RefreshAllAsync(metas, "eldenring", fake, NoDelay);

        // A 429 from the endorsements call is best-effort — it must not flip RateLimited (that flag
        // is the stats-sweep's own throttle signal) nor drop the refreshed stats.
        Assert.False(result.RateLimited);
        Assert.Equal(1, result.Refreshed);
        Assert.Single(result.Updated);

        // Same preservation law as the offline case: the swallowed 429 left the bulk apply unrun, so
        // the previously-endorsed value (user intent) must survive the wholesale persist, not get
        // wiped to null.
        Assert.True(result.Updated[0].Endorsed);   // preserved through the failed best-effort call
    }
}
