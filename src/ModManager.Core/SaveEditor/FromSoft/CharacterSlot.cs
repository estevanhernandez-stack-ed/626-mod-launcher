namespace ModManager.Core.SaveEditor.FromSoft;

/// <summary>One character's read-only state at parse-time. Returned from
/// <see cref="EldenRingSave.ReadCharacters"/>. The eight attribute fields are intentionally
/// individual properties (not a Dictionary) so the call sites stay strongly typed.</summary>
public sealed record CharacterSlot(
    int SlotIndex,
    string Name,
    string Class,
    int Level,
    uint Runes,
    byte Vig, byte Mnd, byte End, byte Str, byte Dex, byte Int, byte Fai, byte Arc,
    string SteamId);
