using Microsoft.UI.Xaml.Controls;
using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.App;

public sealed partial class CharacterEditDialog : ContentDialog
{
    public CharacterEditDialog(CharacterSlot slot)
    {
        InitializeComponent();
        IntroText.Text = $"Editing \"{slot.Name}\" — {slot.Class}, currently Lv {slot.Level}.";
        NameBox.Text = slot.Name;
        RunesBox.Value = slot.Runes;
        VigBox.Value = slot.Vig; MndBox.Value = slot.Mnd;
        EndBox.Value = slot.End; StrBox.Value = slot.Str;
        DexBox.Value = slot.Dex; IntBox.Value = slot.Int;
        FaiBox.Value = slot.Fai; ArcBox.Value = slot.Arc;
        UpdateLevelText();
    }

    /// <summary>The edit the user wants to apply. Read by the caller on Primary result.</summary>
    public CharacterEdit GetEdit() => new(
        Name: (NameBox.Text ?? "").Trim(),
        Runes: ToUInt32(RunesBox.Value),
        Vig: ToByte(VigBox.Value), Mnd: ToByte(MndBox.Value),
        End: ToByte(EndBox.Value), Str: ToByte(StrBox.Value),
        Dex: ToByte(DexBox.Value), Int: ToByte(IntBox.Value),
        Fai: ToByte(FaiBox.Value), Arc: ToByte(ArcBox.Value));

    /// <summary>True when the form passes light validation (non-empty name within 16 chars).</summary>
    public bool IsValid()
    {
        var name = (NameBox.Text ?? "").Trim();
        return name.Length is > 0 and <= 16;
    }

    private void OnStatChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        => UpdateLevelText();

    private void UpdateLevelText()
    {
        var sum = ToByte(VigBox.Value) + ToByte(MndBox.Value) + ToByte(EndBox.Value) + ToByte(StrBox.Value)
                + ToByte(DexBox.Value) + ToByte(IntBox.Value) + ToByte(FaiBox.Value) + ToByte(ArcBox.Value);
        LevelText.Text = $"→ Level {sum - 79} (recomputed from stats)";
    }

    private static uint ToUInt32(double v) => double.IsNaN(v) ? 0u : (uint)Math.Clamp(v, 0, 999_999_999);
    private static byte ToByte(double v) => double.IsNaN(v) ? (byte)1 : (byte)Math.Clamp(v, 1, 99);
}
