using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.Tests.SaveEditor.FromSoft;

/// <summary>
/// Round-trip tests for <see cref="SlotData"/> reads/writes against the FIXTURE anchor
/// (all-zero GA-items region, where <c>magic_offset = 0x20 + 5120*8 + 0x1AF = 0xA1CF</c>).
/// Real ER saves use a runtime-discovered anchor — Task 4 wires that up. Until then, all
/// SlotData operations assume the fixture's empty-inventory layout.
/// </summary>
public class SlotDataOffsetTests
{
    // Allocate the FULL slot size from the research note (0x280000 = 2.5 MB). Tests run in a
    // temp dir but the byte buffer is in memory only. .NET handles this size fine.
    private static byte[] BlankSlot() => new byte[SlotData.SlotSize];

    [Fact]
    public void Runes_round_trip_as_uint32_little_endian()
    {
        var slot = BlankSlot();
        SlotData.WriteRunes(slot, 198_500u);
        Assert.Equal(198_500u, SlotData.ReadRunes(slot));
    }

    [Fact]
    public void Runes_zero_round_trips()
    {
        var slot = BlankSlot();
        SlotData.WriteRunes(slot, 0u);
        Assert.Equal(0u, SlotData.ReadRunes(slot));
    }

    [Fact]
    public void All_eight_stats_round_trip_independently()
    {
        var slot = BlankSlot();
        SlotData.WriteStats(slot, vig: 40, mnd: 16, end_: 30, str: 50, dex: 12, int_: 18, fai: 20, arc: 25);
        var stats = SlotData.ReadStats(slot);
        Assert.Equal(40, stats.Vig);
        Assert.Equal(16, stats.Mnd);
        Assert.Equal(30, stats.End);
        Assert.Equal(50, stats.Str);
        Assert.Equal(12, stats.Dex);
        Assert.Equal(18, stats.Int);
        Assert.Equal(20, stats.Fai);
        Assert.Equal(25, stats.Arc);
    }

    [Fact]
    public void Stats_writes_do_not_corrupt_runes_or_neighboring_bytes()
    {
        var slot = BlankSlot();
        SlotData.WriteRunes(slot, 12_345u);
        SlotData.WriteStats(slot, 40, 16, 30, 50, 12, 18, 20, 25);
        Assert.Equal(12_345u, SlotData.ReadRunes(slot));   // runes intact
        Assert.Equal((byte)40, SlotData.ReadStats(slot).Vig);
    }

    [Fact]
    public void Level_from_stats_sums_eight_attributes_with_baseline()
    {
        // Wretch (all 10) = level 1. The class baseline puts the sum at (10*8)=80, so level
        // = 80 - 79 = 1. ER's "starting level + leveled attribute points" math.
        var wretch = new SlotStats(10, 10, 10, 10, 10, 10, 10, 10);
        Assert.Equal(1, SlotData.LevelFromStats(wretch));

        // All 11 = 88 → level 9.
        var leveled = new SlotStats(11, 11, 11, 11, 11, 11, 11, 11);
        Assert.Equal(9, SlotData.LevelFromStats(leveled));
    }
}
