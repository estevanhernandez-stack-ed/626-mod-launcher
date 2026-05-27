namespace ModManager.Core.SaveEditor.FromSoft;

/// <summary>The set of fields a single edit changes on a slot. All fields are required — the UI
/// pre-fills with the current values, so an "unchanged" edit is just the same value re-applied.
/// This keeps the write path uniform (always overwrite all 8 attributes + runes + name) and
/// avoids the bug class where "only-edit-runes" loses other state to default values.</summary>
public sealed record CharacterEdit(
    string Name,
    uint Runes,
    byte Vig, byte Mnd, byte End, byte Str, byte Dex, byte Int, byte Fai, byte Arc);
