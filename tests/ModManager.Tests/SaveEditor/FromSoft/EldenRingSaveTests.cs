using System.IO;
using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.Tests.SaveEditor.FromSoft;

/// <summary>
/// Round-trip tests for the public <see cref="EldenRingSave"/> API. These tests build a synthetic
/// .sl2 via <see cref="EldenRingFixture"/>, read it through <see cref="EldenRingSave.ReadCharacters"/>,
/// then apply <see cref="EldenRingSave.WriteEdit"/> and re-read to confirm persistence.
///
/// Coverage matrix:
/// - Read happy-path: stats + runes + name + level from a one-character fixture.
/// - Write happy-path: edit persists across read; non-edited slots stay zero.
/// - Bounds: invalid slot indices throw.
/// - Integrity: tampered slot bytes (without MD5 recompute) are skipped on read, not thrown over.
/// </summary>
public class EldenRingSaveTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "er-save-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string CreateFixture(uint runes, byte vig, byte mnd, byte end_, byte str, byte dex, byte int_, byte fai, byte arc, string? name = null)
    {
        Directory.CreateDirectory(_tmp);
        var savePath = Path.Combine(_tmp, "ER0000.sl2");
        var bytes = name is null
            ? EldenRingFixture.BuildSaveWithOneCharacter(runes, vig, mnd, end_, str, dex, int_, fai, arc)
            : EldenRingFixture.BuildSaveWithOneCharacter(name, runes, vig, mnd, end_, str, dex, int_, fai, arc);
        File.WriteAllBytes(savePath, bytes);
        return savePath;
    }

    [Fact]
    public void Read_returns_one_slot_with_fixture_values()
    {
        var savePath = CreateFixture(
            runes: 198_500u,
            vig: 40, mnd: 16, end_: 30, str: 50, dex: 12, int_: 12, fai: 12, arc: 12);

        var slots = EldenRingSave.ReadCharacters(savePath);

        var slot = Assert.Single(slots);
        Assert.Equal(0, slot.SlotIndex);
        Assert.Equal(198_500u, slot.Runes);
        Assert.Equal(40, slot.Vig);
        Assert.Equal(16, slot.Mnd);
        Assert.Equal(30, slot.End);
        Assert.Equal(50, slot.Str);
        Assert.Equal(12, slot.Dex);
        Assert.Equal(12, slot.Int);
        Assert.Equal(12, slot.Fai);
        Assert.Equal(12, slot.Arc);
    }

    [Fact]
    public void Read_returns_character_name_from_save_header()
    {
        var savePath = CreateFixture(
            runes: 1000u, vig: 10, mnd: 10, end_: 10, str: 10, dex: 10, int_: 10, fai: 10, arc: 10,
            name: "Tarnished");

        var slots = EldenRingSave.ReadCharacters(savePath);

        var slot = Assert.Single(slots);
        Assert.Equal("Tarnished", slot.Name);
    }

    [Fact]
    public void Read_returns_level_computed_from_stats()
    {
        // Wretch baseline (all 10) = level 1.
        var savePath = CreateFixture(
            runes: 0u, vig: 10, mnd: 10, end_: 10, str: 10, dex: 10, int_: 10, fai: 10, arc: 10);

        var slots = EldenRingSave.ReadCharacters(savePath);

        var slot = Assert.Single(slots);
        Assert.Equal(1, slot.Level);
    }

    [Fact]
    public void WriteEdit_persists_new_values_and_round_trips()
    {
        var savePath = CreateFixture(
            runes: 100u, vig: 10, mnd: 10, end_: 10, str: 10, dex: 10, int_: 10, fai: 10, arc: 10);

        EldenRingSave.WriteEdit(savePath, slotIndex: 0, new CharacterEdit(
            Name: "Renamed",
            Runes: 1_000_000u,
            Vig: 50, Mnd: 16, End: 30, Str: 50, Dex: 12, Int: 12, Fai: 12, Arc: 12));

        var slots = EldenRingSave.ReadCharacters(savePath);
        var slot = Assert.Single(slots);
        Assert.Equal(1_000_000u, slot.Runes);
        Assert.Equal(50, slot.Vig);
        Assert.Equal(16, slot.Mnd);
        Assert.Equal(30, slot.End);
        Assert.Equal(50, slot.Str);
        Assert.Equal(12, slot.Dex);
        Assert.Equal(12, slot.Int);
        Assert.Equal(12, slot.Fai);
        Assert.Equal(12, slot.Arc);
        Assert.Equal("Renamed", slot.Name);
    }

    [Fact]
    public void WriteEdit_recomputes_slot_md5_so_round_trip_read_succeeds()
    {
        // If the slot MD5 weren't recomputed after the write, Read would skip the slot due to
        // the integrity check — and Assert.Single below would fail. This test exists to lock
        // that contract: WriteEdit must keep MD5 in sync with data.
        var savePath = CreateFixture(
            runes: 100u, vig: 10, mnd: 10, end_: 10, str: 10, dex: 10, int_: 10, fai: 10, arc: 10);

        EldenRingSave.WriteEdit(savePath, slotIndex: 0, new CharacterEdit(
            Name: "X", Runes: 42u,
            Vig: 11, Mnd: 11, End: 11, Str: 11, Dex: 11, Int: 11, Fai: 11, Arc: 11));

        var slots = EldenRingSave.ReadCharacters(savePath);
        Assert.Single(slots); // Reading succeeded — MD5 matched.
    }

    [Fact]
    public void WriteEdit_throws_on_invalid_slot_index()
    {
        var savePath = CreateFixture(
            runes: 0u, vig: 1, mnd: 1, end_: 1, str: 1, dex: 1, int_: 1, fai: 1, arc: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EldenRingSave.WriteEdit(savePath, slotIndex: 99,
                new CharacterEdit("y", 0u, 1, 1, 1, 1, 1, 1, 1, 1)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EldenRingSave.WriteEdit(savePath, slotIndex: -1,
                new CharacterEdit("y", 0u, 1, 1, 1, 1, 1, 1, 1, 1)));
    }

    [Fact]
    public void Md5_mismatch_on_read_skips_the_slot_rather_than_throws()
    {
        Directory.CreateDirectory(_tmp);
        var savePath = Path.Combine(_tmp, "ER0000.sl2");
        var bytes = EldenRingFixture.BuildSaveWithOneCharacter(
            runes: 1000u, vig: 10, mnd: 10, end_: 10, str: 10, dex: 10, int_: 10, fai: 10, arc: 10);
        // Tamper slot 0's data without recomputing the MD5.
        bytes[0x330 + 0x100] ^= 0xFF;
        File.WriteAllBytes(savePath, bytes);

        var slots = EldenRingSave.ReadCharacters(savePath);
        // Bad MD5 → slot is skipped, not thrown over.
        Assert.Empty(slots);
    }

    [Fact]
    public void ReadCharacters_throws_on_missing_file()
    {
        Assert.Throws<FileNotFoundException>(() =>
            EldenRingSave.ReadCharacters(Path.Combine(_tmp, "does-not-exist.sl2")));
    }

    [Fact]
    public void ReadCharacters_throws_on_truncated_file()
    {
        Directory.CreateDirectory(_tmp);
        var savePath = Path.Combine(_tmp, "tiny.sl2");
        File.WriteAllBytes(savePath, new byte[100]);
        // File too small to even contain the BND4 envelope + 10 slots — should report clearly.
        Assert.Throws<InvalidDataException>(() => EldenRingSave.ReadCharacters(savePath));
    }

    [Fact]
    public void WriteEdit_throws_on_inactive_slot()
    {
        // BuildEmptySave produces a file where every slot's active flag is 0. WriteEdit must
        // refuse rather than create a phantom character (header says active, body has nothing).
        Directory.CreateDirectory(_tmp);
        var savePath = Path.Combine(_tmp, "ER0000.sl2");
        File.WriteAllBytes(savePath, EldenRingFixture.BuildEmptySave());

        Assert.Throws<InvalidOperationException>(() =>
            EldenRingSave.WriteEdit(savePath, slotIndex: 0,
                new CharacterEdit("X", 0u, 1, 1, 1, 1, 1, 1, 1, 1)));
    }

    [Fact]
    public void Read_finds_save_header_by_name_when_section_is_relocated()
    {
        // Resilience claim: ReadCharacters looks up the save-header section by NAME
        // (USER_DATA011) via the BND4 file-table walk, not by the hardcoded 0x019003B0.
        //
        // We shift the save-header section + its MD5 by 0x1000 bytes and update the
        // USER_DATA011 entry's data_offset accordingly. A hardcoded reader would read
        // garbage (zeros) at the old offset, find an all-zero active flag, and return
        // zero characters. The walking reader follows the file table to the new offset
        // and returns the correct character.
        const int PadBytes = 0x1000;
        Directory.CreateDirectory(_tmp);
        var savePath = Path.Combine(_tmp, "ER0000.sl2");
        var bytes = EldenRingFixture.BuildSaveWithShiftedSaveHeader(
            name: "Shifted",
            runes: 12_345u,
            vig: 25, mnd: 14, end_: 22, str: 35, dex: 11, int_: 9, fai: 9, arc: 9,
            extraPadBytes: PadBytes);
        File.WriteAllBytes(savePath, bytes);

        var slots = EldenRingSave.ReadCharacters(savePath);

        var slot = Assert.Single(slots);
        Assert.Equal("Shifted", slot.Name);
        Assert.Equal(12_345u, slot.Runes);
        Assert.Equal(25, slot.Vig);
        Assert.Equal(14, slot.Mnd);
        Assert.Equal(22, slot.End);
        Assert.Equal(35, slot.Str);
        Assert.Equal(11, slot.Dex);
        Assert.Equal(9, slot.Int);
        Assert.Equal(9, slot.Fai);
        Assert.Equal(9, slot.Arc);
    }

    [Fact]
    public void Read_and_edit_round_trip_against_save_with_inventory()
    {
        // Real ER saves always have at least one inventory item (every starting class spawns
        // with a weapon). This fixture plants a 21-byte weapon entry at GA-items index 0, which
        // shifts the magic anchor from 0xA1CF (empty inventory) to 0xA1DC. The point of this
        // test is to lock that DiscoverMagicOffset finds the shifted anchor and reads/writes
        // stats from the correct place — without it, the previous EnsureEmptyInventoryOrThrow
        // guard would have rejected every real save.
        Directory.CreateDirectory(_tmp);
        var savePath = Path.Combine(_tmp, "ER0000.sl2");
        var bytes = EldenRingFixture.BuildSaveWithInventory("Tarnished",
            runes: 198_500u,
            vig: 40, mnd: 16, end_: 30, str: 50, dex: 12, int_: 12, fai: 12, arc: 12);
        File.WriteAllBytes(savePath, bytes);

        // i) DiscoverMagicOffset walks the weapon entry and lands on 0xA1DC.
        var slotData = bytes.AsSpan(0x330, SlotData.SlotSize);
        int discovered = EldenRingSave.DiscoverMagicOffset(slotData);
        Assert.Equal(0xA1DC, discovered);

        // ii) Reading stats works — the values we planted at the shifted anchor come back.
        var slots = EldenRingSave.ReadCharacters(savePath);
        var slot = Assert.Single(slots);
        Assert.Equal("Tarnished", slot.Name);
        Assert.Equal(198_500u, slot.Runes);
        Assert.Equal(40, slot.Vig);
        Assert.Equal(50, slot.Str);

        // iii) WriteEdit round-trips correctly. The post-write verification mask uses the
        // discovered anchor, so a wrong-anchor write would either fail VerifyPostWrite or fail
        // the round-trip read below.
        EldenRingSave.WriteEdit(savePath, slotIndex: 0, new CharacterEdit(
            Name: "Renamed",
            Runes: 1_000_000u,
            Vig: 50, Mnd: 18, End: 32, Str: 55, Dex: 14, Int: 14, Fai: 14, Arc: 14));

        var afterEdit = EldenRingSave.ReadCharacters(savePath);
        var edited = Assert.Single(afterEdit);
        Assert.Equal("Renamed", edited.Name);
        Assert.Equal(1_000_000u, edited.Runes);
        Assert.Equal(50, edited.Vig);
        Assert.Equal(18, edited.Mnd);
        Assert.Equal(32, edited.End);
        Assert.Equal(55, edited.Str);
        Assert.Equal(14, edited.Dex);
        Assert.Equal(14, edited.Int);
        Assert.Equal(14, edited.Fai);
        Assert.Equal(14, edited.Arc);
    }
}
