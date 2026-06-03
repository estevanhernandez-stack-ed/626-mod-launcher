using Microsoft.UI.Xaml.Controls;

namespace ModManager.App.Vortex;

public sealed partial class VortexTakeoverDialog : ContentDialog
{
    public VortexTakeoverDialog(string modName)
    {
        InitializeComponent();
        BodyText.Text = $"\"{modName}\" lives in a folder Vortex used to deploy. Take it over so you can manage it here?";
    }
}
