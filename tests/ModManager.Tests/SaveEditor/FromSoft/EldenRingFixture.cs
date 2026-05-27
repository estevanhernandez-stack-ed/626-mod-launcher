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

    // Bnd4 file-table layout — 11 entries (10 USER_DATAxxx slots + USER_DATA011 for the save
    // header) at 0x28 bytes each, name table immediately following. 11 * 0x28 = 0x1B8; entries
    // span 0x40..0x1F8. Each name is 12 UTF-16 chars + 2-byte null = 26 bytes; 11 * 26 = 286
    // = 0x11E; names span 0x1F8..0x316. We round the BND4 header region up to 0x320 to keep
    // SlotsRegionStart 16-byte aligned (and to match EldenRingSave's mirror constants).
    public const int BndHeaderSize = 0x320;                    // BND4 header + file table + name table

    public const int SlotsRegionStart = BndHeaderSize;         // 0x320
    public const int FirstSlotMd5Offset = SlotsRegionStart;    // 0x320
    public const int FirstSlotDataOffset = SlotsRegionStart + 0x10; // 0x330

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

    /// <summary>Build a save like <see cref="BuildSaveWithOneCharacter(string, uint, byte, byte, byte, byte, byte, byte, byte, byte)"/>
    /// but with the save-header section relocated to a different file offset, and the
    /// <c>USER_DATA011</c> file-table entry's <c>data_offset</c> field updated to point at the
    /// new location. The MD5 region (16 bytes immediately preceding the section) moves with it.
    ///
    /// Used to prove that <see cref="EldenRingSave.ReadCharacters"/> finds the save header by
    /// NAME via the BND4 file-table walk, not by the hardcoded <c>0x019003B0</c>. A pre-walk
    /// reader will read garbage at the original offset and produce wrong / no characters.
    ///
    /// <paramref name="extraPadBytes"/> is the number of zero bytes inserted between the slots
    /// and the save-header section. Slot entries remain at their original offsets; only the
    /// save-header section + MD5 move (by <c>extraPadBytes</c>). Pre-DLC ER puts the section
    /// at <c>0x019003B0</c>; with a 0x1000 pad it lands at <c>0x019013B0</c>.</summary>
    public static byte[] BuildSaveWithShiftedSaveHeader(string name, uint runes,
        byte vig, byte mnd, byte end_, byte str, byte dex, byte int_, byte fai, byte arc,
        int extraPadBytes)
    {
        if (extraPadBytes < 0) throw new ArgumentOutOfRangeException(nameof(extraPadBytes));

        // Start with the standard fixture. Then build a larger buffer where the save-header
        // section + its preceding MD5 are shifted by extraPadBytes. The 10 slot entries stay put.
        var standard = BuildSaveWithOneCharacter(name, runes, vig, mnd, end_, str, dex, int_, fai, arc);

        // New total length: standard length + pad.
        var shifted = new byte[standard.Length + extraPadBytes];

        // Region A — bytes [0, SaveHeaderMd5Offset) — copy as-is. Covers BND4 header,
        // file table, name table, slot MD5s + slot data. Untouched by the shift.
        Array.Copy(standard, 0, shifted, 0, SaveHeaderMd5Offset);

        // Region B — the save-header MD5 (16 bytes) + save-header section (0x60000 bytes) —
        // relocated by extraPadBytes. Bytes between [SaveHeaderMd5Offset, SaveHeaderMd5Offset
        // + extraPadBytes) in the new buffer stay zero (the pad).
        int oldMd5Start = SaveHeaderMd5Offset;
        int newMd5Start = SaveHeaderMd5Offset + extraPadBytes;
        // 0x10 MD5 bytes + 0x60000 section bytes = SaveHeaderTotalEnd - SaveHeaderMd5Offset.
        int regionBLength = SaveHeaderTotalEnd - SaveHeaderMd5Offset;
        Array.Copy(standard, oldMd5Start, shifted, newMd5Start, regionBLength);

        // Update USER_DATA011's data_offset field in the file table to point at the new
        // save-header section location (newMd5Start + 0x10 = old SaveHeadersSectionStart + pad).
        // The file-table entry for the save-header entry is the 11th entry (index SlotCount).
        int saveHeaderEntryStart = FileTableStart + SlotCount * Bnd4EntrySize;
        long newSaveHeaderDataOffset = SaveHeadersSectionStart + extraPadBytes;
        BitConverter.TryWriteBytes(
            shifted.AsSpan(saveHeaderEntryStart + 0x18, 8),
            newSaveHeaderDataOffset);

        return shifted;
    }

    /// <summary>Build a save like <see cref="BuildSaveWithOneCharacter(string, uint, byte, byte, byte, byte, byte, byte, byte, byte)"/>
    /// but with one EXTRA entry in the BND4 file table — <c>USER_DATA012</c>, simulating
    /// the same DLC-style addition the real Elden Ring DLC already shipped. The 10 slot entries
    /// + the save-header entry (USER_DATA011) stay at their original absolute file offsets, so
    /// the EldenRingSave constants for the save-header section (<c>0x019003B0</c> etc.) remain
    /// valid; only the BND4 file table grows.
    ///
    /// Layout choices:
    /// - File table at <c>0x40</c> grows from 11 × 0x28 to 12 × 0x28 = 0x1E0 bytes, ending at
    ///   <c>0x220</c>. The legacy padding zone (<c>0x220..0x320</c>) absorbs the growth without
    ///   touching the slot region.
    /// - The name table is APPENDED past the save-header section (at
    ///   <c>SaveHeaderTotalEnd</c> = 0x19603B0). Each <c>name_offset</c> field on every entry
    ///   points at the appended name table. This keeps the slot-region start at 0x320, so we
    ///   don't have to shift slot data and recompute every MD5.
    /// - <c>USER_DATA012</c>'s <c>data_offset</c> points at a small zero-filled 0x100-byte
    ///   region appended right after the name table. The test only cares that the walk doesn't
    ///   fall over on the extra entry — it doesn't need to be a real save section.
    ///
    /// Used to prove that ReadCharacters / WriteEdit walk the BND4 file table by NAME to find
    /// USER_DATA011 even when it's no longer the last entry — the contract that protects the
    /// app against future ER patches that add entries.</summary>
    public static byte[] BuildSaveWithExtraEntry(string name, uint runes,
        byte vig, byte mnd, byte end_, byte str, byte dex, byte int_, byte fai, byte arc)
    {
        // 12 entries: 10 slots + USER_DATA011 (save header) + USER_DATA012 (DLC-style extra).
        const int ExtendedEntryCount = SlotCount + 2;
        // The extra USER_DATA012 region appended at the end of the file — its content is not
        // meaningful, just zero-filled so the per-entry bounds check passes.
        const int ExtraEntryDataSize = 0x100;

        // Name table appended past the save-header section; extra data appended past that.
        int nameTableStartExtended = SaveHeaderTotalEnd;
        int extraDataStart = nameTableStartExtended + ExtendedEntryCount * NameByteLength;
        int totalSize = extraDataStart + ExtraEntryDataSize;

        var buffer = new byte[totalSize];

        // BND4 magic + counts + file-header-offset + version_string + file_header_size + unicode flag.
        Encoding.ASCII.GetBytes("BND4").CopyTo(buffer);
        BitConverter.TryWriteBytes(buffer.AsSpan(0x0C, 4), ExtendedEntryCount);
        BitConverter.TryWriteBytes(buffer.AsSpan(0x10, 8), (long)FileTableStart);
        Encoding.ASCII.GetBytes("04E10231").CopyTo(buffer.AsSpan(0x18, 8));
        BitConverter.TryWriteBytes(buffer.AsSpan(0x20, 8), (long)(ExtendedEntryCount * Bnd4EntrySize));
        buffer[0x30] = 1;

        // Per-entry name_offsets — point into the appended name table.
        var nameOffsets = new int[ExtendedEntryCount];
        for (int i = 0; i < ExtendedEntryCount; i++)
        {
            nameOffsets[i] = nameTableStartExtended + i * NameByteLength;
        }

        // File-table entries — slots, save header, extra DLC entry.
        for (int i = 0; i < ExtendedEntryCount; i++)
        {
            int entryStart = FileTableStart + i * Bnd4EntrySize;

            long dataOffset;
            long dataSize;
            int fileId;
            if (i < SlotCount)
            {
                dataOffset = FirstSlotMd5Offset + (long)i * SlotStride;
                dataSize = SlotStride;
                fileId = i;
            }
            else if (i == SlotCount)
            {
                // USER_DATA011 — save header section (matches the standard fixture).
                dataOffset = SaveHeadersSectionStart;
                dataSize = SaveHeadersSectionLength;
                fileId = SaveHeaderEntryFileId;
            }
            else
            {
                // USER_DATA012 — the DLC-style extra entry. Zero-filled region at end of file.
                dataOffset = extraDataStart;
                dataSize = ExtraEntryDataSize;
                fileId = SaveHeaderEntryFileId + 1; // 12
            }

            buffer[entryStart] = 0x40;
            BitConverter.TryWriteBytes(buffer.AsSpan(entryStart + 0x04, 4), -1);
            BitConverter.TryWriteBytes(buffer.AsSpan(entryStart + 0x08, 8), dataSize);
            BitConverter.TryWriteBytes(buffer.AsSpan(entryStart + 0x10, 8), dataSize);
            BitConverter.TryWriteBytes(buffer.AsSpan(entryStart + 0x18, 8), dataOffset);
            BitConverter.TryWriteBytes(buffer.AsSpan(entryStart + 0x20, 4), fileId);
            BitConverter.TryWriteBytes(buffer.AsSpan(entryStart + 0x24, 4), nameOffsets[i]);
        }

        // Name table at the appended location.
        for (int i = 0; i < ExtendedEntryCount; i++)
        {
            string entryName = i < SlotCount
                ? $"USER_DATA00{i}"
                : i == SlotCount
                    ? SaveHeaderEntryName
                    : "USER_DATA012";
            var nameBytes = Encoding.Unicode.GetBytes(entryName);
            nameBytes.CopyTo(buffer.AsSpan(nameOffsets[i], nameBytes.Length));
        }

        // Slot 0 — populate with the character (runes + stats at the fixture anchor) and MD5.
        var slot0Data = buffer.AsSpan(FirstSlotDataOffset, SlotDataSize);
        SlotData.WriteRunes(slot0Data, runes, SlotData.FixtureMagicOffset);
        SlotData.WriteStats(slot0Data, vig, mnd, end_, str, dex, int_, fai, arc, SlotData.FixtureMagicOffset);

        var slot0Md5Region = buffer.AsSpan(FirstSlotMd5Offset, 0x10);
        SlotChecksum.ComputeMd5(slot0Data).CopyTo(slot0Md5Region);

        // Slots 1-9: zero-filled with the zero-slot MD5.
        var zeroSlotMd5 = SlotChecksum.ComputeMd5(new byte[SlotDataSize]);
        for (int i = 1; i < SlotCount; i++)
        {
            var md5Region = buffer.AsSpan(FirstSlotMd5Offset + i * SlotStride, 0x10);
            zeroSlotMd5.CopyTo(md5Region);
        }

        // Save-header section: active flag + per-slot summary + save-header MD5.
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

    /// <summary>Build a save like <see cref="BuildSaveWithOneCharacter(string, uint, byte, byte, byte, byte, byte, byte, byte, byte)"/>
    /// but with the <c>USER_DATA011</c> name in the BND4 name table replaced with
    /// <paramref name="renamedTo"/>. The save-header section + MD5 are unchanged; only the
    /// name-table bytes for that entry are overwritten.
    ///
    /// Constraint: <paramref name="renamedTo"/> must be EXACTLY 12 characters so the UTF-16LE
    /// encoding fits in the same 24 bytes that <c>USER_DATA011</c> occupies. Anything else would
    /// require shifting subsequent name-table entries.
    ///
    /// Used to prove that the BND4 walk fails LOUD when the expected save-header entry is
    /// missing — surfaces as an <see cref="InvalidDataException"/> listing the names that WERE
    /// found, never a silent wrong-region read.</summary>
    public static byte[] BuildSaveWithRenamedSaveHeader(string renamedTo, string name, uint runes,
        byte vig, byte mnd, byte end_, byte str, byte dex, byte int_, byte fai, byte arc)
    {
        if (renamedTo is null) throw new ArgumentNullException(nameof(renamedTo));
        if (renamedTo.Length != SaveHeaderEntryName.Length)
        {
            throw new ArgumentException(
                $"renamedTo must be exactly {SaveHeaderEntryName.Length} characters (same byte width as " +
                $"'{SaveHeaderEntryName}'); got '{renamedTo}' ({renamedTo.Length} chars).",
                nameof(renamedTo));
        }
        if (string.Equals(renamedTo, SaveHeaderEntryName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"renamedTo must differ from '{SaveHeaderEntryName}' — otherwise the fixture would " +
                "not simulate a renamed save-header entry.",
                nameof(renamedTo));
        }

        var buffer = BuildSaveWithOneCharacter(name, runes, vig, mnd, end_, str, dex, int_, fai, arc);

        // The save-header entry is the 11th file-table entry (index SlotCount). Its name_offset
        // points at nameOffsets[SlotCount] = NameTableStart + SlotCount * NameByteLength.
        int saveHeaderNameOffset = NameTableStart + SlotCount * NameByteLength;
        // Clear the existing name's UTF-16 bytes (24 bytes — 12 chars × 2). The trailing 2-byte
        // null terminator at +0x18 stays zero.
        buffer.AsSpan(saveHeaderNameOffset, SaveHeaderEntryName.Length * 2).Clear();
        var newNameBytes = Encoding.Unicode.GetBytes(renamedTo);
        newNameBytes.CopyTo(buffer.AsSpan(saveHeaderNameOffset, newNameBytes.Length));

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

    // --- BND4 file-table + name-table layout (the long-offsets + IDs + names format ER ships) ---
    private const int Bnd4EntrySize = 0x28;                         // 40 bytes per entry
    private const int FileTableStart = 0x40;                        // BND4 v4 always
    private const int TotalEntryCount = SlotCount + 1;              // 10 slots + 1 save-header entry
    private const int NameTableStart = FileTableStart + TotalEntryCount * Bnd4EntrySize; // 0x1F8
    private const int NameByteLength = 12 * 2 + 2;                  // "USER_DATAxxx" UTF-16 + null = 26 bytes
    public const int SaveHeaderEntryFileId = 11;                    // USER_DATA011 carries the save header
    public const string SaveHeaderEntryName = "USER_DATA011";

    private static void WriteBnd4Header(Span<byte> buffer)
    {
        // BND4 magic at 0x00.
        Encoding.ASCII.GetBytes("BND4").CopyTo(buffer);

        // unk04 / unk05 / big_endian / bit_big_endian flags at 0x04..0x0A — all zero (PC is LE).

        // file_count at 0x0C — 10 slot entries + 1 save-header entry (USER_DATA011).
        BitConverter.TryWriteBytes(buffer.Slice(0x0C, 4), TotalEntryCount);

        // file_header_offset at 0x10 (int64, always 0x40 for BND4 v4).
        BitConverter.TryWriteBytes(buffer.Slice(0x10, 8), (long)FileTableStart);

        // version_string at 0x18 — 8 bytes ASCII. ER ships "04E10231" in real saves; using
        // it here for verisimilitude, but Task 4 readers should not depend on the exact bytes.
        Encoding.ASCII.GetBytes("04E10231").CopyTo(buffer.Slice(0x18, 8));

        // file_header_size at 0x20 (int64). Each entry is 0x28 bytes in the long-offsets +
        // IDs + names format; size = TotalEntryCount * 0x28.
        BitConverter.TryWriteBytes(buffer.Slice(0x20, 8), (long)(TotalEntryCount * Bnd4EntrySize));

        // unicode flag at 0x30 — ER saves are unicode-named.
        buffer[0x30] = 1;
        // format byte at 0x31 — flag bits indicating long offsets + IDs + names (exact bits
        // unverified for this fixture, but a stride-based reader doesn't use this byte).
        // extended byte at 0x32 — leave 0.

        // Precompute each entry's name offset within the buffer. Names are 26 bytes wide
        // ("USER_DATAxxx" = 12 UTF-16 code units + 2-byte null terminator).
        var nameOffsets = new int[TotalEntryCount];
        for (int i = 0; i < TotalEntryCount; i++)
        {
            nameOffsets[i] = NameTableStart + i * NameByteLength;
        }

        // File-table entries (40 bytes each). For each of the 10 USER_DATA000..USER_DATA009
        // slot entries, the data points at the slot's MD5 region (MD5 + data = SlotStride
        // blob). The 11th entry (USER_DATA011) points at the save-header section.
        for (int i = 0; i < TotalEntryCount; i++)
        {
            int entryStart = FileTableStart + i * Bnd4EntrySize;

            long dataOffset;
            long dataSize;
            int fileId;
            if (i < SlotCount)
            {
                dataOffset = FirstSlotMd5Offset + (long)i * SlotStride;
                dataSize = SlotStride;
                fileId = i;
            }
            else
            {
                // USER_DATA011 — save header section, NOT counting the leading MD5.
                dataOffset = SaveHeadersSectionStart;
                dataSize = SaveHeadersSectionLength;
                fileId = SaveHeaderEntryFileId;
            }

            // file_flags at +0x00 (typically 0x40 for stored, not compressed).
            buffer[entryStart] = 0x40;
            // padding 3 bytes
            // reserved at +0x04 (int32, always -1).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x04, 4), -1);
            // compressed_size at +0x08 (int64).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x08, 8), dataSize);
            // uncompressed_size at +0x10 (int64).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x10, 8), dataSize);
            // data_offset at +0x18 (int64).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x18, 8), dataOffset);
            // file_id at +0x20 (int32).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x20, 4), fileId);
            // name_offset at +0x24 (int32) — points at the UTF-16LE name in the name table.
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x24, 4), nameOffsets[i]);
        }

        // Name table at 0x1F8. Each name is 12 chars * 2 bytes UTF-16LE + a 2-byte null
        // terminator. For slot i in 0..9 write "USER_DATA00i"; the 11th entry uses
        // USER_DATA011 (skipping 010 matches real ER saves where 010 is reserved).
        for (int i = 0; i < TotalEntryCount; i++)
        {
            string name = i < SlotCount
                ? $"USER_DATA00{i}"
                : SaveHeaderEntryName;
            var nameBytes = Encoding.Unicode.GetBytes(name);
            nameBytes.CopyTo(buffer.Slice(nameOffsets[i], nameBytes.Length));
            // 2-byte null terminator at nameOffsets[i] + nameBytes.Length stays zero by default.
        }
        // The remaining bytes from end-of-name-table through BndHeaderSize (0x316..0x320)
        // stay zero — padding / hash region.
    }
}
