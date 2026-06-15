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
}
