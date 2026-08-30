using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.Tests.SaveEditor.FromSoft;

public class CharacterSlotTests
{
    [Fact]
    public void CharacterSlot_carries_identity_and_stats()
    {
        var slot = new CharacterSlot(
            SlotIndex: 0,
            Name: "Yuka",
            Class: "Vagabond",
            Level: 120,
            Runes: 198_500,
            Vig: 40, Mnd: 16, End: 30, Str: 50, Dex: 12, Int: 12, Fai: 12, Arc: 12,
            SteamId: "76500000000000000");

        Assert.Equal("Yuka", slot.Name);
        Assert.Equal(120, slot.Level);
        Assert.Equal(198_500u, slot.Runes);
        Assert.Equal(40, slot.Vig);
        Assert.Equal("76500000000000000", slot.SteamId);
    }

    [Fact]
    public void CharacterEdit_carries_changed_fields_only()
    {
        var edit = new CharacterEdit(
            Name: "Renamed",
            Runes: 1_000_000u,
            Vig: 50, Mnd: 16, End: 30, Str: 50, Dex: 12, Int: 12, Fai: 12, Arc: 12);

        Assert.Equal("Renamed", edit.Name);
        Assert.Equal(1_000_000u, edit.Runes);
        Assert.Equal(50, edit.Vig);
    }
}

/// <summary>
/// The account a FromSoft save belongs to.
///
/// <para>Deferred since the original editor work, with the offset noted in a comment. It is worth
/// having now for a reason the transport work turned up: a shareable bundle removes FILES, and this is
/// a FIELD — so a game that stamps its account id into the save can never be laundered by dropping
/// <c>steam_autocloud.vdf</c>. Reading it is how a curator finds that out.</para>
/// </summary>
public class EldenRingSteamIdTests
{
    private static byte[] SaveHeaderAt(int start, ulong id, int size = 0x2000)
    {
        var b = new byte[start + size];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(
            b.AsSpan(start + ModManager.Core.SaveEditor.FromSoft.EldenRingSave.SteamIdRelative), id);
        return b;
    }

    [Fact]
    public void The_id_is_read_four_bytes_into_the_save_header_section()
    {
        // Verified against a real save folder: the same value at this place in all seven files -
        // .sl2, .co2, .err and their backups - including an .sl2 holding no characters, because it
        // lives in the PROFILE block rather than in any slot.
        var bytes = SaveHeaderAt(0x1000, 76561197969211145UL);

        Assert.Equal("76561197969211145",
            ModManager.Core.SaveEditor.FromSoft.EldenRingSave.ReadSteamId(bytes, 0x1000));
    }

    [Fact]
    public void Bytes_that_are_not_an_account_id_read_as_absent_rather_than_as_a_number()
    {
        // The guard that matters. If a patch moves the section, eight bytes at a fixed place still
        // yield a NUMBER, not an error - and a plausible-looking wrong account shown beside somebody's
        // character is worse than showing nothing.
        foreach (var notAnId in new ulong[] { 0, 1, 12345, ulong.MaxValue, 76561197960265727UL })
            Assert.Equal("", ModManager.Core.SaveEditor.FromSoft.EldenRingSave.ReadSteamId(
                SaveHeaderAt(0x1000, notAnId), 0x1000));
    }

    [Fact]
    public void A_truncated_or_impossible_offset_is_empty_and_never_a_throw()
    {
        var bytes = SaveHeaderAt(0x1000, 76561197969211145UL);

        Assert.Equal("", ModManager.Core.SaveEditor.FromSoft.EldenRingSave.ReadSteamId(bytes, bytes.Length - 2));
        Assert.Equal("", ModManager.Core.SaveEditor.FromSoft.EldenRingSave.ReadSteamId(bytes, -8));
        Assert.Equal("", ModManager.Core.SaveEditor.FromSoft.EldenRingSave.ReadSteamId(Array.Empty<byte>(), 0));
    }
}
