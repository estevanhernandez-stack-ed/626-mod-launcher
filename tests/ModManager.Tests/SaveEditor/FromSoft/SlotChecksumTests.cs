using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.Tests.SaveEditor.FromSoft;

public class SlotChecksumTests
{
    [Fact]
    public void ComputeMd5_is_deterministic_for_the_same_input()
    {
        var payload = new byte[1024];
        new Random(42).NextBytes(payload);

        var first = SlotChecksum.ComputeMd5(payload);
        var second = SlotChecksum.ComputeMd5(payload);

        Assert.Equal(16, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void VerifyMd5_returns_true_for_matching_pair()
    {
        var payload = new byte[1024];
        new Random(7).NextBytes(payload);
        var checksum = SlotChecksum.ComputeMd5(payload);

        Assert.True(SlotChecksum.VerifyMd5(checksum, payload));
    }

    [Fact]
    public void VerifyMd5_returns_false_for_tampered_payload()
    {
        var payload = new byte[1024];
        new Random(7).NextBytes(payload);
        var checksum = SlotChecksum.ComputeMd5(payload);

        // Tamper one byte.
        payload[100] ^= 0xFF;
        Assert.False(SlotChecksum.VerifyMd5(checksum, payload));
    }

    [Fact]
    public void VerifyMd5_returns_false_for_wrong_length_checksum()
    {
        var payload = new byte[1024];
        Assert.False(SlotChecksum.VerifyMd5(new byte[15], payload));
        Assert.False(SlotChecksum.VerifyMd5(new byte[17], payload));
    }
}
