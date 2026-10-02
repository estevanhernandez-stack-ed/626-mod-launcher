using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// A6: the breadcrumb for a data-folder move. The move runs before the registry write, so a launcher
/// that died between them left the data at the new folder and the registration at the old one, with
/// no record a move happened.
/// </summary>
public class DataDirMoveJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ddmj-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private string From => Path.Combine(_root, "old");
    private string To => Path.Combine(_root, "new");

    private DataDirMoveRecord Record() => new("cp2077", From, To,
        new GameEntry { Id = "cp2077", GameName = "Cyberpunk 2077", Engine = "custom", DataDir = To }, DateTime.UtcNow);

    private GameEntry RegisteredAt(string dataDir) => new() { Id = "cp2077", GameName = "Cyberpunk 2077", Engine = "custom", DataDir = dataDir };

    private static Func<string, bool> Has(params string[] withData) => p => withData.Contains(p);

    // ---- the record on disk --------------------------------------------------------------------

    [Fact]
    public void A_record_round_trips_as_camelCase()
    {
        DataDirMoveJournal.Write(_root, Record());

        var json = File.ReadAllText(DataDirMoveJournal.PathFor(_root, "cp2077"));
        Assert.Contains("\"gameId\"", json);
        Assert.Contains("\"proposed\"", json);
        Assert.DoesNotContain("\"GameId\"", json);

        var back = Assert.Single(DataDirMoveJournal.Pending(_root));
        Assert.Equal(To, back.To);
        Assert.Equal(To, back.Proposed.DataDir);
    }

    [Fact]
    public void Clearing_removes_the_record()
    {
        DataDirMoveJournal.Write(_root, Record());
        DataDirMoveJournal.Clear(_root, "cp2077");

        Assert.Empty(DataDirMoveJournal.Pending(_root));
    }

    // Runs at startup: a broken record is skipped, never thrown on.
    [Fact]
    public void An_unreadable_record_is_skipped()
    {
        DataDirMoveJournal.Write(_root, Record());
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(DataDirMoveJournal.PathFor(_root, "x"))!, "broken.json"), "{ not json");

        Assert.Single(DataDirMoveJournal.Pending(_root));
    }

    [Fact]
    public void No_journal_folder_means_nothing_pending()
        => Assert.Empty(DataDirMoveJournal.Pending(Path.Combine(_root, "absent")));

    // ---- what a leftover record means ----------------------------------------------------------

    [Fact]
    public void Data_only_at_the_target_with_the_registration_at_the_source_finishes_the_save()
        => Assert.Equal(MoveRecovery.FinishSave, DataDirMoveJournal.Assess(Record(), RegisteredAt(From), Has(To)));

    [Fact]
    public void A_save_that_landed_needs_nothing()
        => Assert.Equal(MoveRecovery.NothingToDo, DataDirMoveJournal.Assess(Record(), RegisteredAt(To), Has(To)));

    [Fact]
    public void A_move_that_never_happened_or_was_put_back_needs_nothing()
        => Assert.Equal(MoveRecovery.NothingToDo, DataDirMoveJournal.Assess(Record(), RegisteredAt(From), Has(From)));

    // Ambiguous: never a guess made for the user.
    [Fact]
    public void Data_at_both_or_neither_needs_the_user()
    {
        Assert.Equal(MoveRecovery.NeedsYou, DataDirMoveJournal.Assess(Record(), RegisteredAt(From), Has(From, To)));
        Assert.Equal(MoveRecovery.NeedsYou, DataDirMoveJournal.Assess(Record(), RegisteredAt(From), Has()));
    }

    [Fact]
    public void A_game_removed_since_needs_nothing()
        => Assert.Equal(MoveRecovery.NothingToDo, DataDirMoveJournal.Assess(Record(), null, Has(To)));

    // The user changed the folders again since: not this move's to finish.
    [Fact]
    public void A_registration_changed_again_since_needs_nothing()
        => Assert.Equal(MoveRecovery.NothingToDo,
            DataDirMoveJournal.Assess(Record(), RegisteredAt(Path.Combine(_root, "elsewhere")), Has(To)));

    [Fact]
    public void The_messages_name_the_folders()
    {
        Assert.Contains(To, DataDirMoveJournal.FinishedMessage(Record(), "Cyberpunk 2077"));
        var needs = DataDirMoveJournal.NeedsYouMessage(Record(), "Cyberpunk 2077");
        Assert.Contains(From, needs);
        Assert.Contains(To, needs);
        Assert.Contains("Nothing was moved or deleted", needs);
    }
}
