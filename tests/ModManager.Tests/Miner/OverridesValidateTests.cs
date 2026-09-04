using ManifestMiner;

namespace ModManager.Tests.Miner;

/// <summary>
/// The gate that stops two curated files quietly fighting over one game.
///
/// <para>Before it, two overrides sharing a Steam app id both "worked": one won by iteration order and
/// nothing said so. Two files in the real overrides directory do exactly that
/// (the-witcher-2-assassins-of-kings and its enhanced edition, both claiming 20920). Today the richer
/// one happens to win; if that order ever flipped, the game would silently drop to nexus-only with no
/// engine and no mod path.</para>
/// </summary>
public class OverridesValidateTests
{
    private static OverrideEntry E(string? id = null, string? steam = null, string? name = null, string? path = null)
        => new() { Id = id, SteamAppId = steam, Name = name, SourcePath = path ?? (id ?? steam) + ".json" };

    [Fact]
    public void A_clean_set_has_no_problems()
        => Assert.Empty(OverridesValidate.Check(new[]
        {
            E(id: "skyrim", steam: "72850"),
            E(id: "palworld", steam: "1623730"),
            E(id: "some-ea-game"),
        }));

    [Fact]
    public void Two_overrides_sharing_a_Steam_id_are_a_problem_that_names_both_files()
    {
        var problems = OverridesValidate.Check(new[]
        {
            E(id: "the-witcher-2", steam: "20920", path: "witcher2.json"),
            E(id: "the-witcher-2-ee", steam: "20920", path: "witcher2-ee.json"),
        });

        var p = Assert.Single(problems);
        Assert.Contains("20920", p.Message);
        Assert.Contains("witcher2.json", p.Message);
        Assert.Contains("witcher2-ee.json", p.Message);
    }

    [Fact]
    public void Two_overrides_sharing_a_slug_are_a_problem_that_names_both_files()
    {
        // The failure mode slug-keying introduces. There is no second key to disambiguate on, so it
        // has to be fatal rather than resolved.
        var problems = OverridesValidate.Check(new[]
        {
            E(id: "big-ambitions", path: "big-ambitions.json"),
            E(id: "big-ambitions", path: "big-ambitions-copy.json"),
        });

        var p = Assert.Single(problems);
        Assert.Contains("big-ambitions", p.Message);
        Assert.Contains("big-ambitions-copy.json", p.Message);
    }

    [Fact]
    public void A_slug_derived_from_the_name_still_collides()
    {
        // An entry with no explicit id is addressed by Slugify(Name), so two of those collide just as
        // hard as two explicit ids - and it is less obvious from reading the files.
        var problems = OverridesValidate.Check(new[]
        {
            E(name: "Big Ambitions", path: "a.json"),
            E(name: "Big Ambitions", path: "b.json"),
        });

        Assert.Single(problems);
    }

    [Fact]
    public void An_override_with_no_usable_key_at_all_is_a_problem()
    {
        var problems = OverridesValidate.Check(new[] { E(path: "mystery.json") });

        var p = Assert.Single(problems);
        Assert.Contains("mystery.json", p.Message);
    }

    [Fact]
    public void Keys_are_compared_case_insensitively()
    {
        // "Palworld.json" and "palworld.json" are the same game to every consumer downstream.
        Assert.Single(OverridesValidate.Check(new[] { E(id: "Palworld"), E(id: "palworld") }));
    }

    [Fact]
    public void The_same_slug_and_the_same_Steam_id_reports_once_not_twice()
    {
        // One pair of files, one problem. Reporting it under both rules would read as two conflicts.
        var problems = OverridesValidate.Check(new[]
        {
            E(id: "dupe", steam: "111", path: "a.json"),
            E(id: "dupe", steam: "111", path: "b.json"),
        });

        Assert.Single(problems);
    }
}
