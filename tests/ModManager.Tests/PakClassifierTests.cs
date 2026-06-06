using ModManager.Core;

namespace ModManager.Tests;

public class PakClassifierTests
{
    [Theory]
    [InlineData("pakchunk0-WindowsNoEditor.pak", 4L * 1024 * 1024 * 1024, true)]   // base: name + size
    [InlineData("pakchunk0optional-WindowsNoEditor.pak", 591L * 1024 * 1024, true)] // base: name only (modest size)
    [InlineData("pakchunk30-2x-witchfire_P.pak", 6 * 1024, false)]                 // mod
    [InlineData("zz_Funner_Witchfire.pak", 22L * 1024 * 1024, false)]              // mod
    public void Classifies_Witchfire_paks(string name, long size, bool expectedBase)
        => Assert.Equal(expectedBase, PakClassifier.IsBaseGamePak(name, size));

    [Fact]
    public void Size_alone_flags_an_unconventionally_named_huge_pak_as_base()
        => Assert.True(PakClassifier.IsBaseGamePak("Witchfire-WindowsClient.pak", 3L * 1024 * 1024 * 1024));

    [Fact]
    public void Name_alone_flags_a_modestly_sized_base_chunk_as_base()
        => Assert.True(PakClassifier.IsBaseGamePak("pakchunk12-WindowsNoEditor.pak", 2 * 1024 * 1024));

    [Fact]
    public void A_normal_mod_pak_is_not_base()
        => Assert.False(PakClassifier.IsBaseGamePak("CoolWeapon_P.pak", 5 * 1024 * 1024));

    [Fact]
    public void Accepted_edge_a_mod_named_like_a_base_pak_is_treated_as_base()
        => Assert.True(PakClassifier.IsBaseGamePak("pakchunk0-WindowsNoEditor.pak", 3 * 1024));

    [Fact]
    public void Case_insensitive_on_the_name_pattern()
        => Assert.True(PakClassifier.IsBaseGamePak("PakChunk5-WindowsNoEditor.PAK", 1 * 1024 * 1024));
}
