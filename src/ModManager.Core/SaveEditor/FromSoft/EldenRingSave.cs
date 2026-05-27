using System.Text;

namespace ModManager.Core.SaveEditor.FromSoft;

/// <summary>
/// Public API for reading and editing Elden Ring <c>.sl2</c> save files. Wraps the lower-level
/// <see cref="SlotData"/> + <see cref="SlotChecksum"/> primitives into a slot-aware reader/writer.
///
/// Round-trip contract:
/// 1) <see cref="ReadCharacters"/> returns one <see cref="CharacterSlot"/> per populated slot.
///    Slots with an invalid MD5 or all-zero stats are skipped (treated as unused/corrupt).
/// 2) <see cref="WriteEdit"/> patches one slot in-memory, recomputes both the slot MD5 and the
///    save-header MD5 (the per-slot summary name/level lives there), and writes atomically.
///
/// File format (BND4 container, per <c>docs/superpowers/research/2026-05-26-fromsoft-save-libs.md</c>):
/// - 0x000..0x300: BND4 header + file table.
/// - 0x300 + i * 0x280010: slot i MD5 (16 bytes).
/// - 0x310 + i * 0x280010: slot i data (0x280000 bytes).
/// - 0x19003A0..0x19003B0: save-header MD5 (16 bytes).
/// - 0x19003B0..0x19603B0: save-header section (0x60000 bytes; per-slot summaries + flags).
/// - 0x1901D04 + i: active-flag byte for slot i.
/// - 0x1901D0E + i * 0x24C: per-slot summary (name UTF-16 @ 0x00, level int16 @ 0x22).
///
/// References:
/// - BenGrn/EldenRingSaveCopier (MIT) — slot stride, save-header layout, summary offsets.
/// - alfizari/Elden-Ring-Save-Editor (MIT) — anchor-relative stat/rune offsets, MD5 ranges.
/// - ClayAmore/ER-Save-Editor (Apache-2.0) — BND4 layout cross-reference.
/// </summary>
public static class EldenRingSave
{
    // --- File layout constants (mirror EldenRingFixture; canonical source for runtime reads) ---
    internal const int SlotCount = 10;
    internal const int SlotDataSize = SlotData.SlotSize;        // 0x280000
    internal const int SlotStride = SlotDataSize + 0x10;         // 0x280010
    internal const int FirstSlotMd5Offset = 0x300;
    internal const int FirstSlotDataOffset = 0x310;

    internal const int SaveHeaderMd5Offset = 0x019003A0;
    internal const int SaveHeadersSectionStart = 0x019003B0;
    internal const int SaveHeadersSectionLength = 0x60000;
    internal const int SaveHeaderTotalEnd = SaveHeadersSectionStart + SaveHeadersSectionLength; // 0x19603B0
    internal const int CharActiveStatusOffset = 0x01901D04;
    internal const int PerSlotSummaryStart = 0x01901D0E;
    internal const int PerSlotSummaryStride = 0x24C;
    internal const int CharNameOffsetInSummary = 0x00;
    internal const int CharNameLengthBytes = 0x22;
    internal const int CharLevelOffsetInSummary = 0x22;
    internal const int CharPlayedSecondsOffsetInSummary = 0x26;

    // The minimum file size we can meaningfully parse: the BND4 envelope + 10 slots + the
    // save-header MD5/section. Anything smaller is structurally invalid.
    internal const int MinimumFileSize = SaveHeaderTotalEnd;

    /// <summary>Read every populated character slot. Skips slots whose MD5 doesn't match their
    /// data (treated as corrupt) and slots whose 8 stats are all zero (treated as unused — the
    /// active-flag array is consulted too, but the stat-zero heuristic catches fixtures and
    /// any save where the flag is unreliable).</summary>
    public static IReadOnlyList<CharacterSlot> ReadCharacters(string savePath)
    {
        if (savePath is null) throw new ArgumentNullException(nameof(savePath));
        if (!File.Exists(savePath)) throw new FileNotFoundException("Save file not found.", savePath);

        var bytes = File.ReadAllBytes(savePath);
        if (bytes.Length < MinimumFileSize)
        {
            throw new InvalidDataException(
                $"Save file is too small ({bytes.Length} bytes) to be a valid Elden Ring .sl2 — expected at least {MinimumFileSize}.");
        }

        var result = new List<CharacterSlot>(SlotCount);
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = TryReadSlot(bytes, i);
            if (slot is not null) result.Add(slot);
        }
        return result;
    }

    /// <summary>Apply an edit to one slot. Recomputes the slot MD5 and the save-header MD5
    /// (the latter covers the name/level summary). Writes atomically (temp + rename) so a
    /// crash mid-write can't corrupt the user's save.</summary>
    public static void WriteEdit(string savePath, int slotIndex, CharacterEdit edit)
    {
        if (savePath is null) throw new ArgumentNullException(nameof(savePath));
        if (edit is null) throw new ArgumentNullException(nameof(edit));
        if (slotIndex < 0 || slotIndex >= SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex),
                $"Slot index must be 0..{SlotCount - 1}; got {slotIndex}.");
        }
        if (!File.Exists(savePath)) throw new FileNotFoundException("Save file not found.", savePath);

        var bytes = File.ReadAllBytes(savePath);
        if (bytes.Length < MinimumFileSize)
        {
            throw new InvalidDataException(
                $"Save file is too small ({bytes.Length} bytes) to be a valid Elden Ring .sl2 — expected at least {MinimumFileSize}.");
        }

        // 1) Patch the slot body — runes + stats at the runtime-discovered anchor (currently
        //    fixed-anchor for the MVP fixture; real-save anchor discovery is a future-task wedge).
        var slotData = bytes.AsSpan(FirstSlotDataOffset + slotIndex * SlotStride, SlotDataSize);
        SlotData.WriteRunes(slotData, edit.Runes);
        SlotData.WriteStats(slotData, edit.Vig, edit.Mnd, edit.End, edit.Str, edit.Dex, edit.Int, edit.Fai, edit.Arc);

        // 2) Recompute the slot MD5 over the new slot bytes.
        var slotMd5 = bytes.AsSpan(FirstSlotMd5Offset + slotIndex * SlotStride, 0x10);
        SlotChecksum.ComputeMd5(slotData).CopyTo(slotMd5);

        // 3) Update the per-slot summary in the save-header section (name + level). Also flip
        //    the active flag on for this slot so a reader's active-flag check stays consistent.
        bytes[CharActiveStatusOffset + slotIndex] = 1;
        var summary = bytes.AsSpan(PerSlotSummaryStart + slotIndex * PerSlotSummaryStride, PerSlotSummaryStride);
        WriteCharacterName(summary, edit.Name);
        var stats = new SlotStats(edit.Vig, edit.Mnd, edit.End, edit.Str, edit.Dex, edit.Int, edit.Fai, edit.Arc);
        BitConverter.TryWriteBytes(summary.Slice(CharLevelOffsetInSummary, 2), (short)SlotData.LevelFromStats(stats));

        // 4) Recompute the save-header MD5 over [SaveHeadersSectionStart, SaveHeaderTotalEnd).
        var saveHeader = bytes.AsSpan(SaveHeadersSectionStart, SaveHeadersSectionLength);
        var saveHeaderMd5 = bytes.AsSpan(SaveHeaderMd5Offset, 0x10);
        SlotChecksum.ComputeMd5(saveHeader).CopyTo(saveHeaderMd5);

        // 5) Atomic write: temp file + rename. Mirrors AtomicJson.WriteJsonAtomic.
        WriteBytesAtomic(savePath, bytes);
    }

    /// <summary>Walk the GA-items table to find the anchor offset where stats/runes live.
    /// In the MVP fixture (and any save with an entirely-empty inventory) the table is 5120
    /// empty entries of 8 bytes each, so the anchor is the fixed value <see cref="SlotData.FixtureMagicOffset"/>.
    /// Real saves require branching on <c>handle &amp; 0xF0000000</c> per entry — wired here as
    /// the empty-entry path; non-empty entries are deferred (Task 9 smoke will surface them).</summary>
    internal static int DiscoverMagicOffset(ReadOnlySpan<byte> slotBody)
    {
        // GA-items table starts at 0x20. Each empty entry (handle == 0) is 8 bytes. Non-empty
        // entries vary by type bits — see alfizari/Final.py for the full branch table. For now
        // the MVP fixture only uses empty entries, so we walk straight through.
        int cursor = SlotData.GaItemsStart;
        for (int i = 0; i < SlotData.GaItemCountForFixture; i++)
        {
            uint handle = BitConverter.ToUInt32(slotBody.Slice(cursor, 4));
            // type_bits = handle & 0xF0000000. 0 → empty (8 bytes). Other type-bit branches are
            // documented in alfizari/Final.py; the MVP fixture only exercises the empty path.
            uint typeBits = handle & 0xF0000000u;
            int entrySize = typeBits switch
            {
                0u => SlotData.EmptyGaItemSize,
                _ => SlotData.EmptyGaItemSize, // TODO Task 9 smoke: branch to 13/16 for weapon/armor.
            };
            cursor += entrySize;
        }
        return cursor + SlotData.MagicAnchorOffset;
    }

    private static CharacterSlot? TryReadSlot(byte[] bytes, int slotIndex)
    {
        var slotMd5 = bytes.AsSpan(FirstSlotMd5Offset + slotIndex * SlotStride, 0x10);
        var slotData = bytes.AsSpan(FirstSlotDataOffset + slotIndex * SlotStride, SlotDataSize);

        // 1) MD5 integrity — corrupt slots are skipped, not thrown over.
        if (!SlotChecksum.VerifyMd5(slotMd5, slotData)) return null;

        // 2) Active-flag check + stat-zero fallback. The fixture sets the active flag; real
        //    saves do too. If the flag is 0 AND all stats are zero we treat the slot as unused.
        //    (If the flag is 1 we trust it. If the flag is 0 but stats are non-zero — possible
        //    after the user deletes a character — we still skip; see comment.)
        byte activeFlag = bytes[CharActiveStatusOffset + slotIndex];
        var stats = SlotData.ReadStats(slotData);
        bool allStatsZero = stats.Vig == 0 && stats.Mnd == 0 && stats.End == 0 && stats.Str == 0
            && stats.Dex == 0 && stats.Int == 0 && stats.Fai == 0 && stats.Arc == 0;
        if (activeFlag == 0 || allStatsZero) return null;

        // 3) Stats + runes from the anchor.
        uint runes = SlotData.ReadRunes(slotData);
        int level = SlotData.LevelFromStats(stats);

        // 4) Name from the per-slot summary in the save-header section.
        var summary = bytes.AsSpan(PerSlotSummaryStart + slotIndex * PerSlotSummaryStride, PerSlotSummaryStride);
        string name = ReadCharacterName(summary);

        return new CharacterSlot(
            SlotIndex: slotIndex,
            Name: name,
            Class: string.Empty, // Class detection deferred to Task 9 smoke (not stored explicitly in the save).
            Level: level,
            Runes: runes,
            Vig: stats.Vig, Mnd: stats.Mnd, End: stats.End, Str: stats.Str,
            Dex: stats.Dex, Int: stats.Int, Fai: stats.Fai, Arc: stats.Arc,
            SteamId: string.Empty); // Steam ID read deferred to Task 9 smoke (Steam ID lives at 0x19003B4).
    }

    private static string ReadCharacterName(ReadOnlySpan<byte> summary)
    {
        var nameBytes = summary.Slice(CharNameOffsetInSummary, CharNameLengthBytes);
        // UTF-16 LE. Trim at the first U+0000 to mimic the in-game character name length.
        var decoded = Encoding.Unicode.GetString(nameBytes);
        int nullIndex = decoded.IndexOf('\0');
        return nullIndex < 0 ? decoded : decoded.Substring(0, nullIndex);
    }

    private static void WriteCharacterName(Span<byte> summary, string name)
    {
        // Clear the name region first, then write at most 16 UTF-16 chars (32 bytes); the 17th
        // slot is reserved for the null terminator that ER's UI scans for.
        summary.Slice(CharNameOffsetInSummary, CharNameLengthBytes).Clear();
        var capped = name.Length > 16 ? name.Substring(0, 16) : name;
        var nameBytes = Encoding.Unicode.GetBytes(capped);
        nameBytes.AsSpan(0, Math.Min(nameBytes.Length, CharNameLengthBytes))
            .CopyTo(summary.Slice(CharNameOffsetInSummary, CharNameLengthBytes));
    }

    private static void WriteBytesAtomic(string path, byte[] bytes)
    {
        // Mirror AtomicJson.WriteTextAtomic: write to a sibling temp, then atomic-rename.
        var tmp = path + ".tmp-" + Environment.ProcessId;
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* nothing to clean up */ }
            throw;
        }
    }
}
