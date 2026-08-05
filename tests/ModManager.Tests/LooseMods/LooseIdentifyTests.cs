using ModManager.Core;
using ModManager.Core.LooseMods;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.LooseMods;

// Loose-root (decima / Death Stranding 2) name-search identify: propose a Nexus name-search match
// for loose-root rows the launcher hasn't identified yet, without ever touching disk or a real
// plugin — search is injected as a delegate, so this stays pure Core. Fixture mirrors the real DS2
// loose-root install (see LooseRootListingTests): three "plugin" ASIs, three "shaders" hits, two
// "loader" proxies (dxgi/version) that must never be proposed a search.
public class LooseIdentifyTests
{
    // Real DS2 loose-root rows, one per Class the launcher actually produces for this game.
    private static List<Mod> Rows() => new()
    {
        Row("Zipliner_v1.1", "plugin"),
        Row("DollmanMute", "plugin"),
        Row("DeathStranding2Fix", "plugin"),
        Row("ShaderToggler", "shaders"),
        Row("DeathStranding2UI", "shaders"),
        Row("renodx-deathstranding2", "shaders"),
        Row("dxgi", "loader"),
        Row("version", "loader"),
    };

    private static Mod Row(string @base, string cls) => new()
    {
        Name = @base,
        Base = @base,
        Class = cls,
        Location = LooseRootListing.LooseRootLocation,
        Enabled = true,
    };

    private static SourceSearchHit Hit(string name, int modId = 1) =>
        new("deathstranding2", modId, name, "SomeAuthor", "summary", 42, "https://nexusmods.com/deathstranding2/mods/" + modId);

    [Fact]
    public void Candidates_excludes_both_loader_rows_always()
    {
        var candidates = LooseIdentify.Candidates(Rows(), new Dictionary<string, ModMeta>());

        Assert.DoesNotContain(candidates, r => r.Base == "dxgi");
        Assert.DoesNotContain(candidates, r => r.Base == "version");
        Assert.All(candidates, r => Assert.NotEqual("loader", r.Class));
    }

    [Fact]
    public void Candidates_excludes_a_row_whose_meta_IsManual()
    {
        var meta = new Dictionary<string, ModMeta>
        {
            ["DollmanMute"] = new ModMeta { IsManual = true },
        };

        var candidates = LooseIdentify.Candidates(Rows(), meta);

        Assert.DoesNotContain(candidates, r => r.Base == "DollmanMute");
        // Everything else non-loader still proposed.
        Assert.Contains(candidates, r => r.Base == "Zipliner_v1.1");
    }

    [Fact]
    public void Candidates_excludes_a_row_already_identified_by_NexusModId_or_confidence()
    {
        var meta = new Dictionary<string, ModMeta>
        {
            ["ShaderToggler"] = new ModMeta { NexusModId = 12345 },
            ["DeathStranding2UI"] = new ModMeta { SourceConfidence = "fingerprint" },
        };

        var candidates = LooseIdentify.Candidates(Rows(), meta);

        Assert.DoesNotContain(candidates, r => r.Base == "ShaderToggler");
        Assert.DoesNotContain(candidates, r => r.Base == "DeathStranding2UI");
        // The unidentified shaders row is still a candidate.
        Assert.Contains(candidates, r => r.Base == "renodx-deathstranding2");
    }

    [Fact]
    public void CleanQuery_uses_the_real_CleanModName()
    {
        // NameMatch.CleanModName strips the load-order/version noise but "v1.1" splits into a
        // dropped "v1" token (matches the version regex) and a surviving bare "1" token — so the
        // real cleaned query is "Zipliner 1", not a naive "Zipliner". Assert against the real
        // helper directly so this test can never drift from NameMatch's actual behavior.
        Assert.Equal("Zipliner 1", NameMatch.CleanModName("Zipliner_v1.1"));
    }

    [Fact]
    public async Task ProposeAsync_picks_the_best_hit_at_or_above_threshold_and_null_below_it()
    {
        var candidates = new List<Mod> { Row("DollmanMute", "plugin"), Row("ShaderToggler", "shaders") };

        Task<IReadOnlyList<SourceSearchHit>> Search(string query)
        {
            IReadOnlyList<SourceSearchHit> hits = query.Contains("Dollman")
                ? new[] { Hit("Dollman Mute"), Hit("Totally Unrelated Mod", 2) }
                : new[] { Hit("Completely Different Thing", 3) }; // weak/no overlap with "Shader Toggler"
            return Task.FromResult(hits);
        }

        var proposals = await LooseIdentify.ProposeAsync(candidates, Search);

        var dollman = Assert.Single(proposals, p => p.ModKey == "DollmanMute");
        Assert.NotNull(dollman.Match);
        Assert.Equal("Dollman Mute", dollman.Match!.Name);

        var shaderToggler = Assert.Single(proposals, p => p.ModKey == "ShaderToggler");
        Assert.Null(shaderToggler.Match);
    }

    [Fact]
    public async Task ProposeAsync_never_throws_a_bad_delegate_yields_null_match_for_that_row_others_proceed()
    {
        var candidates = new List<Mod> { Row("DollmanMute", "plugin"), Row("ShaderToggler", "shaders") };

        Task<IReadOnlyList<SourceSearchHit>> Search(string query)
        {
            if (query.Contains("Dollman")) throw new InvalidOperationException("network blew up");
            IReadOnlyList<SourceSearchHit> hits = new[] { Hit("Shader Toggler") };
            return Task.FromResult(hits);
        }

        var proposals = await LooseIdentify.ProposeAsync(candidates, Search);

        var dollman = Assert.Single(proposals, p => p.ModKey == "DollmanMute");
        Assert.Null(dollman.Match);

        var shaderToggler = Assert.Single(proposals, p => p.ModKey == "ShaderToggler");
        Assert.NotNull(shaderToggler.Match);
        Assert.Equal("Shader Toggler", shaderToggler.Match!.Name);
    }

    [Fact]
    public void Apply_merges_ToMeta_over_existing_entry_keeping_unrelated_fields_and_manual_locks()
    {
        // The App's apply path is Scanner.MergeMeta(existing, ToMeta(hit)) — hit wins per field,
        // existing fills the gaps. ToMeta returns a FRESH ModMeta, so a raw overwrite would wipe
        // unrelated enrichment (InstalledUtc, description, image, downloads). Pin the merge.
        var installed = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new ModMeta
        {
            Description = "Detected: ASI plugin at game root",
            Image = "cover.png",
            Downloads = 5,
            InstalledUtc = installed,
        };

        var merged = Scanner.MergeMeta(existing, LooseIdentify.ToMeta(Hit("Dollman Mute", 999)));

        // The approved hit's identity lands…
        Assert.Equal("Dollman Mute", merged.Title);
        Assert.Equal(999, merged.NexusModId);
        Assert.Equal("nameSearch", merged.SourceConfidence);
        Assert.Equal(42, merged.EndorsementCount);
        // The hit's real summary REPLACES the launcher's own detection placeholder — that's the
        // point of identifying: "Detected: ASI plugin at game root" describes how we found it,
        // the Nexus summary describes what it is.
        Assert.Equal("summary", merged.Description);
        // …and fields this hit carries nothing for still survive (fill-only merge). This hit has
        // no ThumbnailUrl and no DownloadCount, so prior enrichment stands.
        Assert.Equal("cover.png", merged.Image);
        Assert.Equal(5, merged.Downloads);
        Assert.Equal(installed, merged.InstalledUtc);

        // A manual match locks the row — the merge returns it untouched.
        var manual = new ModMeta { IsManual = true, Title = "Hand matched" };
        Assert.Same(manual, Scanner.MergeMeta(manual, LooseIdentify.ToMeta(Hit("Something Else", 2))));
    }

    [Fact]
    public void ToMeta_maps_fields_sets_nameSearch_confidence_and_leaves_IsManual_false()
    {
        var hit = Hit("Dollman Mute", 999);

        var meta = LooseIdentify.ToMeta(hit);

        Assert.Equal("Dollman Mute", meta.Title);
        Assert.Equal("SomeAuthor", meta.Author);
        Assert.Equal(hit.Url, meta.Url);
        Assert.Equal(999, meta.NexusModId);
        Assert.Equal(42, meta.EndorsementCount);
        Assert.Equal("nameSearch", meta.SourceConfidence);
        Assert.False(meta.IsManual);
    }

    // The name-search offer used to be loose-root only, so an unidentified mod in a Bethesda
    // Data folder or a UE Paks folder could never be identified. Widening the scope must NOT
    // weaken any other candidate rule.
    [Fact]
    public void Candidates_include_rows_outside_loose_root()
    {
        var rows = new List<Mod>
        {
            new() { Base = "SkyUI_5_2_SE", Name = "SkyUI_5_2_SE", Location = "Data", Class = "plugin" },
            new() { Base = "FasterShips_P", Name = "FasterShips_P", Location = "Content/Paks/~mods", Class = "pak" },
        };

        var candidates = LooseIdentify.Candidates(rows, new Dictionary<string, ModMeta>());

        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void Widening_does_not_weaken_the_other_candidate_rules()
    {
        var rows = new List<Mod>
        {
            new() { Base = "Manual", Name = "Manual", Location = "Data", Class = "plugin" },
            new() { Base = "Identified", Name = "Identified", Location = "Data", Class = "plugin" },
            new() { Base = "Loader", Name = "Loader", Location = "Data", Class = "loader" },
        };
        var meta = new Dictionary<string, ModMeta>
        {
            ["Manual"] = new() { IsManual = true },
            ["Identified"] = new() { NexusModId = 42 },
        };

        Assert.Empty(LooseIdentify.Candidates(rows, meta));
    }

    // ---- ToMeta carries the WHOLE hit, not a hand-picked subset ----

    // The row UI binds Description and Image (MainWindow.xaml thumbnail + description block), so a
    // dropped Summary/ThumbnailUrl renders as a blank, picture-less row even on a perfect match.
    [Fact]
    public void ToMeta_keeps_the_description_and_the_thumbnail()
    {
        var hit = new SourceSearchHit("cyberpunk2077", 4159, "Equipment-EX", "psiberx",
            "Expands the equipment system.", 4200, "https://nexusmods.com/cyberpunk2077/mods/4159")
        {
            ThumbnailUrl = "https://staticdelivery.nexusmods.com/thumb.jpg",
            Category = "Gameplay",
            DownloadCount = 990_000,
        };

        var meta = LooseIdentify.ToMeta(hit);

        Assert.Equal("Equipment-EX", meta.Title);
        Assert.Equal("Expands the equipment system.", meta.Description);
        Assert.Equal("https://staticdelivery.nexusmods.com/thumb.jpg", meta.Image);
        Assert.Equal("Gameplay", meta.Category);
        Assert.Equal(990_000, meta.Downloads);
        Assert.Equal("psiberx", meta.Author);
        Assert.Equal(4159, meta.NexusModId);
        Assert.Equal(4200, meta.EndorsementCount);
        Assert.Equal("nameSearch", meta.SourceConfidence);
    }

    // A name match identifies WHICH mod, never which FILE is installed. Writing the published
    // version to either side would light a false UPDATE chip on every identified row.
    [Fact]
    public void ToMeta_never_invents_version_state_from_a_name_match()
    {
        var hit = new SourceSearchHit("cyberpunk2077", 4159, "Equipment-EX", "psiberx", null, null, null)
        {
            Version = "2.1",
        };

        var meta = LooseIdentify.ToMeta(hit);

        Assert.Null(meta.Version);
        Assert.Null(meta.NexusLatestVersion);
        Assert.False(new Mod { NexusLatestVersion = meta.NexusLatestVersion, Version = meta.Version }.UpdateAvailable);
    }

    // ---- ProposeAsync at real-library scale (a hand-modded Cyberpunk install is ~200 rows) ----

    private static List<Mod> Many(int n) =>
        Enumerable.Range(0, n).Select(i => Row($"Mod{i:D3}", "plugin")).ToList();

    // Concurrency must not reshuffle the review dialog. Slowest-first ordering guarantees the
    // results ARRIVE backwards, so a naive "append as they complete" implementation fails here.
    [Fact]
    public async Task Proposals_come_back_in_candidate_order_not_completion_order()
    {
        var rows = Many(12);

        var proposals = await LooseIdentify.ProposeAsync(rows, async query =>
        {
            var index = int.Parse(query.Replace("Mod", "").Trim());
            await Task.Delay(5 * (12 - index)); // earlier rows finish LAST
            return new[] { Hit(query) };
        });

        Assert.Equal(rows.Select(r => r.Base), proposals.Select(p => p.ModKey));
    }

    [Fact]
    public async Task Never_exceeds_the_concurrency_cap()
    {
        var inFlight = 0;
        var peak = 0;
        var gate = new object();

        await LooseIdentify.ProposeAsync(Many(40), async _ =>
        {
            lock (gate) { inFlight++; if (inFlight > peak) peak = inFlight; }
            await Task.Delay(5);
            lock (gate) { inFlight--; }
            return Array.Empty<SourceSearchHit>();
        }, maxConcurrency: 4);

        Assert.True(peak <= 4, $"peak in-flight was {peak}, cap was 4");
        Assert.True(peak > 1, "ran serially — the cap is not being used at all");
    }

    [Fact]
    public async Task Progress_climbs_to_the_total()
    {
        var seen = new List<int>();
        var gate = new object();
        var progress = new Progress<LooseIdentifyProgress>(p => { lock (gate) seen.Add(p.Completed); });

        var proposals = await LooseIdentify.ProposeAsync(Many(20),
            _ => Task.FromResult<IReadOnlyList<SourceSearchHit>>(Array.Empty<SourceSearchHit>()),
            progress: progress);

        Assert.Equal(20, proposals.Count);
        // Progress<T> marshals asynchronously, so wait for the last report rather than racing it.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (gate) { if (seen.Count >= 20) break; }
            await Task.Delay(10);
        }
        lock (gate)
        {
            Assert.Equal(20, seen.Count);
            Assert.Equal(20, seen.Max());   // reaches the total
            Assert.Equal(20, seen.Distinct().Count()); // every step reported exactly once
        }
    }

    // Cancelling keeps the work already done. Nothing here writes — every proposal is still
    // review-gated — so throwing away 150 finished searches because the user stopped at 151 would
    // be hostile.
    [Fact]
    public async Task Cancelling_returns_the_completed_proposals_instead_of_throwing()
    {
        using var cts = new CancellationTokenSource();
        var done = 0;

        var proposals = await LooseIdentify.ProposeAsync(Many(200), async query =>
        {
            if (Interlocked.Increment(ref done) == 8) cts.Cancel();
            await Task.Delay(1);
            return new[] { Hit(query) };
        }, maxConcurrency: 2, ct: cts.Token);

        Assert.NotEmpty(proposals);
        Assert.True(proposals.Count < 200, "cancellation did not stop the run");
        // Whatever survived is still in candidate order, with no gaps in the returned list.
        Assert.Equal(proposals.Select(p => p.ModKey).OrderBy(k => k, StringComparer.Ordinal), proposals.Select(p => p.ModKey));
    }

    // One row's search blowing up must not cost the other 193 their attempt.
    [Fact]
    public async Task A_throwing_search_only_costs_its_own_row()
    {
        var proposals = await LooseIdentify.ProposeAsync(Many(10), query =>
            query.EndsWith("004", StringComparison.Ordinal)
                ? throw new InvalidOperationException("upstream blew up")
                : Task.FromResult<IReadOnlyList<SourceSearchHit>>(new[] { Hit(query) }));

        Assert.Equal(10, proposals.Count);
        Assert.Null(proposals.Single(p => p.ModKey == "Mod004").Match);
        Assert.Equal(9, proposals.Count(p => p.Match is not null));
    }

    [Fact]
    public async Task Empty_candidate_list_does_no_work()
    {
        var calls = 0;

        var proposals = await LooseIdentify.ProposeAsync(new List<Mod>(),
            _ => { calls++; return Task.FromResult<IReadOnlyList<SourceSearchHit>>(Array.Empty<SourceSearchHit>()); });

        Assert.Empty(proposals);
        Assert.Equal(0, calls);
    }

    // ---- ExcludeKeys: the strong pass wins inside a single run ----

    // An archive's md5 write keys resolve only at apply time, so a name-search proposal for the
    // same row survives every propose-time filter. Applying md5 first and filtering here is what
    // stops a guess from overwriting an exact match.
    [Fact]
    public void Keys_already_written_by_a_stronger_pass_are_dropped()
    {
        var approved = new[]
        {
            ("EquipmentEx", Hit("Equipment-EX", 1)),
            ("GoneAway", Hit("Gone Away", 2)),
        };

        var kept = LooseIdentify.ExcludeKeys(approved, new[] { "EquipmentEx" });

        var one = Assert.Single(kept);
        Assert.Equal("GoneAway", one.ModKey);
    }

    [Fact]
    public void Key_exclusion_ignores_case()
    {
        var approved = new[] { ("EquipmentEx", Hit("Equipment-EX", 1)) };

        Assert.Empty(LooseIdentify.ExcludeKeys(approved, new[] { "equipmentex" }));
    }

    [Fact]
    public void Excluding_against_nothing_keeps_every_approved_pair()
    {
        var approved = new[] { ("A", Hit("A", 1)), ("B", Hit("B", 2)) };

        Assert.Equal(2, LooseIdentify.ExcludeKeys(approved, Array.Empty<string>()).Count);
    }
}
