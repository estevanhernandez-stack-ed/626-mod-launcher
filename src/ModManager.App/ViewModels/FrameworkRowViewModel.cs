using Microsoft.UI.Xaml;
using ModManager.Core.Frameworks;

namespace ModManager.App.ViewModels;

/// <summary>
/// One installed-framework button in the slim row. Wraps the install manifest + the computed
/// "has an editable config" visibility so the FRAMEWORKS template binds plain <c>x:Bind</c> (no
/// function bindings). The name button opens the how-to toast; the pencil (shown only when
/// <see cref="ConfigVisibility"/> is Visible) opens the framework's settings INI in the editor.
/// </summary>
public sealed class FrameworkRowViewModel
{
    public FrameworkInstallManifest Manifest { get; }
    public string DisplayName => Manifest.DisplayName;
    public string EditAutomationName => $"Edit {Manifest.DisplayName} settings"; // per-item UIA name (F-065)

    /// <summary>Stable id for the chip. Bound, not static — a static id inside a DataTemplate repeats
    /// on every chip and the walker picks whichever it reached first.</summary>
    public string ChipAutomationId => $"FrameworkChip.{Manifest.FrameworkId}";

    /// <summary>Wave 9. Uninstall lived ONLY in the Settings inventory, so moving that inventory out
    /// without bringing this along would have removed something the user can do. It belongs beside
    /// the chips, where the frameworks already are.</summary>
    public string UninstallAutomationId => $"FrameworkUninstall.{Manifest.FrameworkId}";
    public string UninstallAutomationName => $"Uninstall {Manifest.DisplayName}";

    /// <summary>The framework's editable .ini config files (absolute paths), from the manifest.</summary>
    public IReadOnlyList<string> ConfigFiles { get; }

    public Visibility ConfigVisibility => ConfigFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public FrameworkRowViewModel(FrameworkInstallManifest manifest)
    {
        Manifest = manifest;
        ConfigFiles = FrameworkUsage.ConfigFiles(manifest);
    }
}
