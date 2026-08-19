using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ModManager.Core;

namespace ModManager.App.ViewModels;

/// <summary>
/// One chip in the game-state strip — a thin wrapper over the pure <see cref="GameStateChip"/> that
/// Core ranked, adding only the things a view needs: an automation identity, a brush, and the two
/// commands.
///
/// <para><b>The identity is the fact, not the widget.</b> <c>StateChip.ban-risk</c> was on the old
/// inline warning before this strip existed, so the harness assertion that proves the highest-
/// consequence line is reachable stayed green across the move. Ids are bound rather than static
/// because a static id inside a DataTemplate repeats on every chip — see
/// <c>.claude/rules/automation-ids.md</c>.</para>
/// </summary>
public sealed partial class GameStateChipViewModel : ObservableObject
{
    private readonly GameStateChip _chip;
    private readonly Action<GameStateChipViewModel> _onSelect;
    private readonly Action<string> _onAct;
    private readonly Action<string> _onDismiss;

    public GameStateChipViewModel(
        GameStateChip chip,
        Action<GameStateChipViewModel> onSelect,
        Action<string> onAct,
        Action<string> onDismiss)
    {
        _chip = chip;
        _onSelect = onSelect;
        _onAct = onAct;
        _onDismiss = onDismiss;
    }

    public string Id => _chip.Id;
    public string Label => _chip.Label;
    public string Detail => _chip.Detail;
    public string ActionLabel => _chip.ActionLabel ?? "";
    public bool Dismissible => _chip.Dismissible;
    public GameStateSeverity Severity => _chip.Severity;

    public string ChipAutomationId => "StateChip." + _chip.Id;
    public string ActionAutomationId => "StateChipAction." + _chip.Id;
    public string DismissAutomationId => "StateChipDismiss." + _chip.Id;

    /// <summary>What a screen reader announces. The full sentence, not the label — "BAN RISK" on its
    /// own tells someone who has never modded exactly nothing.</summary>
    public string ChipAutomationName => _chip.Detail;

    public string DismissAutomationName => "Dismiss " + _chip.Label;

    public Visibility ActionVisibility =>
        string.IsNullOrEmpty(_chip.ActionLabel) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DismissVisibility => _chip.Dismissible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The warning glyph for anything that costs something, the info glyph for the one state
    /// that costs nothing.</summary>
    public string Glyph => _chip.Severity == GameStateSeverity.Info ? "" : "";

    /// <summary>The LIVE brush instance the theme service mutates, never a new one — a fresh brush
    /// would freeze this chip's colour at the moment the strip was built and stop following theme
    /// changes. Same reasoning as <c>.claude/rules/vsm-danger-buttons.md</c>.</summary>
    public Brush Accent
    {
        get
        {
            var key = _chip.Severity switch
            {
                GameStateSeverity.Danger => "ThemeDanger",
                GameStateSeverity.Warning => "ThemeAccent",
                _ => "ThemeInkSoft",
            };
            return (Brush)Application.Current.Resources[key];
        }
    }

    /// <summary>True when this chip's sentence is the one showing under the strip.</summary>
    [ObservableProperty]
    private bool isExpanded;

    [RelayCommand]
    private void Select() => _onSelect(this);

    [RelayCommand]
    private void Act() => _onAct(_chip.Id);

    [RelayCommand]
    private void Dismiss() => _onDismiss(_chip.Id);
}
