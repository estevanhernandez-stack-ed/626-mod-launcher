using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Patch Day: mods that broke on a game update, read from their own logs. After the 2026-11-04 Elden Ring patch all six
/// TechieW DLL mods popped "Could not find signature!" on every modded launch, and Patch Day only knew about stale
/// loaders. Each writes the failure to <c>Game\mods\&lt;Mod&gt;\log.txt</c>; the lines below are verbatim from Este's
/// install (2026-11-08).
///
/// <para>Detection only: nothing here moves a file. A failure counts when the log was written after the game executable
/// AND after the mod's own DLL, so a mod updated since its failure is not flagged. Matched on the line, never on a mod
/// name, so any memory-scanning mod that logs the same way is covered.</para>
/// </summary>
public class BrokenModLogsTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-broken-");
    private string Play => Path.Combine(_root, "Game");
    private static readonly DateTime ExeUtc = new(2026, 11, 4, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime After = ExeUtc.AddDays(4);

    public BrokenModLogsTests() => Directory.CreateDirectory(Path.Combine(Play, "mods"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private void Mod(string stem, string log, DateTime logUtc, DateTime? dllUtc = null)
    {
        var dll = Path.Combine(Play, "mods", stem + ".dll");
        File.WriteAllText(dll, "dll");
        File.SetLastWriteTimeUtc(dll, dllUtc ?? ExeUtc.AddDays(-30));
        var dir = Path.Combine(Play, "mods", stem);
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "log.txt");
        File.WriteAllText(logPath, log);
        File.SetLastWriteTimeUtc(logPath, logUtc);
    }

    private const string Raised =
        "AdjustTheFov > Reached end of scannable memory.\nAdjustTheFov > Stopped at: 00007FFE5CF87FD7, num regions checked: 614\n"
        + "AdjustTheFov > Raised error: Could not find signature!\n";
    private const string Popup =
        "SkipTheIntro > Stopped at: 00007ffe5cf87fe5, num regions checked: 653\nSkipTheIntro > Error popup: Could not find signature!\n"
        + "SkipTheIntro > Closing window\n";
    // A run that got past its scan: it starts like every run and logs no failure.
    private const string Healthy = "UltrawideFix > Activating UltrawideFix...\nUltrawideFix > Process name: eldenring.exe\n";

    [Fact]
    public void A_signature_failure_written_after_the_update_is_found_and_names_the_row()
    {
        Mod("AdjustTheFov", Raised, After);
        var hit = Assert.Single(BrokenModLogs.Find(Play, ExeUtc));
        Assert.Equal("Adjust The Fov", hit.RowName);                       // the row the list shows
        Assert.Contains("Could not find signature", hit.Line);
    }

    [Fact]
    public void The_error_popup_wording_is_found_too()
    {
        Mod("SkipTheIntro", Popup, After);
        Assert.Equal("Skip The Intro", Assert.Single(BrokenModLogs.Find(Play, ExeUtc)).RowName);
    }

    [Fact]
    public void A_healthy_log_is_not_a_failure()
    {
        Mod("UltrawideFix", Healthy, After);
        Assert.Empty(BrokenModLogs.Find(Play, ExeUtc));
    }

    [Fact]
    public void A_failure_from_before_the_update_is_old_news()
    {
        Mod("AdjustTheFov", Raised, ExeUtc.AddDays(-1));
        Assert.Empty(BrokenModLogs.Find(Play, ExeUtc));
    }

    [Fact]
    public void A_mod_updated_since_its_failure_is_not_flagged()
    {
        Mod("AdjustTheFov", Raised, After, dllUtc: After.AddHours(2));     // new DLL, not run yet
        Assert.Empty(BrokenModLogs.Find(Play, ExeUtc));
    }

    [Fact]
    public void Without_the_executables_date_nothing_is_claimed()
    {
        Mod("AdjustTheFov", Raised, After);
        Assert.Empty(BrokenModLogs.Find(Play, null));
    }

    [Fact]
    public void Matched_on_the_line_not_the_mod_name()
    {
        Mod("SomeoneElsesMod", "SomeoneElsesMod > Raised error: Could not find signature!\n", After);
        Assert.Equal("Someone Elses Mod", Assert.Single(BrokenModLogs.Find(Play, ExeUtc)).RowName);
    }

    [Fact]
    public void A_huge_log_is_read_from_its_tail()
    {
        // SkipTheIntro's real log was 11 MB. The latest run is at the end; an old failure far up is not the latest run.
        var padding = string.Concat(Enumerable.Repeat("SkipTheIntro > Skipped region: 00007FFE5CF88000\n", 60_000));
        Mod("SkipTheIntro", padding + Popup, After);
        Assert.Single(BrokenModLogs.Find(Play, ExeUtc));

        Mod("SkipTheIntro", Popup + padding + "SkipTheIntro > Activating SkipTheIntro...\nSkipTheIntro > Process name: eldenring.exe\n", After);
        Assert.Empty(BrokenModLogs.Find(Play, ExeUtc));
    }

    [Fact]
    public void Only_live_mods_count()
    {
        // A held copy's log sits in holding, not Game\mods: set aside already, nothing to offer.
        Assert.Empty(BrokenModLogs.Find(Play, ExeUtc));
        Assert.Empty(BrokenModLogs.Find(Path.Combine(_root, "nope"), ExeUtc));
    }

    [Fact]
    public void The_summary_names_the_update_date_and_the_mods()
    {
        Mod("AdjustTheFov", Raised, After);
        Mod("SkipTheIntro", Popup, After);
        var s = BrokenModLogs.Summary(BrokenModLogs.Find(Play, ExeUtc), ExeUtc);
        Assert.Equal("2 mods failed to start after the game's Nov 4 update: Adjust The Fov, Skip The Intro. "
            + "They usually need a new release from their authors.", s);
        Assert.Null(BrokenModLogs.Summary(Array.Empty<BrokenMod>(), ExeUtc));
    }

    [Fact]
    public void The_strip_shows_it_amber_with_a_set_aside_action()
    {
        var chip = Assert.Single(GameStateStrip.For(new GameStateConditions { BrokenOnUpdate = "2 mods failed." }));
        Assert.Equal("broke-on-update", chip.Id);
        Assert.Equal(GameStateSeverity.Warning, chip.Severity);
        Assert.Equal("Set these aside", chip.ActionLabel);
    }
}
