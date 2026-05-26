using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.Tests.SaveEditor.FromSoft;

public class CharacterSlotTests
{
    [Fact]
    public void CharacterSlot_carries_identity_and_stats()
    {
        var slot = new CharacterSlot(
            SlotIndex: 0,
            Name: "Yuka",
            Class: "Vagabond",
            Level: 120,
            Runes: 198_500,
            Vig: 40, Mnd: 16, End: 30, Str: 50, Dex: 12, Int: 12, Fai: 12, Arc: 12,
            SteamId: "76561197969211145");

        Assert.Equal("Yuka", slot.Name);
        Assert.Equal(120, slot.Level);
        Assert.Equal(198_500u, slot.Runes);
        Assert.Equal(40, slot.Vig);
        Assert.Equal("76561197969211145", slot.SteamId);
    }

    [Fact]
    public void CharacterEdit_carries_changed_fields_only()
    {
        var edit = new CharacterEdit(
            Name: "Renamed",
            Runes: 1_000_000u,
            Vig: 50, Mnd: 16, End: 30, Str: 50, Dex: 12, Int: 12, Fai: 12, Arc: 12);

        Assert.Equal("Renamed", edit.Name);
        Assert.Equal(1_000_000u, edit.Runes);
        Assert.Equal(50, edit.Vig);
    }
}
