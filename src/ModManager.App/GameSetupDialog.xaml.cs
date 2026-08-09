using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.Services;
using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.App;

/// <summary>
/// What this game's registration claims, next to what the launcher actually found — and, behind an
/// expander, the fields to change it.
///
/// <para>Diagnosis first, on purpose. The common outcome is that NOTHING is wrong: a game whose mods
/// load by a route the registration does not describe is normal (Elden Ring's eleven mods load by
/// direct-inject while it declares a Mod Engine 2 folder that does not exist). A surface that opened
/// straight into editable fields would imply something needed editing.</para>
///
/// <para>One dialog rather than two because WinUI 3 permits one ContentDialog per XamlRoot; chaining
/// diagnose to edit to confirm would be two nested hand-offs. This leaves exactly one, for the
/// move-or-pin confirm.</para>
/// </summary>
public sealed partial class GameSetupDialog : ContentDialog
{
    private readonly GameEntry _game;
    private readonly RegistrationRepairService _repair;
    private readonly GameShape _shape;

    public GameSetupDialog(GameEntry game, RegistrationRepairService repair)
    {
        InitializeComponent();
        _game = game;
        _repair = repair;
        _shape = repair.Shape(game);
        DialogTheming.Apply(this);   // popup-scope theme brushes
        RenderDiagnosis();
    }

    private void RenderDiagnosis()
    {
        ModsFoundText.Text = _shape.ModCount switch
        {
            0 => "None.",
            1 => "1 mod.",
            _ => $"{_shape.ModCount} mods.",
        };

        // A loader explains why sibling mods load from a folder the registration never mentions —
        // without it named, the drift below reads as misconfiguration with no cause.
        var hasLoaders = _shape.Loaders.Count > 0;
        LoadedByLabel.Visibility = hasLoaders ? Visibility.Visible : Visibility.Collapsed;
        LoadedByText.Visibility = hasLoaders ? Visibility.Visible : Visibility.Collapsed;
        LoadedByText.Text = string.Join(", ", _shape.Loaders);

        var roots = _shape.ContentRoots
            .Select(r => string.IsNullOrEmpty(r.RelativePath) ? "the game folder" : r.RelativePath)
            .ToList();
        LivingInLabel.Visibility = roots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LivingInText.Visibility = roots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LivingInText.Text = string.Join(", ", roots);

        DeclaredText.Text = string.Join(", ", _shape.DeclaredLocations
            .Select(d => d.Exists ? d.Path : d.Path + "  (this folder doesn't exist)"));

        // Rendered verbatim: GameShape already states whether drift is a problem, and re-wording it
        // here would let the dialog and the MCP tool tell the user two different stories.
        VerdictText.Text = string.Join(" ", _shape.Notes);
    }
}
