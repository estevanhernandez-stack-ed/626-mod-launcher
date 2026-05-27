namespace ModManager.Core.SaveEditor.FromSoft;

/// <summary>Eight-attribute snapshot. Mirrors the in-save layout (each attribute is a 4-byte
/// uint32 on disk; only the low byte is exposed because real ER stats cap at 99). Field order
/// matches the on-disk write order: VIG, MND, END, STR, DEX, INT, FAI, ARC.</summary>
public readonly record struct SlotStats(byte Vig, byte Mnd, byte End, byte Str, byte Dex, byte Int, byte Fai, byte Arc);

/// <summary>
/// Reads + writes the load-bearing fields within ONE character slot's plaintext body. The slot
/// is 0x280000 bytes per the BND4 file layout (research note); we only touch a few fields.
///
/// IMPORTANT — anchor model. In real ER saves, stats and runes sit at a runtime-discovered
/// anchor offset: <c>magic_offset = end_of_ga_items_table + 0x1AF</c>. The GA-items table has
/// 5120 variable-size entries; sizes depend on the high nibble (type-bits) of each entry's
/// <c>gaitem_handle</c>. <see cref="EldenRingSave.DiscoverMagicOffset"/> walks that table to
/// produce the anchor for a real slot.
///
/// Stats / runes / level all live at fixed-distance offsets RELATIVE to that anchor. Every
/// reader/writer here takes the discovered anchor as a parameter so the same code path serves
/// both the fixture (empty inventory → anchor at <see cref="FixtureMagicOffset"/> = 0xA1CF)
/// and real saves (variable anchor).
///
/// Sources (all permissively licensed):
/// - Stat / runes relative offsets and 4-byte uint32 layout:
///   alfizari/Elden-Ring-Save-Editor, src/Final.py (MIT).
/// - Field types + GA-items entry structure confirmed against ClayAmore/ER-Save-Editor,
///   src/save/common/save_slot.rs PlayerGameData / GaItem read paths (Apache-2.0).
/// - Slot-size + BND4 layout: BenGrn/EldenRingSaveCopier, Saves/Model/SaveGame.cs (MIT).
/// </summary>
public static class SlotData
{
    /// <summary>Full slot data size per the BND4 layout — see research note.</summary>
    public const int SlotSize = 0x280000;

    // --- GA-items table layout (used by EldenRingSave.DiscoverMagicOffset) ---
    // Table starts at slot-body offset 0x20 and has 5120 entries. Each entry's size depends on
    // the high nibble of gaitem_handle:
    //
    //   0x00000000 (empty / fallback)  →  8 bytes   (handle + item_id)
    //   0x80000000 (weapon)            → 21 bytes   (handle + id + 3*u32 + 1 byte)
    //   0x90000000 (armor)             → 16 bytes   (handle + id + 2*u32)
    //   0xC0000000 (AOW / ash of war)  →  8 bytes
    //   (any other handle != 0)        →  8 bytes   (safe fallback — alfizari Final.py path)
    //
    // Sources: alfizari/Elden-Ring-Save-Editor src/Final.py Item.from_bytes (authoritative for
    // ER 1.13+, MIT); cross-confirmed against ClayAmore/ER-Save-Editor src/save/common/save_slot.rs
    // GaItem read logic (Apache-2.0 — uses item_id discriminator with different field order;
    // both references agree on the 21/16/8 entry sizes).
    internal const int GaItemsStart = 0x20;
    internal const int GaItemCount = 5120;
    internal const int EmptyGaItemSize = 8;
    internal const int WeaponGaItemSize = 21;
    internal const int ArmorGaItemSize = 16;
    internal const int MagicAnchorOffset = 0x1AF;

    // Backwards-compat alias retained for the empty-inventory fixture math (was named
    // GaItemCountForFixture historically). Same value as GaItemCount — the count is fixed.
    internal const int GaItemCountForFixture = GaItemCount;
    internal const int EndOfGaItemsForEmptyFixture = GaItemsStart + GaItemCount * EmptyGaItemSize; // 0xA020

    /// <summary>The magic anchor offset for an all-empty GA-items table (the fixture case).
    /// Real saves discover this at runtime via <see cref="EldenRingSave.DiscoverMagicOffset"/>;
    /// the fixture uses this constant directly when planting stats/runes.</summary>
    public const int FixtureMagicOffset = EndOfGaItemsForEmptyFixture + MagicAnchorOffset; // 0xA1CF

    // --- Relative offsets from the magic anchor (alfizari/Final.py lines 8–32) ---
    // Made internal so EldenRingSave (same assembly) can build its verification mask without
    // duplicating the constants.
    internal const int RunesRelative = -331;   // souls_distance
    internal const int LevelRelative = -335;
    internal const int VigorRelative = -379;
    internal const int MindRelative = -375;
    internal const int EnduranceRelative = -371;
    internal const int StrengthRelative = -367;
    internal const int DexterityRelative = -363;
    internal const int IntelligenceRelative = -359;
    internal const int FaithRelative = -355;
    internal const int ArcaneRelative = -351;

    /// <summary>The most-negative relative offset (Vigor is furthest below the anchor). Used by
    /// the anchor-bounds sanity check in <see cref="EldenRingSave.DiscoverMagicOffset"/>.</summary>
    internal const int MinRelativeOffset = VigorRelative;

    // --- Fixture-anchor absolute offsets (used by VerifyPostWrite mask + legacy tests) ---
    internal const int OffsetRunes = FixtureMagicOffset + RunesRelative;          // 0xA084
    internal const int OffsetVigor = FixtureMagicOffset + VigorRelative;          // 0xA054
    internal const int OffsetArcane = FixtureMagicOffset + ArcaneRelative;        // 0xA070

    /// <summary>Read the rune total as little-endian uint32 from the slot, relative to the
    /// discovered magic anchor.</summary>
    public static uint ReadRunes(ReadOnlySpan<byte> slot, int magicOffset)
        => BitConverter.ToUInt32(slot.Slice(magicOffset + RunesRelative, 4));

    /// <summary>Write the rune total as little-endian uint32. Overwrites all 4 bytes at
    /// <c>magicOffset + RunesRelative</c>.</summary>
    public static void WriteRunes(Span<byte> slot, uint runes, int magicOffset)
        => BitConverter.TryWriteBytes(slot.Slice(magicOffset + RunesRelative, 4), runes);

    /// <summary>Read all 8 attributes relative to the discovered magic anchor. Each is on-disk a
    /// uint32; we surface the low byte because ER stat values cap at 99 (well within byte range).</summary>
    public static SlotStats ReadStats(ReadOnlySpan<byte> slot, int magicOffset)
    {
        return new SlotStats(
            (byte)BitConverter.ToUInt32(slot.Slice(magicOffset + VigorRelative, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(magicOffset + MindRelative, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(magicOffset + EnduranceRelative, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(magicOffset + StrengthRelative, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(magicOffset + DexterityRelative, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(magicOffset + IntelligenceRelative, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(magicOffset + FaithRelative, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(magicOffset + ArcaneRelative, 4)));
    }

    /// <summary>Write all 8 attributes relative to the discovered magic anchor. Each value is
    /// zero-extended to uint32 on disk; the upper 3 bytes are always written as zero, which
    /// matches the observed-in-the-wild behavior (real saves with stats 1–99 have upper bytes = 0).</summary>
    public static void WriteStats(Span<byte> slot, byte vig, byte mnd, byte end_, byte str, byte dex, byte int_, byte fai, byte arc, int magicOffset)
    {
        BitConverter.TryWriteBytes(slot.Slice(magicOffset + VigorRelative, 4), (uint)vig);
        BitConverter.TryWriteBytes(slot.Slice(magicOffset + MindRelative, 4), (uint)mnd);
        BitConverter.TryWriteBytes(slot.Slice(magicOffset + EnduranceRelative, 4), (uint)end_);
        BitConverter.TryWriteBytes(slot.Slice(magicOffset + StrengthRelative, 4), (uint)str);
        BitConverter.TryWriteBytes(slot.Slice(magicOffset + DexterityRelative, 4), (uint)dex);
        BitConverter.TryWriteBytes(slot.Slice(magicOffset + IntelligenceRelative, 4), (uint)int_);
        BitConverter.TryWriteBytes(slot.Slice(magicOffset + FaithRelative, 4), (uint)fai);
        BitConverter.TryWriteBytes(slot.Slice(magicOffset + ArcaneRelative, 4), (uint)arc);
    }

    /// <summary>Level = sum of attributes - 79. Wretch (all 10s) sums to 80, level = 1. Each
    /// attribute point above the 10-baseline adds a level. Verify against a real save in
    /// Task 9 — different starting classes share this formula because each class's starting
    /// stat sum equals (79 + starting_level).</summary>
    public static int LevelFromStats(SlotStats s)
        => s.Vig + s.Mnd + s.End + s.Str + s.Dex + s.Int + s.Fai + s.Arc - 79;
}
