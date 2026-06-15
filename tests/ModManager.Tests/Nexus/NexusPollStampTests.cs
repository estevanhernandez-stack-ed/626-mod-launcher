using ModManager.Core;

namespace ModManager.Tests.Nexus;

// The per-game last-poll stamp that debounces the auto-check. Pure given a path: the App passes
// %LOCALAPPDATA%\ModManagerBuilder\last-nexus-poll-<gameId>.txt. ShouldPoll is the >24h-or-never
// gate; Read/Write round-trip the timestamp through a round-trip-kind string on disk.
public class NexusPollStampTests
{
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    [Fact]
    public void ShouldPoll_true_when_never_polled()
    {
        Assert.True(NexusPollStamp.ShouldPoll(null, DateTime.UtcNow, Day));
    }

    [Fact]
    public void ShouldPoll_true_when_older_than_interval()
    {
        var now = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var last = now.AddHours(-25);
        Assert.True(NexusPollStamp.ShouldPoll(last, now, Day));
    }

    [Fact]
    public void ShouldPoll_false_when_within_interval()
    {
        var now = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var last = now.AddHours(-1);
        Assert.False(NexusPollStamp.ShouldPoll(last, now, Day));
    }

    [Fact]
    public void ShouldPoll_false_exactly_at_interval()
    {
        var now = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var last = now.AddHours(-24);
        // exactly 24h elapsed is not yet "more than" the interval
        Assert.False(NexusPollStamp.ShouldPoll(last, now, Day));
    }

    [Fact]
    public void Read_returns_null_for_missing_file()
    {
        var path = Path.Combine(TestSupport.TempDir("pollstamp-"), "last-nexus-poll-eldenring.txt");
        Assert.Null(NexusPollStamp.Read(path));
    }

    [Fact]
    public void Read_returns_null_for_garbage_file()
    {
        var dir = TestSupport.TempDir("pollstamp-");
        var path = Path.Combine(dir, "last-nexus-poll-eldenring.txt");
        File.WriteAllText(path, "not a timestamp");
        Assert.Null(NexusPollStamp.Read(path));
    }

    [Fact]
    public void Write_then_Read_round_trips_the_stamp()
    {
        var dir = TestSupport.TempDir("pollstamp-");
        var path = Path.Combine(dir, "last-nexus-poll-eldenring.txt");
        var when = new DateTime(2024, 6, 1, 12, 30, 45, DateTimeKind.Utc);

        NexusPollStamp.Write(path, when);
        var read = NexusPollStamp.Read(path);

        Assert.NotNull(read);
        Assert.Equal(when, read!.Value);
        Assert.Equal(DateTimeKind.Utc, read.Value.Kind);
    }
}
