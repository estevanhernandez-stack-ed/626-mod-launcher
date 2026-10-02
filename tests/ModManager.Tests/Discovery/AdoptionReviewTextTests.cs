using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

/// <summary>
/// One spelling of a review row, read by both review dialogs. Their comments claimed the wording
/// "can't drift" while the hash-match line already had (C4).
/// </summary>
public class AdoptionReviewTextTests
{
    private static AdoptionProposal P(AdoptionEvidence evidence, DiscoveryKind kind = DiscoveryKind.Archive,
        AdoptionReach? reach = AdoptionReach.NamesAMod, string? title = "Cool Mod", string? author = "someone")
        => new AdoptionProposal(new DiscoveryCandidate("mods/cool.pak", "cool.pak", kind), evidence, 1, title, author, null)
            { Reach = reach };

    [Fact]
    public void A_hash_match_reads_the_same_on_both_surfaces()
        => Assert.Equal("Matched exactly by file hash. mods/cool.pak", AdoptionReviewText.Detail(P(AdoptionEvidence.Md5)));

    [Fact]
    public void A_name_match_names_its_author()
        => Assert.Equal("Matched by name · by someone. mods/cool.pak", AdoptionReviewText.Detail(P(AdoptionEvidence.NameIndex)));

    // A live-search match is identified (its row is pre-checked), so it must not read as unidentified.
    [Fact]
    public void A_live_search_match_reads_as_a_name_match()
    {
        var detail = AdoptionReviewText.Detail(P(AdoptionEvidence.NameSearch));

        Assert.StartsWith("Matched by name", detail);
        Assert.DoesNotContain("anyway", detail);
    }

    [Fact]
    public void A_loader_is_described_as_a_loader_whatever_the_evidence()
    {
        var p = P(AdoptionEvidence.Md5, DiscoveryKind.ProxyLoader);

        Assert.Equal("cool.pak — mod loader", AdoptionReviewText.Headline(p));
        Assert.Contains("loader other mods ride on", AdoptionReviewText.Detail(p));
    }

    [Fact]
    public void A_download_never_installed_says_so()
    {
        var p = P(AdoptionEvidence.Md5, reach: AdoptionReach.NothingToNameYet);

        Assert.Equal("cool.pak — Cool Mod (downloaded, not installed)", AdoptionReviewText.Headline(p));
        Assert.Contains("This is the download, not an installed mod", AdoptionReviewText.Detail(p));
    }

    [Fact]
    public void An_already_named_mod_has_nothing_to_add()
        => Assert.Contains("Already named", AdoptionReviewText.Detail(P(AdoptionEvidence.Md5, reach: AdoptionReach.AlreadyNamed)));

    [Fact]
    public void An_unidentified_file_says_so()
    {
        var p = P(AdoptionEvidence.None, title: null, author: null);

        Assert.Equal("cool.pak — not identified", AdoptionReviewText.Headline(p));
        Assert.Equal("Found at mods/cool.pak. Adopt it to manage it anyway.", AdoptionReviewText.Detail(p));
    }

    // Both dialogs build their rows from this class and nothing else; a copy of the wording would
    // be a second spelling waiting to drift.
    [Theory]
    [InlineData("IdentifyReviewDialog.xaml.cs")]
    [InlineData("DiscoveryReviewDialog.xaml.cs")]
    public void Both_review_dialogs_read_the_one_spelling(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ModManager.App"))) dir = dir.Parent;
        Assert.True(dir is not null, "could not find src/ModManager.App above the test output");
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "src", "ModManager.App", file));

        Assert.Contains("AdoptionReviewText.Headline(", src);
        Assert.Contains("AdoptionReviewText.Detail(", src);
        Assert.Contains("AdoptionReviewText.PreChecked(", src);
        Assert.Contains("AdoptionReviewText.WillWrite(", src);
        // No second spelling of any line the class owns.
        foreach (var owned in new[] { "by file hash", "Matched by name", "manage it anyway", "mod loader\"", "not identified\"" })
            Assert.DoesNotContain(owned, src);
    }
}
