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

    /// <summary>The framework's editable .ini config files (absolute paths), from the manifest.</summary>
    public IReadOnlyList<string> ConfigFiles { get; }

    public Visibility ConfigVisibility => ConfigFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public FrameworkRowViewModel(FrameworkInstallManifest manifest)
    {
        Manifest = manifest;
        ConfigFiles = FrameworkUsage.ConfigFiles(manifest);
    }
}
