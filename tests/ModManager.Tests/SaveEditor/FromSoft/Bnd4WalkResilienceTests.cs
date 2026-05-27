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
    public void ResolveSaveHeaderStart_picks_USER_DATA010_in_current_real_er_shape()
    {
        // Current real ER (.sl2/.co2/.err after the post-DLC patch) packs the save header into
        // USER_DATA010 (size 0x60010 = 16-byte MD5 + 0x60000 section) and reserves USER_DATA011
        // for an unrelated appended section. The resolver must prefer USER_DATA010 when it has
        // the right size, and compute section/MD5 starts correctly from it.
        var entries = new List<Bnd4Entry>
        {
            new("USER_DATA000", DataOffset: 0x300,     DataSize: 0x280010),
            new("USER_DATA009", DataOffset: 0x1680390, DataSize: 0x280010),
            new("USER_DATA010", DataOffset: 0x19003A0, DataSize: 0x60010),    // MD5 + section
            new("USER_DATA011", DataOffset: 0x19603B0, DataSize: 0x240020),   // unrelated DLC section
        };

        var (sectionStart, md5Start) = EldenRingSave.ResolveSaveHeaderStart(entries);

        // Section starts at MD5+0x10, MD5 starts at the entry's data_offset.
        Assert.Equal(0x19003B0, sectionStart);
        Assert.Equal(0x19003A0, md5Start);
    }

    [Fact]
    public void ResolveSaveHeaderStart_falls_back_to_USER_DATA011_when_USER_DATA010_missing()
    {
        // Pre-DLC / fixture shape: only USER_DATA011 exists for the save header, and its
        // data_offset points at the section data (NOT at the preceding MD5). The resolver
        // must compute MD5 start as section - 0x10.
        var entries = new List<Bnd4Entry>
        {
            new("USER_DATA000", DataOffset: 0x320,     DataSize: 0x280010),
            new("USER_DATA009", DataOffset: 0x16803B0, DataSize: 0x280010),
            new("USER_DATA011", DataOffset: 0x19003B0, DataSize: 0x60000),
        };

        var (sectionStart, md5Start) = EldenRingSave.ResolveSaveHeaderStart(entries);

        Assert.Equal(0x19003B0, sectionStart);
        Assert.Equal(0x19003A0, md5Start);
    }

    [Fact]
    public void ResolveSaveHeaderStart_skips_USER_DATA010_when_size_doesnt_match_and_falls_back()
    {
        // Defensive: if a future variant adds a USER_DATA010 entry with a size that isn't the
        // save-header bundle (0x60010), the resolver shouldn't blindly grab it — it should fall
        // back to USER_DATA011.
        var entries = new List<Bnd4Entry>
        {
            new("USER_DATA010", DataOffset: 0x100, DataSize: 0x500),       // wrong size — ignored
            new("USER_DATA011", DataOffset: 0x19003B0, DataSize: 0x60000), // legacy fallback
        };

        var (sectionStart, _) = EldenRingSave.ResolveSaveHeaderStart(entries);

        Assert.Equal(0x19003B0, sectionStart);
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
