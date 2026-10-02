using System.Collections.Concurrent;
using ModManager.Core.Discovery;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Discovery;

// B2: a swept candidate the name index could not place gets a live name search, capped per run, and
// the cap and any throttle are said out loud. Same engine as the row search (LooseIdentify).
public class DiscoveryNameSearchTests
{
    private static DiscoveryCandidate Swept(string file, DiscoveryKind kind = DiscoveryKind.EngineShaped)
        => new($"mods/{file}", file, kind);

    private static SourceSearchHit Hit(string name, int modId = 77) =>
        new("cyberpunk2077", modId, name, "SomeAuthor", "a summary", 250, "https://nexusmods.com/x/" + modId);

    private static Task<IReadOnlyList<SourceSearchHit>> Hits(params SourceSearchHit[] hits)
        => Task.FromResult<IReadOnlyList<SourceSearchHit>>(hits);

    [Fact]
    public async Task An_unplaced_candidate_is_named_by_the_live_search_and_the_rest_keep_their_order()
    {
        var proposals = new[]
        {
            AdoptionProposal.Unidentified(Swept("QuietFootsteps_P.pak")),
            AdoptionProposal.FromIndex(Swept("FasterShips.pak"), new ModNameIndexEntry(1, "Faster Ships", null, null)),
            AdoptionProposal.Unidentified(Swept("zzz_nothing_like_it.pak")),
        };

        var r = await DiscoveryNameSearch.NameAsync(proposals,
            q => q.Contains("Quiet", StringComparison.OrdinalIgnoreCase) ? Hits(Hit("Quiet Footsteps")) : Hits(),
            cap: 10);

        Assert.Equal(new[] { AdoptionEvidence.NameSearch, AdoptionEvidence.NameIndex, AdoptionEvidence.None },
            r.Proposals.Select(p => p.Evidence));
        Assert.Equal("Quiet Footsteps", r.Proposals[0].Title);
        Assert.Equal((2, 2, 1, 0, false), (r.Worth, r.Searched, r.Named, r.LeftByCap, r.RateLimited));
        Assert.Equal("Quiet Footsteps", Assert.Single(r.Hits).Name);   // for the caller to grow the index
        Assert.Null(DiscoveryNameSearch.Note(r, auto: true));
    }

    [Fact]
    public async Task Index_hits_md5_hits_and_proxy_loaders_are_never_searched()
    {
        var asked = new ConcurrentBag<string>();
        var proposals = new[]
        {
            AdoptionProposal.FromIndex(Swept("FasterShips.pak"), new ModNameIndexEntry(1, "Faster Ships", null, null)),
            AdoptionProposal.Unidentified(Swept("version.dll", DiscoveryKind.ProxyLoader)),
        };

        var r = await DiscoveryNameSearch.NameAsync(proposals, q => { asked.Add(q); return Hits(Hit(q)); }, cap: 10);

        Assert.Empty(asked);
        Assert.Same(proposals, r.Proposals);
        Assert.Equal(0, r.Worth);
    }

    [Fact]
    public async Task The_cap_limits_who_is_asked_and_says_how_many_were_left()
    {
        var asked = new ConcurrentBag<string>();
        var names = new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf" };
        var proposals = names.Select(n => AdoptionProposal.Unidentified(Swept($"{n}Tweaks.pak"))).ToList();

        var r = await DiscoveryNameSearch.NameAsync(proposals, q => { asked.Add(q); return Hits(); }, cap: 3, maxConcurrency: 1);

        // The FIRST three, in list order, and nobody past them: a capped candidate costs nothing.
        foreach (var n in names[..3]) Assert.Contains(asked, q => q.Contains(n, StringComparison.OrdinalIgnoreCase));
        foreach (var n in names[3..]) Assert.DoesNotContain(asked, q => q.Contains(n, StringComparison.OrdinalIgnoreCase));
        Assert.Equal((7, 3, 4), (r.Worth, r.Searched, r.LeftByCap));
        Assert.All(r.Proposals, p => Assert.Equal(AdoptionEvidence.None, p.Evidence));   // listed, not dropped
        Assert.Equal(7, r.Proposals.Count);

        Assert.Equal("Searched Nexus by name for 3 of 7 unnamed finds; Identify my mods searches more.",
            DiscoveryNameSearch.Note(r, auto: true));
        Assert.Equal("Searched Nexus by name for the first 3 of 7 unnamed finds; the rest are listed as not identified.",
            DiscoveryNameSearch.Note(r, auto: false));
    }

    [Fact]
    public async Task A_rate_limit_stops_the_run_and_is_said_rather_than_read_as_misses()
    {
        var calls = 0;
        var proposals = Enumerable.Range(1, 6)
            .Select(i => AdoptionProposal.Unidentified(Swept($"Thing{i}.pak")))
            .ToList();

        var r = await DiscoveryNameSearch.NameAsync(proposals, q =>
        {
            if (Interlocked.Increment(ref calls) > 2) throw new SourceRateLimitException();
            return Hits();
        }, cap: 10, maxConcurrency: 1);

        Assert.True(r.RateLimited);
        Assert.True(r.Searched < r.Worth);
        var note = DiscoveryNameSearch.Note(r, auto: false);
        Assert.NotNull(note);
        Assert.Contains("rate-limited", note);
        Assert.Contains($"after {r.Searched} of 6", note);
    }

    [Fact]
    public async Task A_search_that_throws_costs_only_its_own_candidate()
    {
        var proposals = new[]
        {
            AdoptionProposal.Unidentified(Swept("Broken Mod.pak")),
            AdoptionProposal.Unidentified(Swept("QuietFootsteps_P.pak")),
        };

        var r = await DiscoveryNameSearch.NameAsync(proposals, q =>
            q.Contains("Broken", StringComparison.Ordinal) ? throw new HttpRequestException("boom")
            : q.Contains("Quiet", StringComparison.OrdinalIgnoreCase) ? Hits(Hit("Quiet Footsteps"))
            : Hits(), cap: 10);

        Assert.Equal(AdoptionEvidence.None, r.Proposals[0].Evidence);
        Assert.Equal(AdoptionEvidence.NameSearch, r.Proposals[1].Evidence);
        Assert.False(r.RateLimited);
    }

    [Fact]
    public async Task A_cap_of_zero_asks_nothing()
    {
        var called = false;
        var proposals = new[] { AdoptionProposal.Unidentified(Swept("QuietFootsteps_P.pak")) };

        var r = await DiscoveryNameSearch.NameAsync(proposals, q => { called = true; return Hits(); }, cap: 0);

        Assert.False(called);
        Assert.Equal((1, 0, 1), (r.Worth, r.Searched, r.LeftByCap));
    }

    [Fact]
    public void The_auto_cap_is_smaller_than_the_requested_one()
        => Assert.True(DiscoveryNameSearch.AutoCap < DiscoveryNameSearch.RequestedCap);

    // Review on #367: a vanilla Data/Skyrim.esm cleans to the one-word query "Skyrim", which scores
    // exactly 0.5 against "Skyrim Together". The index already refuses that; the live search must too.
    [Fact]
    public async Task A_one_word_filename_is_never_fuzzy_matched()
    {
        var proposals = new[] { AdoptionProposal.Unidentified(Swept("Skyrim.esm", DiscoveryKind.Signature)) };

        var r = await DiscoveryNameSearch.NameAsync(proposals, q => Hits(Hit("Skyrim Together")), cap: 10);

        Assert.Equal(AdoptionEvidence.None, r.Proposals[0].Evidence);
        Assert.Equal(0, r.Named);
    }

    [Fact]
    public async Task A_one_word_filename_still_matches_a_mod_with_exactly_that_name()
    {
        var proposals = new[] { AdoptionProposal.Unidentified(Swept("Ragnarok.pak")) };

        var r = await DiscoveryNameSearch.NameAsync(proposals, q => Hits(Hit("Ragnarok Mod"), Hit("Ragnarok", 5)), cap: 10);

        Assert.Equal(5, r.Proposals[0].ModId);
    }

    // Review on #367: a search that throws (a timeout) was indistinguishable from "Nexus has no such
    // mod". It is counted and said.
    [Fact]
    public async Task Searches_that_fail_are_counted_and_said_not_read_as_misses()
    {
        var proposals = new[]
        {
            AdoptionProposal.Unidentified(Swept("Slow Mod One.pak")),
            AdoptionProposal.Unidentified(Swept("Slow Mod Two.pak")),
        };

        var r = await DiscoveryNameSearch.NameAsync(proposals, q => throw new TimeoutException(), cap: 10);

        Assert.Equal((2, 2), (r.Searched, r.Failed));
        Assert.Equal("2 couldn't be searched because Nexus didn't answer in time.", DiscoveryNameSearch.Note(r, auto: true));
    }

    // Review on #367: a stopped run reported the CAP as the number searched.
    [Fact]
    public async Task A_stopped_run_reports_what_it_asked_not_what_the_cap_allowed()
    {
        using var cts = new CancellationTokenSource();
        var proposals = new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo" }
            .Select(n => AdoptionProposal.Unidentified(Swept($"{n}Tweaks.pak"))).ToList();

        var r = await DiscoveryNameSearch.NameAsync(proposals, q =>
        {
            if (q.Contains("Bravo", StringComparison.OrdinalIgnoreCase)) cts.Cancel();
            return Hits();
        }, cap: 10, maxConcurrency: 1, ct: cts.Token);

        Assert.Equal(2, r.Searched);
        Assert.Equal("The name search stopped after 2 of 5 unnamed finds; the rest are listed as not identified.",
            DiscoveryNameSearch.Note(r, auto: false));
    }
}
