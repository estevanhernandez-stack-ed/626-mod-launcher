using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// "What broke", first slice (research backlog 2026-10-16, bet 3): when a modded launch verifies that every loader ran,
/// the setup is stamped as last known good; when a later launch finds a loader did not run, the status line says what
/// changed since. Read-only: nothing here moves a file. Going back to it is the next slice.
/// </summary>
public class KnownGoodTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-known-good-");
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static readonly DateTime Stamped = new(2026, 11, 8, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Exe = new(2026, 11, 4, 20, 0, 0, DateTimeKind.Utc);

    private static Mod M(string name, bool on, string location = "mods", bool isBase = false)
        => new() { Name = name, Enabled = on, Location = location, IsBase = isBase };

    private static LoaderOutcome O(LoadVerdict v) => new("UE4SS", v, "detail");

    [Fact]
    public void A_launch_where_every_loader_ran_is_stamped_and_only_then()
    {
        Assert.True(KnownGood.ShouldStamp(new[] { O(LoadVerdict.Ran), O(LoadVerdict.Ran) }, LaunchMode.Modded));
        Assert.False(KnownGood.ShouldStamp(new[] { O(LoadVerdict.Ran), O(LoadVerdict.Unknown) }, LaunchMode.Modded));
        Assert.False(KnownGood.ShouldStamp(new[] { O(LoadVerdict.Ran), O(LoadVerdict.DidNotRun) }, LaunchMode.Modded));
        Assert.False(KnownGood.ShouldStamp(Array.Empty<LoaderOutcome>(), LaunchMode.Modded));   // nothing proven
        Assert.False(KnownGood.ShouldStamp(new[] { O(LoadVerdict.Ran) }, LaunchMode.Vanilla));  // not a modded setup
    }

    [Fact]
    public void The_snapshot_keeps_the_enabled_mods_and_leaves_out_the_games_own_files()
    {
        var s = KnownGood.Snapshot(new[] { M("A", true), M("B", false), M("Base.esm", true, isBase: true) }, Exe, Stamped);
        var only = Assert.Single(s.Enabled);
        Assert.Equal("A", only.Name);
        Assert.Equal(Exe, s.GameExeUtc);
    }

    [Fact]
    public void Nothing_changed_says_nothing()
    {
        var s = KnownGood.Snapshot(new[] { M("A", true), M("B", false) }, Exe, Stamped);
        Assert.Null(KnownGood.Since(s, new[] { M("A", true), M("B", false) }, Exe).Describe());
    }

    [Fact]
    public void Every_kind_of_change_is_named_in_one_sentence()
    {
        var s = KnownGood.Snapshot(new[] { M("Keep", true), M("Off Now", true), M("Gone", true), M("Was Off", false) }, Exe, Stamped);
        var now = new[] { M("Keep", true), M("Off Now", false), M("Was Off", true), M("Brand New", true) };

        var d = KnownGood.Since(s, now, Exe.AddDays(6));

        Assert.True(d.GameUpdated);
        Assert.Equal(new[] { "Brand New", "Was Off" }, d.TurnedOn);
        Assert.Equal(new[] { "Off Now" }, d.TurnedOff);
        Assert.Equal(new[] { "Gone" }, d.Removed);
        Assert.Equal("Since mods last loaded cleanly on Nov 8: the game updated; you turned on Brand New and Was Off; "
            + "turned off Off Now; removed Gone.", d.Describe());
    }

    [Fact]
    public void A_mod_with_the_same_name_in_another_location_is_a_different_mod()
    {
        var s = KnownGood.Snapshot(new[] { M("Thing", true, "paks") }, Exe, Stamped);
        var d = KnownGood.Since(s, new[] { M("Thing", true, "lua") }, Exe);
        Assert.Equal(new[] { "Thing" }, d.TurnedOn);
        Assert.Equal(new[] { "Thing" }, d.Removed);
    }

    [Fact]
    public void A_long_list_is_shortened_with_a_count()
    {
        var s = KnownGood.Snapshot(Array.Empty<Mod>(), Exe, Stamped);
        var d = KnownGood.Since(s, Enumerable.Range(1, 7).Select(i => M($"Mod {i}", true)).ToArray(), Exe);
        Assert.Contains("you turned on Mod 1, Mod 2, Mod 3 and 4 more", d.Describe());
    }

    [Fact]
    public void An_unknown_executable_date_never_claims_an_update()
    {
        var s = KnownGood.Snapshot(new[] { M("A", true) }, null, Stamped);
        Assert.False(KnownGood.Since(s, new[] { M("A", true) }, Exe).GameUpdated);
        var s2 = KnownGood.Snapshot(new[] { M("A", true) }, Exe, Stamped);
        Assert.False(KnownGood.Since(s2, new[] { M("A", true) }, null).GameUpdated);
    }

    [Fact]
    public void It_round_trips_on_disk_as_camelCase_and_a_missing_or_torn_file_reads_as_none()
    {
        var dir = Path.Combine(_root, "data");
        Assert.Null(KnownGood.Load(dir));

        KnownGood.Save(dir, KnownGood.Snapshot(new[] { M("A", true, "paks") }, Exe, Stamped));
        var json = File.ReadAllText(Path.Combine(dir, "known-good.json"));
        Assert.Contains("\"stampedUtc\"", json);
        Assert.Contains("\"gameExeUtc\"", json);
        Assert.Contains("\"enabled\"", json);
        Assert.DoesNotContain("\"StampedUtc\"", json);

        var back = KnownGood.Load(dir)!;
        Assert.Equal(Stamped, back.StampedUtc);
        Assert.Equal("paks", Assert.Single(back.Enabled).Location);

        File.WriteAllText(Path.Combine(dir, "known-good.json"), "{ torn");
        Assert.Null(KnownGood.Load(dir));
    }

    [Fact]
    public void The_failure_line_carries_what_changed_and_a_clean_line_is_left_alone()
    {
        var s = KnownGood.Snapshot(new[] { M("A", true) }, Exe, Stamped);
        var failed = "ELDEN RING started. UE4SS did not load.";
        Assert.Equal(failed + " Since mods last loaded cleanly on Nov 8: you turned on B.",
            KnownGood.Explain(failed, new[] { O(LoadVerdict.DidNotRun) }, s, new[] { M("A", true), M("B", true) }, Exe));
        Assert.Equal(failed, KnownGood.Explain(failed, new[] { O(LoadVerdict.DidNotRun) }, null, new[] { M("A", true) }, Exe));
        Assert.Equal("ok", KnownGood.Explain("ok", new[] { O(LoadVerdict.Ran) }, s, new[] { M("B", true) }, Exe));
    }
}
