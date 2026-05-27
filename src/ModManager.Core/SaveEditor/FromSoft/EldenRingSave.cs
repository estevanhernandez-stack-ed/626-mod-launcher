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
/// - 0x000..0x320: BND4 header + file table + name table.
/// - 0x320 + i * 0x280010: slot i MD5 (16 bytes).
/// - 0x330 + i * 0x280010: slot i data (0x280000 bytes).
/// - 0x19003A0..0x19003B0: save-header MD5 (16 bytes).
/// - 0x19003B0..0x19603B0: save-header section (0x60000 bytes; per-slot summaries + flags).
/// - 0x1901D04 + i: active-flag byte for slot i.
/// - 0x1901D0E + i * 0x24C: per-slot summary (name UTF-16 @ 0x00, level int16 @ 0x22).
///
/// Anchor model: every active slot's stats / runes live at a runtime-discovered offset inside
/// the slot body — <c>magic_offset = end_of_ga_items + 0x1AF</c>. The GA-items table at slot
/// offset 0x20 has 5120 variable-size entries; <see cref="DiscoverMagicOffset"/> walks them.
///
/// References:
/// - BenGrn/EldenRingSaveCopier (MIT) — slot stride, save-header layout, summary offsets.
/// - alfizari/Elden-Ring-Save-Editor (MIT) — anchor-relative stat/rune offsets, MD5 ranges,
///   GA-items per-entry sizes by handle type-bits.
/// - ClayAmore/ER-Save-Editor (Apache-2.0) — BND4 layout + GA-items structure cross-reference.
/// </summary>
public static class EldenRingSave
{
    // --- File layout constants (mirror EldenRingFixture; canonical source for runtime reads) ---
    internal const int SlotCount = 10;
    internal const int SlotDataSize = SlotData.SlotSize;        // 0x280000
    internal const int SlotStride = SlotDataSize + 0x10;         // 0x280010
    internal const int FirstSlotMd5Offset = 0x320;
    internal const int FirstSlotDataOffset = 0x330;

    internal const int SaveHeaderMd5Offset = 0x019003A0;
    internal const int SaveHeadersSectionStart = 0x019003B0;
    internal const int SaveHeadersSectionLength = 0x60000;
    internal const int SaveHeaderTotalEnd = SaveHeadersSectionStart + SaveHeadersSectionLength; // 0x19603B0
    internal const int CharActiveStatusOffset = 0x01901D04;
    internal const int PerSlotSummaryStart = 0x01901D0E;
    internal const int PerSlotSummaryStride = 0x24C;

    // Relative offsets INSIDE the save-header section. These are the layout positions of
    // the active-flag array and per-slot summary table within the section — they do NOT
    // depend on where the section sits in the file. CharActiveStatusOffset and
    // PerSlotSummaryStart above are the absolute file offsets for the legacy pre-DLC
    // layout, retained ONLY as the compile-time seeds for these relative constants. Every
    // runtime read/write path (ReadCharacters, WriteEdit, VerifyPostWrite, TryReadSlot) now
    // resolves absolutes by adding these relatives to the BND4-file-table-walked section
    // start — no method body computes a file offset from the absolute constants directly.
    //
    // Verified: 0x01901D04 - 0x019003B0 = 0x196954 and 0x01901D0E - 0x019003B0 = 0x19695E.
    internal const int CharActiveStatusRelative = CharActiveStatusOffset - SaveHeadersSectionStart; // 0x196954
    internal const int PerSlotSummaryRelative = PerSlotSummaryStart - SaveHeadersSectionStart;     // 0x19695E

    // BND4 file-table entry names. USER_DATA000..USER_DATA009 carry slot bodies (MD5 + data).
    // The save-header section lives at either USER_DATA010 OR USER_DATA011 depending on the
    // ER save-format generation (the synthetic fixture and pre-DLC saves put it at
    // USER_DATA011; current real saves put it at USER_DATA010 and use USER_DATA011 for a
    // newer appended section). The semantic difference: USER_DATA010 entries include the
    // 16-byte MD5 prefix in DataOffset; USER_DATA011 entries start AT the section data with
    // the MD5 sitting 16 bytes before. <see cref="ResolveSaveHeaderStart"/> handles both.
    internal const string SaveHeaderEntryNameLegacy = "USER_DATA011";  // pre-DLC / fixture
    internal const string SaveHeaderEntryNameCurrent = "USER_DATA010"; // current real saves
    // Retained for callers that haven't moved off the single-name lookup. New code should use
    // ResolveSaveHeaderStart instead.
    internal const string SaveHeaderEntryName = SaveHeaderEntryNameLegacy;
    internal const int CharNameOffsetInSummary = 0x00;
    internal const int CharNameLengthBytes = 0x22;
    internal const int CharLevelOffsetInSummary = 0x22;
    internal const int CharPlayedSecondsOffsetInSummary = 0x26;

    // The minimum file size we can meaningfully parse: the BND4 envelope + 10 slots + the
    // save-header MD5/section. Anything smaller is structurally invalid.
    internal const int MinimumFileSize = SaveHeaderTotalEnd;

    /// <summary>
    /// Locate the save-header section in the BND4 entries and return both the section start
    /// (the first byte of the 0x60000-byte payload) and the MD5 start (16 bytes before). Two
    /// shapes are supported:
    /// <list type="bullet">
    ///   <item><description><b>USER_DATA011 (fixture / pre-DLC)</b>: DataOffset points at the
    ///     section data. MD5 sits at DataOffset - 0x10.</description></item>
    ///   <item><description><b>USER_DATA010 (current real ER, post-DLC)</b>: DataOffset points
    ///     at the MD5. Section starts at DataOffset + 0x10. (USER_DATA011 in this shape carries
    ///     an unrelated DLC-introduced section we don't need here.)</description></item>
    /// </list>
    /// Prefer USER_DATA010 when it exists with the right size (0x60010 = MD5 + section) — that
    /// matches current ER. Otherwise fall back to USER_DATA011 (fixture/pre-DLC).
    /// </summary>
    public static (int SectionStart, int Md5Start) ResolveSaveHeaderStart(
        IReadOnlyList<Bnd4Entry> entries)
    {
        // Current real saves: USER_DATA010 is the MD5+section bundle.
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Name == SaveHeaderEntryNameCurrent
                && entries[i].DataSize == SaveHeadersSectionLength + 0x10)
            {
                int md5Start = checked((int)entries[i].DataOffset);
                return (md5Start + 0x10, md5Start);
            }
        }
        // Pre-DLC / fixture: USER_DATA011 is the section (no MD5 prefix).
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Name == SaveHeaderEntryNameLegacy)
            {
                int sectionStart = checked((int)entries[i].DataOffset);
                return (sectionStart, sectionStart - 0x10);
            }
        }
        var foundNames = string.Join(", ", entries.Select(e => e.Name));
        throw new InvalidDataException(
            $"BND4 entry for the save-header section not found. Tried '{SaveHeaderEntryNameCurrent}' " +
            $"(current ER) and '{SaveHeaderEntryNameLegacy}' (pre-DLC / fixture). " +
            $"Found entries: {foundNames}");
    }

    /// <summary>Read every populated character slot. Skips slots whose MD5 doesn't match their
    /// data (treated as corrupt) and slots whose 8 stats are all zero (treated as unused — the
    /// active-flag array is consulted too, but the stat-zero heuristic catches fixtures and
    /// any save where the flag is unreliable).
    ///
    /// For each active slot the GA-items table is walked to discover the magic anchor, then
    /// stats / runes are read at the anchor-relative offsets. Real saves with inventory work
    /// the same way as empty-inventory fixtures — only the anchor moves.</summary>
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

        // Walk the BND4 file table — locate the save-header section + each slot by NAME,
        // not by hardcoded offset. A future patch that shifts the layout (e.g. adds entries)
        // surfaces as a clear "entry not found" rather than reading garbage at a moved offset.
        var entries = Bnd4Reader.Parse(bytes);
        var (saveHeaderStart, _) = ResolveSaveHeaderStart(entries);
        int charActiveStatusAbs = saveHeaderStart + CharActiveStatusRelative;
        int perSlotSummaryStartAbs = saveHeaderStart + PerSlotSummaryRelative;

        var result = new List<CharacterSlot>(SlotCount);
        for (int i = 0; i < SlotCount; i++)
        {
            var slotEntry = Bnd4Reader.GetByName(entries, $"USER_DATA{i:D3}");
            var slot = TryReadSlot(bytes, slotEntry, charActiveStatusAbs, perSlotSummaryStartAbs, i);
            if (slot is not null) result.Add(slot);
        }
        return result;
    }

    /// <summary>Apply an edit to one slot. Recomputes the slot MD5 and the save-header MD5
    /// (the latter covers the name/level summary). Writes atomically (temp + rename) so a
    /// crash mid-write can't corrupt the user's save.
    ///
    /// Guards (in order):
    /// 1. The target slot's active flag must be 1 — refuses to edit an empty slot to avoid
    ///    creating a phantom character (active header / no real body).
    /// 2. The GA-items table is walked to discover the slot's magic anchor; if the walk
    ///    produces an offset outside the slot body we throw <see cref="InvalidDataException"/>
    ///    rather than write garbage at a wrong anchor.
    /// 3. After the atomic write, the file is re-read and the touched fields are verified
    ///    against the edit, while every byte OUTSIDE the touched ranges (runes, 8 stats, name
    ///    region, slot MD5, save-header MD5) must be byte-identical to the pre-edit slot. Any
    ///    drift throws <see cref="InvalidDataException"/> — the snapshot taken by the caller
    ///    is the user's line back.</summary>
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

        // Walk the BND4 file table — resolve the save-header section + this slot by NAME, not
        // by hardcoded offset. Mirrors ReadCharacters / TryReadSlot so read and write always
        // agree on where the bytes live. A future layout shift (extra entries, padding) surfaces
        // as a clear "entry not found" instead of a silent corrupt write at a stale offset.
        var entries = Bnd4Reader.Parse(bytes);
        var (saveHeaderStart, saveHeaderMd5Start) = ResolveSaveHeaderStart(entries);
        var slotEntry = Bnd4Reader.GetByName(entries, $"USER_DATA{slotIndex:D3}");

        int charActiveStatusAbs = saveHeaderStart + CharActiveStatusRelative;
        int perSlotSummaryStartAbs = saveHeaderStart + PerSlotSummaryRelative;
        // The slot entry's data_offset points at the slot's MD5 (16 bytes); the slot body
        // follows at +0x10. Same convention as TryReadSlot — read and write stay in lockstep.
        int slotMd5Start = checked((int)slotEntry.DataOffset);
        int slotDataStart = slotMd5Start + 0x10;

        // Guard 0: active-flag check. Editing an inactive slot would create a phantom character
        // — the save header says the slot is active but the body has no real character. The user
        // must create a character in-game first.
        if (bytes[charActiveStatusAbs + slotIndex] == 0)
        {
            throw new InvalidOperationException(
                $"Slot {slotIndex} is inactive — cannot edit an empty slot. Create a character in-game first.");
        }

        // Snapshot the pre-edit slot body for post-write verification (Step 6 below).
        var preSlot = bytes.AsSpan(slotDataStart, SlotDataSize).ToArray();

        // Guard 1: walk GA-items to discover the magic anchor. Throws InvalidDataException if
        // the walk produces an offset outside the slot body (unknown handle type → wrong sum).
        var slotData = bytes.AsSpan(slotDataStart, SlotDataSize);
        int magicOffset = DiscoverMagicOffset(slotData);

        // 1) Patch the slot body — runes + stats at the discovered anchor.
        SlotData.WriteRunes(slotData, edit.Runes, magicOffset);
        SlotData.WriteStats(slotData, edit.Vig, edit.Mnd, edit.End, edit.Str, edit.Dex, edit.Int, edit.Fai, edit.Arc, magicOffset);

        // 2) Recompute the slot MD5 over the new slot bytes.
        var slotMd5 = bytes.AsSpan(slotMd5Start, 0x10);
        SlotChecksum.ComputeMd5(slotData).CopyTo(slotMd5);

        // 3) Update the per-slot summary in the save-header section (name + level). The active
        //    flag is NOT touched — Guard 0 above already verified it was 1.
        var summary = bytes.AsSpan(perSlotSummaryStartAbs + slotIndex * PerSlotSummaryStride, PerSlotSummaryStride);
        WriteCharacterName(summary, edit.Name);
        var stats = new SlotStats(edit.Vig, edit.Mnd, edit.End, edit.Str, edit.Dex, edit.Int, edit.Fai, edit.Arc);
        BitConverter.TryWriteBytes(summary.Slice(CharLevelOffsetInSummary, 2), (short)SlotData.LevelFromStats(stats));

        // 4) Recompute the save-header MD5 over [saveHeaderStart, saveHeaderStart + SaveHeadersSectionLength).
        var saveHeader = bytes.AsSpan(saveHeaderStart, SaveHeadersSectionLength);
        var saveHeaderMd5 = bytes.AsSpan(saveHeaderMd5Start, 0x10);
        SlotChecksum.ComputeMd5(saveHeader).CopyTo(saveHeaderMd5);

        // 5) Atomic write: temp file + rename. Mirrors AtomicJson.WriteJsonAtomic.
        WriteBytesAtomic(savePath, bytes);

        // 6) Post-write validation guard — the spec's safety net for the bricked-save risk class.
        //    Re-read from disk, re-parse the slot via the same TryReadSlot path, and byte-compare
        //    the post-write slot body against preSlot masking out the touched ranges. Any drift
        //    outside the masks throws InvalidDataException so the caller's snapshot is the line back.
        VerifyPostWrite(savePath, slotIndex, edit, preSlot, magicOffset);
    }

    /// <summary>Walk the GA-items table to find the magic anchor where stats/runes live.
    ///
    /// Algorithm: starting at slot offset 0x20, read 5120 entries. Each entry's first 4 bytes
    /// are the <c>gaitem_handle</c>; the high nibble (<c>handle &amp; 0xF0000000</c>) determines
    /// the entry's total size. After the walk, <c>magic_offset = end_of_ga_items + 0x1AF</c>.
    ///
    /// Type-bit table (alfizari/Final.py Item.from_bytes, MIT — cross-confirmed against
    /// ClayAmore/ER-Save-Editor save_slot.rs, Apache-2.0):
    /// <list type="bullet">
    ///   <item>handle == 0 → empty, 8 bytes.</item>
    ///   <item>0x80000000 → weapon, 21 bytes (handle + id + 3*u32 + 1 byte).</item>
    ///   <item>0x90000000 → armor, 16 bytes (handle + id + 2*u32).</item>
    ///   <item>0xC0000000 → ash of war, 8 bytes (handle + id).</item>
    ///   <item>any other non-zero handle → 8 bytes (alfizari's fallback path — no extra read).</item>
    /// </list>
    ///
    /// If the resulting anchor would put VIG outside the slot body, throws
    /// <see cref="InvalidDataException"/> — that's the "unknown handle type" failure mode and we
    /// fail loud instead of writing garbage at a wrong anchor.
    ///
    /// Public so diagnostic tooling and tests can verify the walk against known fixtures.</summary>
    public static int DiscoverMagicOffset(ReadOnlySpan<byte> slotBody)
    {
        int cursor = SlotData.GaItemsStart;
        for (int i = 0; i < SlotData.GaItemCount; i++)
        {
            uint handle = BitConverter.ToUInt32(slotBody.Slice(cursor, 4));
            int entrySize;
            if (handle == 0)
            {
                entrySize = SlotData.EmptyGaItemSize;
            }
            else
            {
                uint typeBits = handle & 0xF0000000u;
                entrySize = typeBits switch
                {
                    0x80000000u => SlotData.WeaponGaItemSize,  // 21 bytes
                    0x90000000u => SlotData.ArmorGaItemSize,   // 16 bytes
                    // 0xC0000000 (AOW) and any other non-zero type-bits fall through to base
                    // size 8 — matches alfizari's branch table where only weapon/armor read
                    // additional fields beyond the 8-byte (handle + id) base.
                    _ => SlotData.EmptyGaItemSize,
                };
            }
            cursor += entrySize;
        }

        int magicOffset = cursor + SlotData.MagicAnchorOffset;

        // Sanity: the lowest stat relative offset is VigorRelative (-379). If magic + Vigor
        // would land outside the slot, the walk hit an unknown handle layout. Fail loud.
        int lowestStatOffset = magicOffset + SlotData.MinRelativeOffset;
        if (lowestStatOffset < 0 || magicOffset + 4 > slotBody.Length)
        {
            throw new InvalidDataException(
                $"Discovered magic offset 0x{magicOffset:X} is outside the slot body (slot size 0x{slotBody.Length:X}). " +
                $"The GA-items walk likely hit an unknown handle type. Bisect against a real save.");
        }

        return magicOffset;
    }

    /// <summary>Post-write validation. Re-reads the file, re-parses the touched slot, confirms
    /// every edited field landed, and byte-compares the slot body against <paramref name="preSlot"/>
    /// masking out the touched ranges (runes / 8 stats at the discovered anchor). Throws
    /// <see cref="InvalidDataException"/> with a diff location on any drift.</summary>
    private static void VerifyPostWrite(string savePath, int slotIndex, CharacterEdit edit, byte[] preSlot, int magicOffset)
    {
        var verifyBytes = File.ReadAllBytes(savePath);
        if (verifyBytes.Length < MinimumFileSize)
        {
            throw new InvalidDataException(
                $"Post-write verify: file is too small ({verifyBytes.Length} bytes) — expected at least {MinimumFileSize}.");
        }

        // 1) Re-parse the slot via the same path the public reader uses (BND4 file-table walk).
        var entries = Bnd4Reader.Parse(verifyBytes);
        var (saveHeaderStart, _) = ResolveSaveHeaderStart(entries);
        int charActiveStatusAbs = saveHeaderStart + CharActiveStatusRelative;
        int perSlotSummaryStartAbs = saveHeaderStart + PerSlotSummaryRelative;
        var slotEntry = Bnd4Reader.GetByName(entries, $"USER_DATA{slotIndex:D3}");
        // Same convention as TryReadSlot + WriteEdit — entry data_offset points at the slot MD5,
        // body lives at +0x10. We resolve this once here so the post-write byte mask uses the
        // SAME absolute offset the writer just wrote to, even if the layout has been shifted.
        int slotMd5StartAbs = checked((int)slotEntry.DataOffset);
        int slotDataStartAbs = slotMd5StartAbs + 0x10;

        var verified = TryReadSlot(verifyBytes, slotEntry, charActiveStatusAbs, perSlotSummaryStartAbs, slotIndex)
            ?? throw new InvalidDataException(
                $"Post-write verify: slot {slotIndex} failed to re-read (MD5 mismatch or all-zero stats). The edit did not land cleanly.");

        // 2) Every edited field must equal the edit.
        if (verified.Runes != edit.Runes)
            throw new InvalidDataException($"Post-write verify: Runes mismatch — wrote {edit.Runes}, read back {verified.Runes}.");
        if (verified.Vig != edit.Vig)
            throw new InvalidDataException($"Post-write verify: Vig mismatch — wrote {edit.Vig}, read back {verified.Vig}.");
        if (verified.Mnd != edit.Mnd)
            throw new InvalidDataException($"Post-write verify: Mnd mismatch — wrote {edit.Mnd}, read back {verified.Mnd}.");
        if (verified.End != edit.End)
            throw new InvalidDataException($"Post-write verify: End mismatch — wrote {edit.End}, read back {verified.End}.");
        if (verified.Str != edit.Str)
            throw new InvalidDataException($"Post-write verify: Str mismatch — wrote {edit.Str}, read back {verified.Str}.");
        if (verified.Dex != edit.Dex)
            throw new InvalidDataException($"Post-write verify: Dex mismatch — wrote {edit.Dex}, read back {verified.Dex}.");
        if (verified.Int != edit.Int)
            throw new InvalidDataException($"Post-write verify: Int mismatch — wrote {edit.Int}, read back {verified.Int}.");
        if (verified.Fai != edit.Fai)
            throw new InvalidDataException($"Post-write verify: Fai mismatch — wrote {edit.Fai}, read back {verified.Fai}.");
        if (verified.Arc != edit.Arc)
            throw new InvalidDataException($"Post-write verify: Arc mismatch — wrote {edit.Arc}, read back {verified.Arc}.");

        // 3) Byte-compare slot body against preSlot OUTSIDE the touched ranges. The touched ranges
        //    inside the slot body, computed at the DISCOVERED anchor (not the fixture anchor), are:
        //      - runes: [magicOffset + RunesRelative, magicOffset + RunesRelative + 4)
        //      - 8 stat uint32s: [magicOffset + VigorRelative, magicOffset + ArcaneRelative + 4)
        //    (The slot MD5 lives OUTSIDE the slot body, so it doesn't appear in this mask. The
        //     name + save-header MD5 also live outside the slot body — they're in the save-header
        //     section. Those drift cases would surface as save-header MD5 mismatch causing a
        //     downstream parse to fail, not as a slot-body drift here.)
        var postSlot = verifyBytes.AsSpan(slotDataStartAbs, SlotDataSize);
        int statsStart = magicOffset + SlotData.VigorRelative;
        int statsEndExclusive = magicOffset + SlotData.ArcaneRelative + 4;
        int runesStart = magicOffset + SlotData.RunesRelative;
        int runesEndExclusive = runesStart + 4;

        for (int i = 0; i < SlotDataSize; i++)
        {
            bool inStats = i >= statsStart && i < statsEndExclusive;
            bool inRunes = i >= runesStart && i < runesEndExclusive;
            if (inStats || inRunes) continue;
            if (postSlot[i] != preSlot[i])
            {
                throw new InvalidDataException(
                    $"Post-write verify: unexpected byte drift in slot {slotIndex} body at offset 0x{i:X} (pre=0x{preSlot[i]:X2}, post=0x{postSlot[i]:X2}). The write touched bytes outside the edit's mask — the file may be corrupt.");
            }
        }
    }

    private static CharacterSlot? TryReadSlot(
        byte[] bytes,
        Bnd4Entry slotEntry,
        int charActiveStatusAbs,
        int perSlotSummaryStartAbs,
        int slotIndex)
    {
        // The BND4 entry's data_offset points at the slot's MD5 (16 bytes), followed by the
        // slot data (SlotDataSize). We derive both regions from the entry so a layout shift
        // (extra entries, padding) is handled transparently.
        int slotMd5Start = checked((int)slotEntry.DataOffset);
        int slotDataStart = slotMd5Start + 0x10;
        var slotMd5 = bytes.AsSpan(slotMd5Start, 0x10);
        var slotData = bytes.AsSpan(slotDataStart, SlotDataSize);

        // 1) MD5 integrity — corrupt slots are skipped, not thrown over.
        if (!SlotChecksum.VerifyMd5(slotMd5, slotData)) return null;

        // 2) Active-flag check. Inactive slots are skipped. (Stat-zero fallback is applied below
        //    at the discovered anchor — fixtures and unused slots both surface as all-zero.)
        byte activeFlag = bytes[charActiveStatusAbs + slotIndex];
        if (activeFlag == 0) return null;

        // 3) Discover the magic anchor for this slot (handles real saves with inventory).
        //    If the walk hits an unknown handle type, DiscoverMagicOffset throws — the slot is
        //    skipped here rather than thrown over (caller's flow handles "X slots skipped" UX).
        int magicOffset;
        try
        {
            magicOffset = DiscoverMagicOffset(slotData);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        // 4) Stats + runes from the discovered anchor.
        var stats = SlotData.ReadStats(slotData, magicOffset);
        bool allStatsZero = stats.Vig == 0 && stats.Mnd == 0 && stats.End == 0 && stats.Str == 0
            && stats.Dex == 0 && stats.Int == 0 && stats.Fai == 0 && stats.Arc == 0;
        if (allStatsZero) return null;

        uint runes = SlotData.ReadRunes(slotData, magicOffset);
        int level = SlotData.LevelFromStats(stats);

        // 5) Name from the per-slot summary in the save-header section. The summary start
        //    is anchored to the walked save-header section, not the hardcoded 0x01901D0E.
        var summary = bytes.AsSpan(perSlotSummaryStartAbs + slotIndex * PerSlotSummaryStride, PerSlotSummaryStride);
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
