using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// The pure rule behind the automatic cleanup of <c>beforeRestore</c> undo snapshots: keep a game's newest
/// three, never touch one younger than a day, and never select anything that is not provably one of that
/// game's own sealed undo snapshots.
/// </summary>
public class RestorePointRetentionTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    private const string Game = "palworld";

    private static string Stamp(DateTime t) => t.ToString("yyyyMMdd-HHmmss");

    // An undo snapshot of `game` taken `age` ago, exactly as CaptureGameOnlyAsync names and records it.
    private static RestorePointFacts Undo(TimeSpan age, string game = Game, string? kind = RestorePointKinds.BeforeRestore,
        bool sealed_ = true, int schema = 1, string[]? games = null)
    {
        var name = $"{Stamp(Now - age)}-{game}";
        return new RestorePointFacts(name, sealed_, schema, kind, name, games ?? new[] { game });
    }

    private static RestorePointFacts[] FiveOld() => new[]
    {
        Undo(TimeSpan.FromDays(2)), Undo(TimeSpan.FromDays(3)), Undo(TimeSpan.FromDays(4)),
        Undo(TimeSpan.FromDays(5)), Undo(TimeSpan.FromDays(6)),
    };

    [Fact]
    public void Five_old_undo_snapshots_lose_the_two_oldest_and_keep_the_three_newest()
    {
        var pts = FiveOld();

        var doomed = RestorePointRetention.BeforeRestoreToPrune(pts, Game, Now);

        Assert.Equal(new[] { pts[4].FolderName, pts[3].FolderName }.OrderBy(n => n), doomed.OrderBy(n => n));
    }

    [Fact]
    public void Input_order_does_not_matter()
    {
        var pts = FiveOld().Reverse().ToArray();
        var doomed = RestorePointRetention.BeforeRestoreToPrune(pts, Game, Now);
        Assert.Equal(2, doomed.Count);
        Assert.Contains(Undo(TimeSpan.FromDays(6)).FolderName, doomed);
        Assert.Contains(Undo(TimeSpan.FromDays(5)).FolderName, doomed);
    }

    [Fact]
    public void Three_or_fewer_select_nothing()
    {
        Assert.Empty(RestorePointRetention.BeforeRestoreToPrune(FiveOld().Take(3).ToArray(), Game, Now));
        Assert.Empty(RestorePointRetention.BeforeRestoreToPrune(Array.Empty<RestorePointFacts>(), Game, Now));
    }

    [Fact]
    public void One_younger_than_a_day_is_never_selected_even_as_the_fourth_newest()
    {
        var young = Undo(TimeSpan.FromHours(5));                       // 4th newest, but not a day old
        var pts = new[]
        {
            Undo(TimeSpan.FromHours(1)), Undo(TimeSpan.FromHours(2)), Undo(TimeSpan.FromHours(3)),
            young,
            Undo(TimeSpan.FromDays(3)), Undo(TimeSpan.FromDays(4)),
        };

        var doomed = RestorePointRetention.BeforeRestoreToPrune(pts, Game, Now);

        Assert.DoesNotContain(young.FolderName, doomed);
        Assert.Equal(2, doomed.Count);                                  // the two old ones
        Assert.All(doomed, n => Assert.True(string.CompareOrdinal(n, Stamp(Now - TimeSpan.FromDays(1))) < 0));
    }

    [Fact]
    public void The_day_is_measured_to_the_second_and_a_future_stamp_counts_as_young()
    {
        var edgeYoung = Undo(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        var edgeOld = Undo(TimeSpan.FromHours(24));
        var future = Undo(TimeSpan.FromHours(-3));
        var pts = new[] { Undo(TimeSpan.FromMinutes(1)), Undo(TimeSpan.FromMinutes(2)), Undo(TimeSpan.FromMinutes(3)),
                          future, edgeYoung, edgeOld };

        var doomed = RestorePointRetention.BeforeRestoreToPrune(pts, Game, Now);

        Assert.Equal(new[] { edgeOld.FolderName }, doomed);
    }

    [Fact]
    public void Only_this_games_sealed_beforeRestore_points_are_ever_selected()
    {
        var mine = FiveOld();
        var ageOld = TimeSpan.FromDays(30);
        var intruders = new[]
        {
            Undo(ageOld, kind: RestorePointKinds.PatchDay),                   // a Patch Day point
            Undo(ageOld.Add(TimeSpan.FromDays(1)), kind: null),               // a Safe Clear point has no kind
            Undo(ageOld.Add(TimeSpan.FromDays(2)), kind: "somethingNewer"),   // a kind from a newer build
            Undo(ageOld.Add(TimeSpan.FromDays(3)), kind: "BEFORERESTORE"),    // not the exact kind
            Undo(ageOld.Add(TimeSpan.FromDays(4)), game: "valheim"),          // another game
            Undo(ageOld.Add(TimeSpan.FromDays(5)), sealed_: false),           // not sealed
            Undo(ageOld.Add(TimeSpan.FromDays(6)), schema: 3),                // sealed by a newer build
            Undo(ageOld.Add(TimeSpan.FromDays(7)), games: new[] { Game, "valheim" }),   // not a single-game point
            Undo(ageOld.Add(TimeSpan.FromDays(8)), games: Array.Empty<string>()),
        };

        var doomed = RestorePointRetention.BeforeRestoreToPrune(mine.Concat(intruders).ToList(), Game, Now);

        Assert.Equal(2, doomed.Count);
        Assert.All(doomed, n => Assert.Contains(n, mine.Select(p => p.FolderName)));
    }

    [Fact]
    public void A_point_whose_record_does_not_match_its_folder_is_not_trusted()
    {
        var name = $"{Stamp(Now - TimeSpan.FromDays(40))}-{Game}";
        var odd = new[]
        {
            new RestorePointFacts("renamed-by-hand", true, 1, RestorePointKinds.BeforeRestore, name, new[] { Game }),
            new RestorePointFacts(name, true, 1, RestorePointKinds.BeforeRestore, null, new[] { Game }),
            new RestorePointFacts(name, true, 1, RestorePointKinds.BeforeRestore, "not-a-time", new[] { Game }),
            new RestorePointFacts($"{Stamp(Now - TimeSpan.FromDays(41))}-other", true, 1, RestorePointKinds.BeforeRestore,
                $"{Stamp(Now - TimeSpan.FromDays(41))}-other", new[] { Game }),   // name's game is not the record's game
            new RestorePointFacts("..\\escape", true, 1, RestorePointKinds.BeforeRestore, "..\\escape", new[] { Game }),
        };

        Assert.Empty(RestorePointRetention.BeforeRestoreToPrune(odd.Concat(FiveOld().Take(3)).ToList(), Game, Now));
    }

    [Fact]
    public void Protected_names_survive_even_when_they_are_the_oldest()
    {
        var pts = FiveOld();
        var protect = new[] { pts[4].FolderName.ToUpperInvariant() };   // case-insensitive, as the file system is

        var doomed = RestorePointRetention.BeforeRestoreToPrune(pts, Game, Now, protect);

        Assert.Equal(new[] { pts[3].FolderName }, doomed);
    }

    [Fact]
    public void A_protected_point_still_takes_its_rank_so_the_rest_are_not_pruned_harder()
    {
        var pts = FiveOld();
        // The newest is protected: it is still one of the three kept, so exactly two are lost.
        var doomed = RestorePointRetention.BeforeRestoreToPrune(pts, Game, Now, new[] { pts[0].FolderName });
        Assert.Equal(2, doomed.Count);
    }

    [Fact]
    public void Keep_and_age_can_be_tuned_and_nonsense_is_refused()
    {
        var pts = FiveOld();
        Assert.Equal(4, RestorePointRetention.BeforeRestoreToPrune(pts, Game, Now, keep: 1).Count);
        Assert.Empty(RestorePointRetention.BeforeRestoreToPrune(pts, Game, Now, minAge: TimeSpan.FromDays(30)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RestorePointRetention.BeforeRestoreToPrune(pts, Game, Now, keep: 0));
    }

    [Fact]
    public void Defaults_are_three_and_a_day()
    {
        Assert.Equal(3, RestorePointRetention.KeepBeforeRestore);
        Assert.Equal(TimeSpan.FromHours(24), RestorePointRetention.MinAge);
    }

    // ---- the journal's record of what is in flight ----

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rp-ret-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public void The_journal_names_the_point_and_the_undo_snapshot_and_fails_closed_on_a_record_it_cannot_read()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            Assert.Empty(RestoreJournal.NamedPoints(_dir)!);                                   // no record: nothing named

            RestoreJournal.Write(_dir, new InterruptedRestore("20261001-000000-palworld", "20261016-130000-palworld", "palworld", "Palworld", Now));
            var named = RestoreJournal.NamedPoints(_dir)!;
            Assert.Contains("20261001-000000-palworld", named);
            Assert.Contains("20261016-130000-PALWORLD", named);

            // A record Read() would not trust (no game name) still names its points.
            File.WriteAllText(RestoreJournal.PathFor(_dir), "{\"restorePoint\":\"a\",\"undoSnapshot\":\"b\"}");
            Assert.Null(RestoreJournal.Read(_dir));
            Assert.Equal(new[] { "a", "b" }, RestoreJournal.NamedPoints(_dir)!.OrderBy(n => n));

            File.WriteAllText(RestoreJournal.PathFor(_dir), "not json");
            Assert.Null(RestoreJournal.NamedPoints(_dir));                                     // unreadable: caller must not delete
            File.WriteAllText(RestoreJournal.PathFor(_dir), "[1]");
            Assert.Null(RestoreJournal.NamedPoints(_dir));
        }
        finally { try { Directory.Delete(_dir, recursive: true); } catch { } }
    }
}
