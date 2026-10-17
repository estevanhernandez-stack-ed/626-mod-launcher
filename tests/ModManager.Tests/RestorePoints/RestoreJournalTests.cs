using System.Text.Json;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// The breadcrumb a Kind restore leaves while it writes over a live game. A launcher that dies halfway
/// leaves some files from the snapshot and some from before, with nothing to say it happened; the record
/// outlives the process so the next start can say so and name the way back. Pure Core: the record is
/// written, read, acknowledged and described here, and nothing in this class ever restores anything.
/// </summary>
public class RestoreJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-journal-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static readonly DateTime Started = new(2026, 10, 16, 13, 0, 0, DateTimeKind.Utc);

    private static InterruptedRestore Sample()
        => new("20261016-120000", "20261016-130000-palworld", "palworld", "Palworld", Started);

    [Fact]
    public void A_record_round_trips_as_camelCase_with_every_field()
    {
        RestoreJournal.Write(_root, Sample());

        var json = File.ReadAllText(RestoreJournal.PathFor(_root));
        foreach (var key in new[] { "restorePoint", "undoSnapshot", "gameId", "gameName", "startedUtc" })
            Assert.Contains($"\"{key}\"", json);
        foreach (var key in new[] { "RestorePoint", "UndoSnapshot", "GameId", "GameName", "StartedUtc" })
            Assert.DoesNotContain($"\"{key}\"", json);

        var read = RestoreJournal.Read(_root);
        Assert.Equal(Sample(), read);
    }

    [Fact]
    public void The_record_lives_in_the_data_root_beside_games_json()
    {
        RestoreJournal.Write(_root, Sample());
        Assert.Equal(Path.Combine(_root, "pending-restore.json"), RestoreJournal.PathFor(_root));
        Assert.True(File.Exists(RestoreJournal.PathFor(_root)));
    }

    [Fact]
    public void Writing_leaves_no_temp_file_behind()
    {
        RestoreJournal.Write(_root, Sample());
        Assert.DoesNotContain(Directory.GetFiles(_root), f => f.Contains(".tmp-"));
    }

    [Fact]
    public void Read_is_null_when_there_is_no_record()
        => Assert.Null(RestoreJournal.Read(_root));

    [Fact]
    public void Clear_removes_the_record_and_is_safe_when_there_is_none()
    {
        RestoreJournal.Write(_root, Sample());
        RestoreJournal.Clear(_root);
        Assert.Null(RestoreJournal.Read(_root));
        RestoreJournal.Clear(_root);   // already gone: no throw
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"restorePoint\":\"x\"}")]                 // no undo snapshot: nothing to point the player at
    [InlineData("{\"undoSnapshot\":\"\",\"gameName\":\"G\"}")]
    public void A_corrupt_or_empty_record_reads_as_nothing_and_never_throws(string content)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(RestoreJournal.PathFor(_root), content);
        Assert.Null(RestoreJournal.Read(_root));
    }

    [Fact]
    public void A_record_written_by_hand_in_another_case_still_reads()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(RestoreJournal.PathFor(_root),
            "{\"RestorePoint\":\"a\",\"UndoSnapshot\":\"b\",\"GameId\":\"g\",\"GameName\":\"G\",\"StartedUtc\":\"2026-10-16T13:00:00Z\"}");
        var r = RestoreJournal.Read(_root);
        Assert.NotNull(r);
        Assert.Equal("b", r!.UndoSnapshot);
    }

    [Fact]
    public void The_message_names_the_game_the_time_and_the_way_back_and_promises_nothing_was_done_for_them()
    {
        var m = RestoreJournal.Message(Sample());

        Assert.Contains("Palworld", m);
        Assert.Contains("20261016-130000-palworld", m);
        Assert.Contains("Settings", m);
        Assert.Contains("Restore points", m);
        Assert.Contains(Started.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture), m);
        Assert.DoesNotContain("—", m);
    }

    [Fact]
    public void The_orchestrator_detects_acknowledges_and_forgets_the_record()
    {
        var orch = new RestorePointOrchestrator(_root, Path.Combine(_root, "restore-points"), "0.25.0",
            new NoGames(), new NoNexus(), new NoProbe());
        Assert.Null(orch.DetectInterruptedRestore());

        RestoreJournal.Write(_root, Sample());
        Assert.Equal(Sample(), orch.DetectInterruptedRestore());
        Assert.Equal(Sample(), orch.DetectInterruptedRestore());   // looking does not consume it

        orch.AcknowledgeInterruptedRestore();
        Assert.Null(orch.DetectInterruptedRestore());
        Assert.False(File.Exists(RestoreJournal.PathFor(_root)));
    }

    [Fact]
    public void Acknowledging_removes_only_the_record_never_a_restore_point()
    {
        var pointDir = Path.Combine(_root, "restore-points", "20261016-130000-palworld");
        Directory.CreateDirectory(pointDir);
        File.WriteAllText(Path.Combine(pointDir, "keep.txt"), "x");
        RestoreJournal.Write(_root, Sample());
        var orch = new RestorePointOrchestrator(_root, Path.Combine(_root, "restore-points"), "0.25.0",
            new NoGames(), new NoNexus(), new NoProbe());

        orch.AcknowledgeInterruptedRestore();

        Assert.True(File.Exists(Path.Combine(pointDir, "keep.txt")));
    }

    private sealed class NoNexus : INexusGate { public bool IsConnected => false; public void DeleteStoredKey() { } }
    private sealed class NoProbe : IGameRunningProbe { public bool AnyRunning(GameEntry g) => false; }
    private sealed class NoGames : IGameProvider
    {
        public IReadOnlyList<GameEntry> Games => Array.Empty<GameEntry>();
        public GameContext ContextFor(GameEntry g) => Scanner.GameContext(g);
        public void Reload() { }
    }
}
