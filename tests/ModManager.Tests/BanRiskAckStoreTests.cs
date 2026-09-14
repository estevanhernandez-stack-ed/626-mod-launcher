using ModManager.Core;

namespace ModManager.Tests;

public class BanRiskAckStoreTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "banack-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void Ack_then_IsAcked_round_trips()
    {
        Assert.False(BanRiskAckStore.IsAcked(_tmp, "marvel-rivals"));
        BanRiskAckStore.Ack(_tmp, "marvel-rivals");
        Assert.True(BanRiskAckStore.IsAcked(_tmp, "marvel-rivals"));
        Assert.False(BanRiskAckStore.IsAcked(_tmp, "other-game"));
    }

    [Fact]
    public void Missing_or_corrupt_file_is_empty_not_an_error()
    {
        Assert.Empty(BanRiskAckStore.Load(_tmp));                       // missing dir
        Directory.CreateDirectory(_tmp);
        File.WriteAllText(Path.Combine(_tmp, "ban-risk-acks.json"), "{ not valid json");
        Assert.Empty(BanRiskAckStore.Load(_tmp));                       // corrupt -> empty, no throw
    }

    [Fact]
    public void Ack_is_idempotent()
    {
        BanRiskAckStore.Ack(_tmp, "g");
        BanRiskAckStore.Ack(_tmp, "g");
        Assert.Single(BanRiskAckStore.Load(_tmp));
    }

    [Fact]
    public void Ack_kinds_are_independent()
    {
        BanRiskAckStore.Ack(_tmp, "elden-ring");                                   // EnableMods by default
        Assert.True(BanRiskAckStore.IsAcked(_tmp, "elden-ring", BanRiskAck.EnableMods));
        Assert.False(BanRiskAckStore.IsAcked(_tmp, "elden-ring", BanRiskAck.WriteSaves));

        BanRiskAckStore.Ack(_tmp, "madden-nfl-27", BanRiskAck.WriteSaves);
        Assert.True(BanRiskAckStore.IsAcked(_tmp, "madden-nfl-27", BanRiskAck.WriteSaves));
        Assert.False(BanRiskAckStore.IsAcked(_tmp, "madden-nfl-27", BanRiskAck.EnableMods));
    }

    [Fact]
    public void Each_kind_writes_its_own_file_and_the_enable_file_keeps_its_name()
    {
        BanRiskAckStore.Ack(_tmp, "a", BanRiskAck.EnableMods);
        BanRiskAckStore.Ack(_tmp, "b", BanRiskAck.WriteSaves);

        Assert.True(File.Exists(Path.Combine(_tmp, "ban-risk-acks.json")));
        Assert.True(File.Exists(Path.Combine(_tmp, "ban-risk-save-acks.json")));
        Assert.DoesNotContain("\"b\"", File.ReadAllText(Path.Combine(_tmp, "ban-risk-acks.json")));
        Assert.DoesNotContain("\"a\"", File.ReadAllText(Path.Combine(_tmp, "ban-risk-save-acks.json")));
    }

    [Fact]
    public void An_enable_ack_file_from_before_the_change_is_still_honoured()
    {
        Directory.CreateDirectory(_tmp);
        File.WriteAllText(Path.Combine(_tmp, "ban-risk-acks.json"), "[\"monster-hunter-wilds\"]");

        Assert.True(BanRiskAckStore.IsAcked(_tmp, "monster-hunter-wilds"));
        Assert.False(BanRiskAckStore.IsAcked(_tmp, "monster-hunter-wilds", BanRiskAck.WriteSaves));
    }
}
