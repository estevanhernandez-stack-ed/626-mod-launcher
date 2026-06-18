using ModManager.Core;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Nexus;

// ApplyEndorsements — the pure status -> bool apply onto the active domain's metas. The bulk
// endorse list is read-only state sync (one cheap call returns the user's per-mod state library-wide);
// NexusRefresh.ApplyEndorsements folds the entries matching the active domain back onto the metas so
// hearts reflect reality even for mods endorsed outside the launcher. The client-request / bulk-read
// wiring (the deleted Core NexusClient/NexusRequests) is covered by the plugin test project.
public class NexusUserEndorsementsTests
{
    [Fact]
    public void ApplyEndorsements_sets_true_for_endorsed_match_in_domain()
    {
        var metas = new List<ModMeta>
        {
            new() { NexusModId = 42, Title = "A" },
        };
        var endorsements = new List<SourceEndorsement>
        {
            new(42, "eldenring", "Endorsed"),
        };

        NexusRefresh.ApplyEndorsements(metas, endorsements, "eldenring");

        Assert.True(metas[0].Endorsed);
    }

    [Fact]
    public void ApplyEndorsements_sets_false_for_non_endorsed_status_match()
    {
        var metas = new List<ModMeta>
        {
            new() { NexusModId = 42, Title = "A" },
            new() { NexusModId = 7, Title = "B" },
        };
        var endorsements = new List<SourceEndorsement>
        {
            new(42, "eldenring", "Abstained"),
            new(7, "eldenring", "Undecided"),
        };

        NexusRefresh.ApplyEndorsements(metas, endorsements, "eldenring");

        Assert.False(metas[0].Endorsed);
        Assert.False(metas[1].Endorsed);
    }

    [Fact]
    public void ApplyEndorsements_leaves_non_matching_metas_untouched()
    {
        var metas = new List<ModMeta>
        {
            new() { NexusModId = 42, Title = "matched", Endorsed = null },
            new() { NexusModId = 99, Title = "no entry", Endorsed = null }, // not in the list
            new() { CurseforgeId = 5, Title = "no nexus id", Endorsed = null }, // unresolvable id
        };
        var endorsements = new List<SourceEndorsement>
        {
            new(42, "eldenring", "Endorsed"),
        };

        NexusRefresh.ApplyEndorsements(metas, endorsements, "eldenring");

        Assert.True(metas[0].Endorsed);
        Assert.Null(metas[1].Endorsed);
        Assert.Null(metas[2].Endorsed);
    }

    [Fact]
    public void ApplyEndorsements_ignores_entries_from_a_different_domain()
    {
        var metas = new List<ModMeta>
        {
            new() { NexusModId = 42, Title = "A", Endorsed = null },
        };
        var endorsements = new List<SourceEndorsement>
        {
            // same mod id, but a different game domain — must NOT apply to eldenring metas.
            new(42, "skyrimspecialedition", "Endorsed"),
        };

        NexusRefresh.ApplyEndorsements(metas, endorsements, "eldenring");

        Assert.Null(metas[0].Endorsed);
    }

    [Fact]
    public void ApplyEndorsements_resolves_id_from_url_when_nexus_mod_id_absent()
    {
        var metas = new List<ModMeta>
        {
            new() { Url = "https://www.nexusmods.com/eldenring/mods/42", Title = "A", Endorsed = null },
        };
        var endorsements = new List<SourceEndorsement>
        {
            new(42, "eldenring", "Endorsed"),
        };

        NexusRefresh.ApplyEndorsements(metas, endorsements, "eldenring");

        Assert.True(metas[0].Endorsed);
    }
}
