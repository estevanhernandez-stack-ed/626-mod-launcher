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
/// anchor offset: <c>magic_offset = end_of_ga_items_table + 0x1AF</c>, where the GA-items table
/// has 5120 variable-size entries (8 bytes if empty/good/ring, 13 bytes if weapon, 16 bytes if
/// armor). Stat offsets are relative to that anchor.
///
/// For the MVP fixture (Task 3) we assume an ALL-EMPTY GA-items region, which gives a fixed
/// anchor at <c>0x20 + 5120*8 + 0x1AF = 0xA1CF</c>. The constants below are computed from that
/// fixture anchor + the relative offsets in alfizari/Elden-Ring-Save-Editor. Task 4 will add
/// runtime anchor discovery for real saves.
///
/// Sources (all permissively licensed):
/// - Stat / runes relative offsets and 4-byte uint32 layout:
///   alfizari/Elden-Ring-Save-Editor, src/Final.py lines 17–32 + 660–745 (MIT).
/// - Field types confirmed against ClayAmore/ER-Save-Editor, src/save/common/save_slot.rs
///   PlayerGameData struct (Apache-2.0).
/// - Slot-size + BND4 layout: BenGrn/EldenRingSaveCopier, Saves/Model/SaveGame.cs (MIT).
///
/// The two references agree on offsets and types. If a future ER patch shifts them, change
/// only the constants here.
/// </summary>
public static class SlotData
{
    /// <summary>Full slot data size per the BND4 layout — see research note.</summary>
    public const int SlotSize = 0x280000;

    // --- Fixture anchor ---
    // GA-items table starts at 0x20, has 5120 entries. Each entry is 8 bytes when the slot
    // contains no real weapon/armor (gaitem_handle == 0 → no type-bits extra payload). So
    // end_of_ga = 0x20 + 5120*8 = 0xA020, and the alfizari "magic" anchor sits 0x1AF after:
    internal const int GaItemsStart = 0x20;
    internal const int GaItemCountForFixture = 5120;
    internal const int EmptyGaItemSize = 8;
    internal const int EndOfGaItemsForEmptyFixture = GaItemsStart + GaItemCountForFixture * EmptyGaItemSize;       // 0xA020
    internal const int MagicAnchorOffset = 0x1AF;
    internal const int FixtureMagicOffset = EndOfGaItemsForEmptyFixture + MagicAnchorOffset;                       // 0xA1CF

    // --- Relative offsets from the magic anchor (alfizari/Final.py lines 8–32) ---
    private const int RunesRelative = -331;   // souls_distance
    private const int LevelRelative = -335;
    private const int VigorRelative = -379;
    private const int MindRelative = -375;
    private const int EnduranceRelative = -371;
    private const int StrengthRelative = -367;
    private const int DexterityRelative = -363;
    private const int IntelligenceRelative = -359;
    private const int FaithRelative = -355;
    private const int ArcaneRelative = -351;

    // --- Absolute offsets for the fixture anchor ---
    // Stats span 0xA054..0xA073 (8 contiguous uint32s, VIG → ARC). Runes at 0xA084.
    internal const int OffsetRunes = FixtureMagicOffset + RunesRelative;          // 0xA084
    internal const int OffsetLevel = FixtureMagicOffset + LevelRelative;          // 0xA080
    internal const int OffsetVigor = FixtureMagicOffset + VigorRelative;          // 0xA054
    internal const int OffsetMind = FixtureMagicOffset + MindRelative;            // 0xA058
    internal const int OffsetEndurance = FixtureMagicOffset + EnduranceRelative;  // 0xA05C
    internal const int OffsetStrength = FixtureMagicOffset + StrengthRelative;    // 0xA060
    internal const int OffsetDexterity = FixtureMagicOffset + DexterityRelative;  // 0xA064
    internal const int OffsetIntelligence = FixtureMagicOffset + IntelligenceRelative; // 0xA068
    internal const int OffsetFaith = FixtureMagicOffset + FaithRelative;          // 0xA06C
    internal const int OffsetArcane = FixtureMagicOffset + ArcaneRelative;        // 0xA070

    /// <summary>Read the rune total as little-endian uint32.</summary>
    public static uint ReadRunes(ReadOnlySpan<byte> slot)
        => BitConverter.ToUInt32(slot.Slice(OffsetRunes, 4));

    /// <summary>Write the rune total as little-endian uint32. Overwrites all 4 bytes.</summary>
    public static void WriteRunes(Span<byte> slot, uint runes)
        => BitConverter.TryWriteBytes(slot.Slice(OffsetRunes, 4), runes);

    /// <summary>Read all 8 attributes. Each is on-disk a uint32; we surface the low byte
    /// because ER stat values cap at 99 (well within byte range).</summary>
    public static SlotStats ReadStats(ReadOnlySpan<byte> slot)
    {
        return new SlotStats(
            (byte)BitConverter.ToUInt32(slot.Slice(OffsetVigor, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(OffsetMind, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(OffsetEndurance, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(OffsetStrength, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(OffsetDexterity, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(OffsetIntelligence, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(OffsetFaith, 4)),
            (byte)BitConverter.ToUInt32(slot.Slice(OffsetArcane, 4)));
    }

    /// <summary>Write all 8 attributes. Each value is zero-extended to uint32 on disk; the
    /// upper 3 bytes are always written as zero, which matches the observed-in-the-wild
    /// behavior (real saves with stats 1–99 have upper bytes = 0).</summary>
    public static void WriteStats(Span<byte> slot, byte vig, byte mnd, byte end_, byte str, byte dex, byte int_, byte fai, byte arc)
    {
        BitConverter.TryWriteBytes(slot.Slice(OffsetVigor, 4), (uint)vig);
        BitConverter.TryWriteBytes(slot.Slice(OffsetMind, 4), (uint)mnd);
        BitConverter.TryWriteBytes(slot.Slice(OffsetEndurance, 4), (uint)end_);
        BitConverter.TryWriteBytes(slot.Slice(OffsetStrength, 4), (uint)str);
        BitConverter.TryWriteBytes(slot.Slice(OffsetDexterity, 4), (uint)dex);
        BitConverter.TryWriteBytes(slot.Slice(OffsetIntelligence, 4), (uint)int_);
        BitConverter.TryWriteBytes(slot.Slice(OffsetFaith, 4), (uint)fai);
        BitConverter.TryWriteBytes(slot.Slice(OffsetArcane, 4), (uint)arc);
    }

    /// <summary>Level = sum of attributes - 79. Wretch (all 10s) sums to 80, level = 1. Each
    /// attribute point above the 10-baseline adds a level. Verify against a real save in
    /// Task 9 — different starting classes share this formula because each class's starting
    /// stat sum equals (79 + starting_level).</summary>
    public static int LevelFromStats(SlotStats s)
        => s.Vig + s.Mnd + s.End + s.Str + s.Dex + s.Int + s.Fai + s.Arc - 79;
}
