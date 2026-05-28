using ModManager.Core;

namespace ModManager.Tests;

public class SpaceCheckTests
{
    [Fact]
    public void Evaluate_ok_when_available_exceeds_payload_plus_floor()
    {
        var r = SpaceCheck.Evaluate("C:\\", payloadBytes: 100, availableBytes: 5L << 30);
        Assert.True(r.Ok);
        Assert.Equal(100 + (1L << 30), r.RequiredBytes);   // 1 GiB floor dominates a tiny payload
    }

    [Fact]
    public void Evaluate_not_ok_when_short()
    {
        var r = SpaceCheck.Evaluate("C:\\", payloadBytes: 10L << 30, availableBytes: 1L << 30);
        Assert.False(r.Ok);
    }

    [Fact]
    public void RequiredWithHeadroom_uses_percentage_when_it_exceeds_floor()
    {
        var payload = 20L << 30;                                   // 20 GiB
        var req = SpaceCheck.RequiredWithHeadroom(payload);        // 10% = 2 GiB margin > 1 GiB floor
        Assert.Equal(payload + (long)(payload * 0.10), req);
    }
}
