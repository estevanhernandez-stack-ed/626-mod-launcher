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
        bytes[0x310 + 0x100] ^= 0xFF;
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
}
