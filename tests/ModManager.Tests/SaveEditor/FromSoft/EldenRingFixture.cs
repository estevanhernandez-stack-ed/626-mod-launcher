using System.Text;
using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.Tests.SaveEditor.FromSoft;

/// <summary>
/// Builds a synthetic ER <c>.sl2</c> byte array entirely in memory. The output is structurally
/// parseable by a BenGrn-style "use the SLOT_START_INDEX + stride formula" reader. It is NOT a
/// byte-identical real save — the BND4 file-table entries are minimal (enough for slot location,
/// not full BND4 v4 compliance), and the save-header section is the bare minimum to carry an
/// active flag + per-slot name/level/playtime summary.
///
/// NO real save files are committed to the repo. Test characters are synthesized with given
/// runes + stats; all other slot bytes are zero.
///
/// Task 4 extension — the buffer now extends past the 10 slots to include the save-header
/// section (0x19003B0..0x19603B0). Slot 0's active flag is set, slot 0's per-slot summary
/// carries the character name + computed level, and the save-header MD5 (at 0x019003A0) is
/// recomputed over [0x19003B0, 0x19603B0).
/// </summary>
public static class EldenRingFixture
{
    public const int SlotCount = 10;
    public const int SlotDataSize = SlotData.SlotSize;        // 0x280000
    public const int SlotStride = SlotDataSize + 0x10;         // 0x280010 (16-byte MD5 + data)
    public const int BndHeaderSize = 0x300;                    // BND4 header + file table

    public const int SlotsRegionStart = BndHeaderSize;         // 0x300
    public const int FirstSlotMd5Offset = SlotsRegionStart;    // 0x300
    public const int FirstSlotDataOffset = SlotsRegionStart + 0x10; // 0x310

    // --- Save-header section (BenGrn SaveGame.cs constants) ---
    public const int SaveHeaderMd5Offset = 0x019003A0;             // 16 bytes immediately before header
    public const int SaveHeadersSectionStart = 0x019003B0;
    public const int SaveHeadersSectionLength = 0x60000;
    public const int SaveHeaderTotalEnd = SaveHeadersSectionStart + SaveHeadersSectionLength; // 0x19603B0
    public const int CharActiveStatusOffset = 0x01901D04;          // 10 bytes (one per slot)
    public const int PerSlotSummaryStart = 0x01901D0E;             // slot 0 summary
    public const int PerSlotSummaryStride = 0x24C;
    public const int CharNameOffsetInSummary = 0x00;               // UTF-16
    public const int CharNameLengthBytes = 0x22;                   // 17 chars + null
    public const int CharLevelOffsetInSummary = 0x22;              // int16
    public const int CharPlayedSecondsOffsetInSummary = 0x26;      // int32

    public const string DefaultFixtureName = "TestChar";

    /// <summary>Build a minimal .sl2 with all 10 slots blank EXCEPT slot 0, which carries the
    /// given runes + stats. Slot 0 is marked active in the save-header active-flag array,
    /// and its per-slot summary is populated with <see cref="DefaultFixtureName"/> + the
    /// computed level + zero playtime. Slot + save-header MD5s recomputed.</summary>
    public static byte[] BuildSaveWithOneCharacter(uint runes,
        byte vig, byte mnd, byte end_, byte str, byte dex, byte int_, byte fai, byte arc)
        => BuildSaveWithOneCharacter(DefaultFixtureName, runes, vig, mnd, end_, str, dex, int_, fai, arc);

    /// <summary>Same as the parameterless-name overload but takes an explicit character name
    /// (UTF-16 in the save header; truncated to 17 chars).</summary>
    public static byte[] BuildSaveWithOneCharacter(string name, uint runes,
        byte vig, byte mnd, byte end_, byte str, byte dex, byte int_, byte fai, byte arc)
    {
        // The buffer must reach past the save-header MD5 region. SaveHeaderTotalEnd = 0x19603B0
        // is the end of the hashed region; everything past the slots is zero-filled by default.
        var totalSize = SaveHeaderTotalEnd;
        var buffer = new byte[totalSize];

        WriteBnd4Header(buffer);

        // Populate slot 0's data (runes + stats) at the fixture anchor.
        var slot0Data = buffer.AsSpan(FirstSlotDataOffset, SlotDataSize);
        SlotData.WriteRunes(slot0Data, runes, SlotData.FixtureMagicOffset);
        SlotData.WriteStats(slot0Data, vig, mnd, end_, str, dex, int_, fai, arc, SlotData.FixtureMagicOffset);

        // Compute and write slot 0's MD5.
        var slot0Md5Region = buffer.AsSpan(FirstSlotMd5Offset, 0x10);
        SlotChecksum.ComputeMd5(slot0Data).CopyTo(slot0Md5Region);

        // Slots 1-9 stay zero-filled; their MD5s match MD5(0x280000 zero bytes). Compute once
        // and copy into each slot's MD5 region.
        var zeroSlotMd5 = SlotChecksum.ComputeMd5(new byte[SlotDataSize]);
        for (int i = 1; i < SlotCount; i++)
        {
            var md5Region = buffer.AsSpan(FirstSlotMd5Offset + i * SlotStride, 0x10);
            zeroSlotMd5.CopyTo(md5Region);
        }

        // --- Save-header section ---
        // Active-flag array: mark slot 0 active, others inactive.
        buffer[CharActiveStatusOffset + 0] = 1;

        // Slot 0 summary — name (UTF-16), level (int16), playtime (int32).
        var slot0Summary = buffer.AsSpan(PerSlotSummaryStart, PerSlotSummaryStride);
        WriteCharacterName(slot0Summary, name);
        var stats = new SlotStats(vig, mnd, end_, str, dex, int_, fai, arc);
        BitConverter.TryWriteBytes(slot0Summary.Slice(CharLevelOffsetInSummary, 2), (short)SlotData.LevelFromStats(stats));
        BitConverter.TryWriteBytes(slot0Summary.Slice(CharPlayedSecondsOffsetInSummary, 4), 0);

        // Save-header MD5: covers [SaveHeadersSectionStart, SaveHeaderTotalEnd).
        var saveHeaderRegion = buffer.AsSpan(SaveHeadersSectionStart, SaveHeadersSectionLength);
        var saveHeaderMd5Region = buffer.AsSpan(SaveHeaderMd5Offset, 0x10);
        SlotChecksum.ComputeMd5(saveHeaderRegion).CopyTo(saveHeaderMd5Region);

        return buffer;
    }

    private static void WriteCharacterName(Span<byte> summary, string name)
    {
        // Clear the name region first.
        summary.Slice(CharNameOffsetInSummary, CharNameLengthBytes).Clear();
        // UTF-16 LE. Cap at 16 chars to leave room for the null terminator.
        var capped = name.Length > 16 ? name.Substring(0, 16) : name;
        var nameBytes = Encoding.Unicode.GetBytes(capped);
        nameBytes.CopyTo(summary.Slice(CharNameOffsetInSummary, Math.Min(nameBytes.Length, CharNameLengthBytes)));
    }

    /// <summary>Build a minimal .sl2 with ALL 10 slots inactive (active-flag array zeroed) and
    /// every slot body zero-filled. The slot MD5s match MD5(zero-filled slot data); the save-
    /// header MD5 covers the zero-filled header region. Used to exercise the inactive-slot guard
    /// in <see cref="EldenRingSave.WriteEdit"/>.</summary>
    public static byte[] BuildEmptySave()
    {
        var totalSize = SaveHeaderTotalEnd;
        var buffer = new byte[totalSize];

        WriteBnd4Header(buffer);

        // All 10 slots zero — compute MD5 once and copy into each slot's MD5 region.
        var zeroSlotMd5 = SlotChecksum.ComputeMd5(new byte[SlotDataSize]);
        for (int i = 0; i < SlotCount; i++)
        {
            var md5Region = buffer.AsSpan(FirstSlotMd5Offset + i * SlotStride, 0x10);
            zeroSlotMd5.CopyTo(md5Region);
        }

        // Active-flag array stays all zeros. Per-slot summaries stay zeroed.
        // Save-header MD5 covers the zero-filled section.
        var saveHeaderRegion = buffer.AsSpan(SaveHeadersSectionStart, SaveHeadersSectionLength);
        var saveHeaderMd5Region = buffer.AsSpan(SaveHeaderMd5Offset, 0x10);
        SlotChecksum.ComputeMd5(saveHeaderRegion).CopyTo(saveHeaderMd5Region);

        return buffer;
    }

    /// <summary>Build a save like <see cref="BuildSaveWithOneCharacter"/> but with a real weapon
    /// entry planted at GA-items index 0. The weapon entry is 21 bytes (per alfizari Item.from_bytes
    /// for handle type-bits 0x80000000), which shifts the magic anchor:
    /// <code>
    /// end_of_ga = 0x20 + 21 + 5119 * 8 = 0xA02D
    /// anchor    = end_of_ga + 0x1AF     = 0xA1DC   (vs 0xA1CF for an all-empty fixture)
    /// </code>
    /// Stats + runes are written at the SHIFTED anchor — round-trip tests that read this fixture
    /// validate that <see cref="EldenRingSave.DiscoverMagicOffset"/> finds 0xA1DC, not 0xA1CF.</summary>
    public static byte[] BuildSaveWithInventory(uint runes,
        byte vig, byte mnd, byte end_, byte str, byte dex, byte int_, byte fai, byte arc)
        => BuildSaveWithInventory(DefaultFixtureName, runes, vig, mnd, end_, str, dex, int_, fai, arc);

    /// <summary>Named-character overload of <see cref="BuildSaveWithInventory(uint, byte, byte, byte, byte, byte, byte, byte, byte)"/>.</summary>
    public static byte[] BuildSaveWithInventory(string name, uint runes,
        byte vig, byte mnd, byte end_, byte str, byte dex, byte int_, byte fai, byte arc)
    {
        // Magic anchor when slot 0's first GA-items entry is a weapon (21 bytes):
        //   0x20 + 21 + 5119 * 8 + 0x1AF = 0xA1DC
        const uint WeaponHandle = 0x80000001u;            // handle != 0, type-bits = 0x80000000
        const uint WeaponItemId = 0x40000000u;            // any valid item_id; not load-bearing
        const int InventoryAnchor = 0xA1DC;

        var totalSize = SaveHeaderTotalEnd;
        var buffer = new byte[totalSize];
        WriteBnd4Header(buffer);

        var slot0Data = buffer.AsSpan(FirstSlotDataOffset, SlotDataSize);

        // Plant a 21-byte weapon entry at GA-items index 0 (slot offset 0x20). Only the first
        // 8 bytes (handle + item_id) carry meaning for the walk; the remaining 13 bytes can be
        // anything (the walk just skips past them based on the handle's type-bits).
        BitConverter.TryWriteBytes(slot0Data.Slice(0x20, 4), WeaponHandle);
        BitConverter.TryWriteBytes(slot0Data.Slice(0x24, 4), WeaponItemId);
        // bytes 0x28..0x35 stay zero — fine, the walk doesn't inspect them.

        // Write stats + runes at the SHIFTED anchor (0xA1DC, not the empty-fixture 0xA1CF).
        SlotData.WriteRunes(slot0Data, runes, InventoryAnchor);
        SlotData.WriteStats(slot0Data, vig, mnd, end_, str, dex, int_, fai, arc, InventoryAnchor);

        // Slot 0 MD5 over the tampered body.
        var slot0Md5Region = buffer.AsSpan(FirstSlotMd5Offset, 0x10);
        SlotChecksum.ComputeMd5(slot0Data).CopyTo(slot0Md5Region);

        // Slots 1-9 stay zero-filled.
        var zeroSlotMd5 = SlotChecksum.ComputeMd5(new byte[SlotDataSize]);
        for (int i = 1; i < SlotCount; i++)
        {
            var md5Region = buffer.AsSpan(FirstSlotMd5Offset + i * SlotStride, 0x10);
            zeroSlotMd5.CopyTo(md5Region);
        }

        // Save-header section: active flag + summary + save-header MD5.
        buffer[CharActiveStatusOffset + 0] = 1;
        var slot0Summary = buffer.AsSpan(PerSlotSummaryStart, PerSlotSummaryStride);
        WriteCharacterName(slot0Summary, name);
        var stats = new SlotStats(vig, mnd, end_, str, dex, int_, fai, arc);
        BitConverter.TryWriteBytes(slot0Summary.Slice(CharLevelOffsetInSummary, 2), (short)SlotData.LevelFromStats(stats));
        BitConverter.TryWriteBytes(slot0Summary.Slice(CharPlayedSecondsOffsetInSummary, 4), 0);
        var saveHeaderRegion = buffer.AsSpan(SaveHeadersSectionStart, SaveHeadersSectionLength);
        var saveHeaderMd5Region = buffer.AsSpan(SaveHeaderMd5Offset, 0x10);
        SlotChecksum.ComputeMd5(saveHeaderRegion).CopyTo(saveHeaderMd5Region);

        return buffer;
    }

    /// <summary>The offset of slot <paramref name="slotIndex"/>'s payload (post-MD5) in the
    /// returned buffer. Mirrors BenGrn's <c>SLOT_START_INDEX + slotIndex * 0x10 + slotIndex *
    /// SLOT_LENGTH</c>.</summary>
    public static int GetSlotDataOffset(int slotIndex)
        => FirstSlotDataOffset + slotIndex * SlotStride;

    /// <summary>The offset of slot <paramref name="slotIndex"/>'s 16-byte MD5 region.</summary>
    public static int GetSlotMd5Offset(int slotIndex)
        => FirstSlotMd5Offset + slotIndex * SlotStride;

    private static void WriteBnd4Header(Span<byte> buffer)
    {
        // BND4 magic at 0x00.
        Encoding.ASCII.GetBytes("BND4").CopyTo(buffer);

        // unk04 / unk05 / big_endian / bit_big_endian flags at 0x04..0x0A — all zero (PC is LE).

        // file_count at 0x0C.
        BitConverter.TryWriteBytes(buffer.Slice(0x0C, 4), SlotCount);

        // file_header_offset at 0x10 (int64, always 0x40 for BND4 v4).
        BitConverter.TryWriteBytes(buffer.Slice(0x10, 8), 0x40L);

        // version_string at 0x18 — 8 bytes ASCII. ER ships "04E10231" in real saves; using
        // it here for verisimilitude, but Task 4 readers should not depend on the exact bytes.
        Encoding.ASCII.GetBytes("04E10231").CopyTo(buffer.Slice(0x18, 8));

        // file_header_size at 0x20 (int64). Each entry is 0x20 bytes in the long-offsets +
        // IDs + names format; size = SlotCount * 0x20.
        BitConverter.TryWriteBytes(buffer.Slice(0x20, 8), (long)(SlotCount * 0x20));

        // unicode flag at 0x30 — ER saves are unicode-named.
        buffer[0x30] = 1;
        // format byte at 0x31 — flag bits indicating long offsets + IDs + names (exact bits
        // unverified for this fixture, but a stride-based reader doesn't use this byte).
        // extended byte at 0x32 — leave 0.

        // File table starts at 0x40. 10 entries, 0x20 bytes each. For each slot i, write a
        // minimal entry pointing at the slot's MD5 region (treating MD5+data as one file
        // blob of size 0x280010).
        // NOTE: real BND4 entries carry per-flag-bit conditional fields. This minimal layout
        // is enough to let a stride-based reader find slots; it is NOT a fully-spec-compliant
        // BND4 entry. Task 4 either extends this or switches readers to the stride formula.
        for (int i = 0; i < SlotCount; i++)
        {
            int entryStart = 0x40 + i * 0x20;
            // file_flags at +0x00 (typically 0x40 for stored, not compressed).
            buffer[entryStart] = 0x40;
            // padding 3 bytes
            // reserved at +0x04 (int32, always -1).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x04, 4), -1);
            // compressed_size at +0x08 (int64).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x08, 8), (long)SlotStride);
            // uncompressed_size at +0x10 (int64).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x10, 8), (long)SlotStride);
            // data_offset at +0x18 (int64).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x18, 8), (long)(FirstSlotMd5Offset + i * SlotStride));
        }
        // Everything past 0x40 + 10*0x20 = 0x180 to 0x300 stays zero (padding / hash region).
    }
}
