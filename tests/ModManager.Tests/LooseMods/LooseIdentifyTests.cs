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
        // …and the unrelated existing fields survive.
        Assert.Equal("Detected: ASI plugin at game root", merged.Description);
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
}
