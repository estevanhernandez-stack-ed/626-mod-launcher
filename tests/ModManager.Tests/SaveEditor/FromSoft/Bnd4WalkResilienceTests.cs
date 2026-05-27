using System.IO;
using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.Tests.SaveEditor.FromSoft;

/// <summary>
/// The resilience contract for the BND4 file-table walk. The whole point of replacing
/// hardcoded offsets with a NAME-based walk is so a future ER patch that shifts the layout
/// (extra entries — the DLC already added <c>USER_DATA012</c>) or renames a section either:
///   1. Still reads correctly via the walk (extra-entry case), OR
///   2. Fails LOUD with a clear <see cref="InvalidDataException"/> listing the names that
///      WERE found (renamed-header case) — never silently reads the wrong region.
///
/// Task 3 covered the shifted-offset variant in <c>EldenRingSaveTests</c>
/// (<c>Read_finds_save_header_by_name_when_section_is_relocated</c>). This file adds the
/// entry-list variants: more entries than expected, and a missing expected entry.
/// </summary>
public class Bnd4WalkResilienceTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "er-resilience-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string WriteToTemp(byte[] bytes, string fileName = "ER0000.sl2")
    {
        Directory.CreateDirectory(_tmp);
        var path = Path.Combine(_tmp, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Reader_handles_save_with_extra_dlc_entry()
    {
        // Future ER patch / DLC adds USER_DATA012 to the BND4 file table — the same layout
        // shift the real DLC already shipped. ReadCharacters must walk the file table by
        // name and find USER_DATA011 even though it's no longer the last entry.
        var bytes = EldenRingFixture.BuildSaveWithExtraEntry(
            name: "Extra",
            runes: 12_345u,
            vig: 20, mnd: 15, end_: 25, str: 30, dex: 12, int_: 10, fai: 10, arc: 10);
        var path = WriteToTemp(bytes);

        var slots = EldenRingSave.ReadCharacters(path);

        var slot = Assert.Single(slots);
        Assert.Equal("Extra", slot.Name);
        Assert.Equal(12_345u, slot.Runes);
        Assert.Equal(20, slot.Vig);
        Assert.Equal(15, slot.Mnd);
        Assert.Equal(25, slot.End);
        Assert.Equal(30, slot.Str);
        Assert.Equal(12, slot.Dex);
        Assert.Equal(10, slot.Int);
        Assert.Equal(10, slot.Fai);
        Assert.Equal(10, slot.Arc);
    }

    [Fact]
    public void Writer_handles_save_with_extra_dlc_entry()
    {
        // Same shape as the reader test — the writer must locate USER_DATA000 (slot 0) and
        // USER_DATA011 (save header) by NAME via the walk, then patch + verify cleanly even
        // though a 12th entry exists.
        var bytes = EldenRingFixture.BuildSaveWithExtraEntry(
            name: "Before",
            runes: 1u,
            vig: 10, mnd: 10, end_: 10, str: 10, dex: 10, int_: 10, fai: 10, arc: 10);
        var path = WriteToTemp(bytes);

        EldenRingSave.WriteEdit(path, slotIndex: 0, new CharacterEdit(
            Name: "Patched",
            Runes: 999u,
            Vig: 50, Mnd: 16, End: 30, Str: 25, Dex: 15, Int: 12, Fai: 12, Arc: 12));

        var slots = EldenRingSave.ReadCharacters(path);
        var slot = Assert.Single(slots);
        Assert.Equal("Patched", slot.Name);
        Assert.Equal(999u, slot.Runes);
        Assert.Equal(50, slot.Vig);
        Assert.Equal(16, slot.Mnd);
        Assert.Equal(30, slot.End);
        Assert.Equal(25, slot.Str);
        Assert.Equal(15, slot.Dex);
        Assert.Equal(12, slot.Int);
        Assert.Equal(12, slot.Fai);
        Assert.Equal(12, slot.Arc);
    }

    [Fact]
    public void Reader_fails_loud_when_save_header_entry_is_missing()
    {
        // Future patch renames USER_DATA011 — the global save-header slot — to something
        // else (here USER_DATA099). The walker must NEVER silently read the wrong region;
        // it must throw InvalidDataException whose message lists the names that WERE found,
        // so a developer reading the failure can see exactly what the patch changed.
        var bytes = EldenRingFixture.BuildSaveWithRenamedSaveHeader(
            renamedTo: "USER_DATA099",
            name: "Doesn'tMatter",
            runes: 1u,
            vig: 10, mnd: 10, end_: 10, str: 10, dex: 10, int_: 10, fai: 10, arc: 10);
        var path = WriteToTemp(bytes);

        var ex = Assert.Throws<InvalidDataException>(() => EldenRingSave.ReadCharacters(path));
        Assert.Contains("USER_DATA011", ex.Message);
        Assert.Contains("USER_DATA099", ex.Message);
    }
}
