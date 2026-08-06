using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

// The unified identify run's terminal status line. This exists in Core, behind tests, because it
// is the sentence that tells the user what we DID to their install — a branch nest deciding what
// we claim we wrote is exactly the wrong thing to leave in the untestable App layer.
//
// The one law every case below enforces: the line must never say less than what was written.
// A run that filled eight rows and then stopped, a run whose adoptions landed but whose names were
// refused, a run truncated by the md5 cap — each has a specific truth, and "nothing happened" is a
// lie in all of them.
public class IdentifyRunReportTests
{
    // ---- nothing happened ----

    [Fact]
    public void Nothing_found_falls_back_to_the_callers_line()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome
        {
            NothingHappenedLine = "No loose mods need identifying.",
        });

        Assert.Equal("No loose mods need identifying.", line);
    }

    [Fact]
    public void Nothing_found_with_no_caller_line_still_says_nothing_changed()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome());

        Assert.Equal("Nothing was changed.", line);
    }

    [Fact]
    public void Stopped_with_nothing_written_says_stopped_and_nothing_changed()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome
        {
            Stopped = true,
            // Even when a caller offers a "nothing found" line, a stopped run must not present it
            // as a verified conclusion — the run never finished looking.
            NothingHappenedLine = "Everything in this game's folder is already in your list and identified.",
        });

        Assert.Equal("Stopped. Nothing was changed.", line);
    }

    // ---- one pass did something ----

    [Fact]
    public void Only_adoptions_names_them_and_keeps_the_files_promise()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome { Adopted = 4 });

        Assert.Equal("Adopted 4 mods. Your files were not moved.", line);
    }

    [Fact]
    public void Only_identifications_reads_as_a_sentence_not_a_fragment()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome { Named = 3 });

        // Capitalized despite "named" being a mid-sentence phrase everywhere else.
        Assert.Equal("Named 3 mods.", line);
        // The files promise belongs to adoption; naming never touched a file to begin with.
        Assert.DoesNotContain("files were not moved", line);
    }

    [Fact]
    public void Only_fills_is_reported_rather_than_swallowed()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome { Filled = 8 });

        Assert.Equal("Filled in details for 8 mods.", line);
    }

    [Fact]
    public void Singular_counts_read_singular()
    {
        Assert.Equal("Adopted 1 mod. Your files were not moved.",
            IdentifyRunReport.Summarize(new IdentifyRunOutcome { Adopted = 1 }));
        Assert.Equal("Named 1 mod.",
            IdentifyRunReport.Summarize(new IdentifyRunOutcome { Named = 1 }));
        Assert.Equal("Filled in details for 1 mod.",
            IdentifyRunReport.Summarize(new IdentifyRunOutcome { Filled = 1 }));
    }

    // ---- combinations ----

    [Fact]
    public void Two_passes_join_with_and()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome { Adopted = 2, Filled = 5 });

        Assert.Equal("Adopted 2 mods and filled in details for 5 mods. Your files were not moved.", line);
    }

    [Fact]
    public void All_three_passes_join_as_a_list()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome { Adopted = 2, Named = 3, Filled = 5 });

        Assert.Equal(
            "Adopted 2 mods, named 3 mods and filled in details for 5 mods. Your files were not moved.",
            line);
    }

    // ---- cancellation, per pass ----

    [Fact]
    public void Stopped_after_filling_never_claims_nothing_happened()
    {
        // The exact regression this whole composer exists for: the fill pass writes its partial
        // batch on cancellation, so eight rows on screen just gained descriptions.
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome { Filled = 8, Stopped = true });

        Assert.Equal("Filled in details for 8 mods. Stopped early — run it again for the rest.", line);
        Assert.DoesNotContain("Nothing was changed", line);
    }

    [Fact]
    public void Stopped_after_adopting_reports_both_the_work_and_the_stop()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome { Adopted = 3, Stopped = true });

        Assert.StartsWith("Adopted 3 mods.", line);
        Assert.EndsWith("Stopped early — run it again for the rest.", line);
    }

    [Fact]
    public void Stopped_after_naming_reports_both()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome { Named = 2, Stopped = true });

        Assert.StartsWith("Named 2 mods.", line);
        Assert.EndsWith("Stopped early — run it again for the rest.", line);
    }

    // ---- the apply explanations must survive ----

    [Fact]
    public void Zero_writes_after_approvals_keeps_the_adoption_applys_reason()
    {
        // The downloads-folder norm: an md5-matched archive whose contents are not installed under
        // any known key expands to zero writes. The user approved "Adopt 3 mods" and must be told
        // WHY none landed, not "Nothing was changed."
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome
        {
            AdoptionNote = "Nothing to adopt — the matched archive doesn't correspond to an installed file yet.",
        });

        Assert.Contains("doesn't correspond to an installed file yet", line);
        // And it is the WHOLE line. A generic "Nothing was changed." in front of a specific reason
        // is noise that makes the reason look like an afterthought.
        Assert.DoesNotContain("Nothing was changed", line);
    }

    [Fact]
    public void A_refused_identify_apply_keeps_its_reason_and_the_rest_of_the_run()
    {
        // A refusal must not erase what the earlier passes already wrote.
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome
        {
            Adopted = 4,
            Filled = 2,
            IdentifyNote = "Connect Nexus first (toolbar -> Nexus).",
        });

        Assert.Contains("Adopted 4 mods", line);
        Assert.Contains("filled in details for 2 mods", line);
        Assert.Contains("Connect Nexus first", line);
    }

    // ---- dropped by ExcludeKeys ----

    [Fact]
    public void Dropped_name_matches_are_reported_without_overstating_the_evidence()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome { Adopted = 4, DroppedNameMatches = 3 });

        Assert.Contains("3 name matches were skipped", line);
        // ExcludeKeys is tier-agnostic: the keys it filters against include name-index and
        // unidentified adoptions, not only md5 ones. Claiming a file-hash match would be a
        // statement about evidence the run does not have.
        Assert.DoesNotContain("hash", line);
    }

    [Fact]
    public void One_dropped_name_match_reads_singular()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome { Adopted = 1, DroppedNameMatches = 1 });

        Assert.Contains("1 name match was skipped", line);
        Assert.Contains("that mod", line);
    }

    // ---- downloads-folder note ----

    [Fact]
    public void The_downloads_note_rides_along_on_any_outcome()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome
        {
            Adopted = 1,
            DownloadsNote = "Nothing in that downloads folder matched Nexus.",
        });

        Assert.Contains("Adopted 1 mod.", line);
        Assert.Contains("Nothing in that downloads folder matched Nexus.", line);
    }

    [Fact]
    public void Downloads_note_survives_a_run_that_wrote_nothing()
    {
        var line = IdentifyRunReport.Summarize(new IdentifyRunOutcome
        {
            NothingHappenedLine = "No loose mods need identifying.",
            DownloadsNote = "That downloads folder was skipped — matching by file hash needs Nexus connected.",
        });

        Assert.Contains("No loose mods need identifying.", line);
        Assert.Contains("needs Nexus connected", line);
    }

    // ---- DownloadsFolderNote: the cap and the diagnosis ----

    [Fact]
    public void A_complete_pass_that_matched_something_says_nothing()
    {
        // The proposals speak for themselves in the review dialog.
        Assert.Null(IdentifyRunReport.DownloadsFolderNote(
            archivesFound: 12, checkedCount: 12, matched: 3, truncated: false, stopped: false));
    }

    [Fact]
    public void A_complete_pass_that_matched_nothing_says_so_and_names_the_remedy()
    {
        var note = IdentifyRunReport.DownloadsFolderNote(
            archivesFound: 12, checkedCount: 12, matched: 0, truncated: false, stopped: false);

        Assert.NotNull(note);
        Assert.Contains("Nothing in that downloads folder matched Nexus", note);
        Assert.Contains("ORIGINAL", note);
    }

    [Fact]
    public void The_cap_note_fires_only_on_a_real_truncation()
    {
        // A 400-archive folder inside the game root where pass 1 already claimed 360: the pass runs
        // to COMPLETION having checked 40, because duplicates are skipped without charging the
        // budget. There is no "rest" to point the user at.
        Assert.Null(IdentifyRunReport.DownloadsFolderNote(
            archivesFound: 400, checkedCount: 40, matched: 2, truncated: false, stopped: false));
    }

    [Fact]
    public void A_truncated_pass_reports_what_it_actually_checked()
    {
        var note = IdentifyRunReport.DownloadsFolderNote(
            archivesFound: 600, checkedCount: 100, matched: 4, truncated: true, stopped: false);

        Assert.NotNull(note);
        Assert.Contains("first 100 of 600", note);
        Assert.Contains("per-run limit", note);
    }

    [Fact]
    public void A_truncated_pass_that_matched_nothing_scopes_its_claim_to_what_it_read()
    {
        var note = IdentifyRunReport.DownloadsFolderNote(
            archivesFound: 600, checkedCount: 100, matched: 0, truncated: true, stopped: false);

        Assert.NotNull(note);
        Assert.Contains("first 100 of 600", note);
        // "None of those" has an antecedent only because the cap sentence precedes it.
        Assert.Contains("None of those matched Nexus.", note);
        Assert.DoesNotContain("Nothing in that downloads folder matched", note);
    }

    [Fact]
    public void A_stopped_pass_never_diagnoses_archives_it_never_read()
    {
        // Stop at archive 3 of 500. "Nothing matched, they must not be the original archives" is a
        // false diagnosis pointing at a wrong remedy, about files we never opened.
        Assert.Null(IdentifyRunReport.DownloadsFolderNote(
            archivesFound: 500, checkedCount: 3, matched: 0, truncated: false, stopped: true));
    }

    [Fact]
    public void A_stopped_pass_after_the_cap_leaves_no_dangling_antecedent()
    {
        // Cap note suppressed for a stopped run means "None of those" would have nothing to refer
        // back to — so it must not be emitted either.
        var note = IdentifyRunReport.DownloadsFolderNote(
            archivesFound: 600, checkedCount: 100, matched: 0, truncated: true, stopped: true);

        Assert.DoesNotContain("None of those", note ?? "");
    }
}
