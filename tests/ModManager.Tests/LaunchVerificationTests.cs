using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Wave 5 / B5. 626 launches the game and stops looking — the whole A13 class of bug, whose only
/// witness today is the user at the crash.
///
/// <para>Measured on the rig 2026-08-18: Death Stranding 2's <c>ReShade.log</c> and
/// <c>dlss-enabler.log</c> were both written at 15:29, matching 626's own launch stamp, while
/// Windrose's <c>UE4SS.log</c> still read <b>2 August</b> — a loader that had not run in over two weeks
/// on a game the launcher reported as healthy.</para>
/// </summary>
public class LaunchVerificationTests
{
    private static readonly DateTime Launch = new(2026, 8, 18, 15, 29, 0, DateTimeKind.Utc);

    private static LoaderEvidence Ev(string name, string[]? wanted = null, string[]? loaded = null, DateTime? log = null)
        => new(name, wanted ?? new[] { "UE4SS.dll" }, loaded ?? Array.Empty<string>(), log);

    [Fact]
    public void A_loaded_module_is_the_strongest_proof_and_needs_no_log()
    {
        // A file on disk has been installed. A module in memory has RUN.
        var o = LaunchVerification.For(Launch, Ev("UE4SS", loaded: new[] { @"C:\game\Binaries\Win64\UE4SS.dll" }));

        Assert.Equal(LoadVerdict.Ran, o.Verdict);
        Assert.Contains("loaded in the game", o.Detail);
    }

    [Fact]
    public void A_module_matches_on_filename_not_on_path()
    {
        var o = LaunchVerification.For(Launch, Ev("ReShade", new[] { "dxgi.dll" }, new[] { @"D:\somewhere\else\DXGI.DLL" }));

        Assert.Equal(LoadVerdict.Ran, o.Verdict);
    }

    [Fact]
    public void A_log_written_during_the_launch_counts_as_having_run()
    {
        var o = LaunchVerification.For(Launch, Ev("ReShade", log: Launch.AddSeconds(4)));

        Assert.Equal(LoadVerdict.Ran, o.Verdict);
        Assert.Contains("wrote its log", o.Detail);
    }

    [Fact]
    public void A_log_written_just_before_the_stamp_still_counts()
    {
        // A loader writes its first line as the game starts, which can land marginally before the
        // stamp 626 records. A false alarm here is worse than a missed one: it teaches the user to
        // ignore the message.
        var o = LaunchVerification.For(Launch, Ev("ReShade", log: Launch.AddSeconds(-20)));

        Assert.Equal(LoadVerdict.Ran, o.Verdict);
    }

    [Fact]
    public void The_real_windrose_case_reads_as_did_not_run_and_says_when_it_last_did()
    {
        // UE4SS.log stuck at 2 August while the launcher reported 27 of 27 enabled.
        var stale = new DateTime(2026, 8, 2, 10, 42, 0, DateTimeKind.Utc);

        var o = LaunchVerification.For(Launch, Ev("UE4SS", log: stale));

        Assert.Equal(LoadVerdict.DidNotRun, o.Verdict);
        Assert.Contains("did not load", o.Detail);
        Assert.Contains("August", o.Detail);
    }

    [Fact]
    public void Read_the_process_and_not_find_it_is_also_an_answer()
    {
        // No log at all, but we successfully read what the game has open and the proxy is not there.
        var o = LaunchVerification.For(Launch,
            Ev("UE4SS", new[] { "dwmapi.dll" }, new[] { "kernel32.dll", "d3d12.dll" }));

        Assert.Equal(LoadVerdict.DidNotRun, o.Verdict);
        Assert.Contains("not among the files the game has open", o.Detail);
    }

    [Fact]
    public void No_evidence_at_all_is_UNKNOWN_and_never_did_not_run()
    {
        // The distinction the whole type exists for. A first launch after a fresh install has no log
        // and nothing readable; calling that a failure is crying wolf.
        var o = LaunchVerification.For(Launch, Ev("UE4SS", loaded: Array.Empty<string>(), log: null));

        Assert.Equal(LoadVerdict.Unknown, o.Verdict);
        Assert.Contains("could not be determined", o.Detail);
    }

    [Fact]
    public void A_loader_we_know_no_module_names_for_is_not_condemned_by_a_module_list()
    {
        // We read the process fine, but we do not know what this loader would look like in it. That is
        // our ignorance, not its failure.
        var o = LaunchVerification.For(Launch,
            new LoaderEvidence("Mystery", Array.Empty<string>(), new[] { "kernel32.dll" }, null));

        Assert.Equal(LoadVerdict.Unknown, o.Verdict);
    }

    [Fact]
    public void The_summary_leads_with_what_failed()
    {
        var outcomes = LaunchVerification.ForAll(Launch, new[]
        {
            Ev("ReShade", new[] { "dxgi.dll" }, new[] { "dxgi.dll" }),
            Ev("UE4SS", log: new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc)),
        });

        var line = LaunchVerification.Summarize("Windrose", outcomes);

        Assert.StartsWith("Windrose started.", line);
        Assert.Contains("UE4SS did not load", line);
        Assert.DoesNotContain("ReShade", line);   // the good news is not the news
    }

    [Fact]
    public void A_clean_launch_says_so_once_and_briefly()
    {
        var outcomes = LaunchVerification.ForAll(Launch, new[]
        {
            Ev("ReShade", new[] { "dxgi.dll" }, new[] { "dxgi.dll" }),
            Ev("UE4SS", new[] { "UE4SS.dll" }, new[] { "UE4SS.dll" }),
        });

        Assert.Equal("Windrose started, and all 2 loaders loaded.", LaunchVerification.Summarize("Windrose", outcomes));
    }

    [Fact]
    public void Knowing_nothing_says_nothing()
    {
        // A launcher that reports "I could not tell" after every launch is noise, and noise is how a
        // real warning gets ignored.
        var outcomes = LaunchVerification.ForAll(Launch, new[] { Ev("UE4SS") });

        Assert.Equal("", LaunchVerification.Summarize("Windrose", outcomes));
        Assert.Equal("", LaunchVerification.Summarize("Windrose", null));
    }
}
