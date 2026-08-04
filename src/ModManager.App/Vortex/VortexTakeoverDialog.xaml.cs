using Microsoft.UI.Xaml.Controls;

namespace ModManager.App.Vortex;

public sealed partial class VortexTakeoverDialog : ContentDialog
{
    public VortexTakeoverDialog(string modName)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this); // vibe-glow wave 1: popup-scope theme brushes
        BodyText.Text = $"\"{modName}\" lives in a folder Vortex used to deploy. Take it over so you can manage it here?";
    }
}
