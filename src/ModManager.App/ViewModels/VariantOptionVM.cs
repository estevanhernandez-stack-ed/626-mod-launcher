using CommunityToolkit.Mvvm.ComponentModel;

namespace ModManager.App.ViewModels;

/// <summary>
/// One selectable option (level/variant) of a multi-variant mod family — e.g. "10X" / "20X" for a
/// Faster Ships family. They're the same logical mod; each option is a separate variant file that can
/// be toggled independently. Rendered as an inline chip on the family's single row.
/// </summary>
public sealed partial class VariantOptionVM : ObservableObject
{
    public string ModName { get; }   // the underlying mod key to enable/disable
    public string Label { get; }     // short option label (the variant tag, e.g. "10X")
    public bool CanToggle { get; }
    [ObservableProperty] private bool enabled;

    public VariantOptionVM(string modName, string label, bool enabled, bool canToggle)
    {
        ModName = modName;
        Label = label;
        this.enabled = enabled;
        CanToggle = canToggle;
    }
}
